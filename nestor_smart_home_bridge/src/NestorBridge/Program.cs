using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
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
builder.Services.AddSingleton<IMqttBridge, MqttBridge>();
builder.Services.AddSingleton<IHaWebSocketClient, HaWebSocketClient>();
builder.Services.AddSingleton<HaServiceCaller>();
builder.Services.AddSingleton<CommandTranslator>();
builder.Services.AddSingleton<TelemetryTranslator>();

// Bootstrap FIRST
builder.Services.AddHostedService<BootstrapService>();
builder.Services.AddHostedService<DownlinkWorker>();
builder.Services.AddHostedService<UplinkWorker>();
builder.Services.AddHostedService<HeartbeatWorker>();

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
  private readonly IHaWebSocketClient _haClient;
  private readonly ILogger<BootstrapService> _logger;

  public BootstrapService(IMqttBridge mqtt, IHaWebSocketClient haClient, ILogger<BootstrapService> logger)
  {
    _mqtt = mqtt;
    _haClient = haClient;
    _logger = logger;
  }

  public async Task StartAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Nestor Bridge starting — connecting to HA WebSocket and MQTT...");

    await _haClient.ConnectAsync(cancellationToken);
    _logger.LogInformation("HA WebSocket connected");

    await _mqtt.ConnectAsync(cancellationToken);
    _logger.LogInformation("MQTT connected");
  }

  public async Task StopAsync(CancellationToken cancellationToken)
  {
    _logger.LogInformation("Nestor Bridge shutting down...");
    await _mqtt.DisconnectAsync(cancellationToken);
    await _haClient.DisconnectAsync(cancellationToken);
  }
}
