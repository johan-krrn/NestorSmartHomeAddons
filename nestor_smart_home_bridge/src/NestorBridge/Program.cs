using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant;
using NestorBridge.Mqtt;
using NestorBridge.Services;
using NestorBridge.Translation;
using NestorBridge.Web;

var builder = WebApplication.CreateBuilder(args);

// Config from /data/options.json (injected by HA Supervisor)
builder.Configuration.AddHaOptionsJson();

// Bind options and validate
builder.Services.Configure<BridgeOptions>(builder.Configuration);
var options = builder.Configuration.Get<BridgeOptions>();
if (options is null)
  throw new InvalidOperationException("Failed to load BridgeOptions from /data/options.json");
options.Validate();

// ── Logging ──────────────────────────────────────────────────────────
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
  o.IncludeScopes = true;
  o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
  o.UseUtcTimestamp = true;
});

if (Enum.TryParse<LogLevel>(options.LogLevel, ignoreCase: true, out var logLevel))
{
  builder.Logging.SetMinimumLevel(logLevel);
}

// ── Kestrel: listen on port 8099 (ingress port) ─────────────────────
builder.WebHost.UseUrls("http://0.0.0.0:8099");

// ── Services ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<MessageLog>();
builder.Services.AddSingleton<ConnectionStatusTracker>();
builder.Services.AddSingleton<ExposedEntitiesStore>();
builder.Services.AddSingleton<IMqttBridge, MqttBridge>();
builder.Services.AddSingleton<ILocalMqttBridge, LocalMqttBridge>();
builder.Services.AddSingleton<IHaWebSocketClient, HaWebSocketClient>();

// ── HTTP client for HA Config REST API ───────────────────────────────
// The HttpClient is created once (singleton lifetime is acceptable here:
// the target is http://supervisor/core — a static internal address with no DNS rotation).
var haToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN")
              ?? Environment.GetEnvironmentVariable("HASSIO_TOKEN")
              ?? Environment.GetEnvironmentVariable("HA_TOKEN")
              ?? string.Empty;
var haRestBaseUrl = Environment.GetEnvironmentVariable("HA_REST_API_URL") ?? "http://supervisor/core/api/";
builder.Services.AddSingleton<IHaRestClient>(sp =>
{
  var httpClient = new HttpClient { BaseAddress = new Uri(haRestBaseUrl) };
  if (!string.IsNullOrEmpty(haToken))
    httpClient.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", haToken);
  return new HaRestClient(httpClient, sp.GetRequiredService<ILogger<HaRestClient>>());
});

builder.Services.AddSingleton<HaServiceCaller>();
builder.Services.AddSingleton<CommandTranslator>();
builder.Services.AddSingleton<TelemetryTranslator>();

// Bootstrap FIRST
builder.Services.AddHostedService<BootstrapService>();
builder.Services.AddHostedService<DownlinkWorker>();
builder.Services.AddHostedService<UplinkWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();
builder.Services.AddHostedService<PairingRelayWorker>();

var app = builder.Build();

// ── Static files for the dashboard ───────────────────────────────────
var wwwroot = Path.Combine(AppContext.BaseDirectory, "Web", "wwwroot");
if (Directory.Exists(wwwroot))
{
  app.UseStaticFiles(new StaticFileOptions
  {
    FileProvider = new PhysicalFileProvider(wwwroot),
    RequestPath = ""
  });
}

// ── API endpoints ────────────────────────────────────────────────────
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// GET /api/messages — recent message history
app.MapGet("/api/messages", (MessageLog log) =>
{
  return Results.Json(log.GetRecent(200), jsonOpts);
});

// GET /api/messages/stream — SSE for live messages
app.MapGet("/api/messages/stream", async (MessageLog log, HttpContext ctx, CancellationToken ct) =>
{
  ctx.Response.ContentType = "text/event-stream";
  ctx.Response.Headers.CacheControl = "no-cache";
  ctx.Response.Headers.Connection = "keep-alive";

  using var sub = log.Subscribe();

  await foreach (var entry in sub.Reader.ReadAllAsync(ct))
  {
    var json = JsonSerializer.Serialize(entry, jsonOpts);
    await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
    await ctx.Response.Body.FlushAsync(ct);
  }
});

// GET /api/status — connection statuses
app.MapGet("/api/status", (ConnectionStatusTracker tracker) =>
{
  return Results.Json(tracker.GetAll(), jsonOpts);
});

// GET /api/entities/exposed — list of exposed entities
app.MapGet("/api/entities/exposed", (ExposedEntitiesStore store) =>
{
  return Results.Json(store.GetAll(), jsonOpts);
});

// POST /api/entities/exposed — add an entity to the exposed list
app.MapPost("/api/entities/exposed", (ExposedEntitiesStore store, HttpContext ctx) =>
{
  using var reader = new StreamReader(ctx.Request.Body);
  var body = reader.ReadToEndAsync().GetAwaiter().GetResult();
  var doc = JsonDocument.Parse(body);
  var entityId = doc.RootElement.GetProperty("entityId").GetString()!;
  var friendlyName = doc.RootElement.TryGetProperty("friendlyName", out var fn)
      ? fn.GetString() : null;

  try
  {
    var entity = store.Add(entityId, friendlyName);
    return Results.Json(entity, jsonOpts);
  }
  catch (InvalidOperationException ex)
  {
    return Results.Conflict(new { error = ex.Message });
  }
});

// DELETE /api/entities/exposed/{entityId} — remove an entity
app.MapDelete("/api/entities/exposed/{entityId}", (string entityId, ExposedEntitiesStore store) =>
{
  return store.Remove(entityId) ? Results.Ok() : Results.NotFound();
});

// GET /api/entities/search — search HA entities via WebSocket
app.MapGet("/api/entities/search", async (string? q, IHaWebSocketClient haClient, CancellationToken ct) =>
{
  var states = await haClient.GetStatesAsync(ct);
  var results = new List<object>();

  if (states.ValueKind == JsonValueKind.Array)
  {
    foreach (var entity in states.EnumerateArray())
    {
      var entityId = entity.GetProperty("entity_id").GetString() ?? "";
      var friendlyName = "";
      if (entity.TryGetProperty("attributes", out var attrs) &&
          attrs.TryGetProperty("friendly_name", out var fn))
        friendlyName = fn.GetString() ?? "";

      if (string.IsNullOrEmpty(q) ||
          entityId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
          friendlyName.Contains(q, StringComparison.OrdinalIgnoreCase))
      {
        results.Add(new { entityId, friendlyName });
      }

      if (results.Count >= 50) break;
    }
  }

  return Results.Json(results, jsonOpts);
});

// GET / — serve the dashboard
app.MapFallbackToFile("index.html", new StaticFileOptions
{
  FileProvider = new PhysicalFileProvider(wwwroot)
});

await app.RunAsync();

/// <summary>
/// Ensures MQTT and HA WebSocket are connected before workers start processing.
/// </summary>
file sealed class BootstrapService : IHostedService
{
  private readonly IMqttBridge _mqtt;
  private readonly ILocalMqttBridge _localMqtt;
  private readonly IHaWebSocketClient _haClient;
  private readonly BridgeOptions _options;
  private readonly ConnectionStatusTracker _tracker;
  private readonly ILogger<BootstrapService> _logger;

  public BootstrapService(
      IMqttBridge mqtt,
      ILocalMqttBridge localMqtt,
      IHaWebSocketClient haClient,
      IOptions<BridgeOptions> options,
      ConnectionStatusTracker tracker,
      ILogger<BootstrapService> logger)
  {
    _mqtt = mqtt;
    _localMqtt = localMqtt;
    _haClient = haClient;
    _options = options.Value;
    _tracker = tracker;
    _logger = logger;
  }

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Nestor Bridge starting — connecting to HA WebSocket and MQTT...");

    _tracker.SetState(ConnectionStatusTracker.HaWebSocket, ConnectionState.Connecting);
    await _haClient.ConnectAsync(cancellationToken);
    _tracker.SetState(ConnectionStatusTracker.HaWebSocket, ConnectionState.Connected);
    _logger.LogInformation("HA WebSocket connected");

    _tracker.SetState(ConnectionStatusTracker.CloudMqtt, ConnectionState.Connecting);
    await _mqtt.ConnectAsync(cancellationToken);
    _tracker.SetState(ConnectionStatusTracker.CloudMqtt, ConnectionState.Connected);
    _logger.LogInformation("Cloud MQTT connected");

    if (_options.LocalMqtt.Enabled)
    {
      _tracker.SetState(ConnectionStatusTracker.LocalMqtt, ConnectionState.Connecting);
      await _localMqtt.ConnectAsync(cancellationToken);
      _tracker.SetState(ConnectionStatusTracker.LocalMqtt, ConnectionState.Connected);
      _logger.LogInformation("Local MQTT connected ({Host}:{Port})",
          _options.LocalMqtt.Host, _options.LocalMqtt.Port);
    }
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Nestor Bridge shutting down...");
    if (_options.LocalMqtt.Enabled)
      await _localMqtt.DisconnectAsync(cancellationToken);
    _tracker.SetState(ConnectionStatusTracker.LocalMqtt, ConnectionState.Disconnected);
    await _mqtt.DisconnectAsync(cancellationToken);
    _tracker.SetState(ConnectionStatusTracker.CloudMqtt, ConnectionState.Disconnected);
    await _haClient.DisconnectAsync(cancellationToken);
    _tracker.SetState(ConnectionStatusTracker.HaWebSocket, ConnectionState.Disconnected);
  }
}
