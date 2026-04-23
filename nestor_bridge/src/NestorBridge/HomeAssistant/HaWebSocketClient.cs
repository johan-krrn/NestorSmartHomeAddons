using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.HomeAssistant;

public sealed class HaWebSocketClient : IHaWebSocketClient, IAsyncDisposable
{
  private ClientWebSocket? _ws;
  private readonly ILogger<HaWebSocketClient> _logger;
  private readonly string _supervisorToken;
  private readonly string _wsEndpoint;
  private int _messageId;
  private CancellationTokenSource? _loopCts;
  private Task? _receiveLoop;
  private readonly ConcurrentDictionary<int, TaskCompletionSource<HaMessage>> _pending = new();

  private int _reconnectDelayMs = 1000;
  private const int MaxReconnectDelayMs = 60_000;
  private const string DefaultWsEndpoint = "ws://supervisor/core/websocket";
  // Track the reconnect task so it is not fire-and-forget
  private Task _reconnectTask = Task.CompletedTask;

  public event Func<HaEvent, Task>? StateChanged;

  public HaWebSocketClient(IOptions<BridgeOptions> options, ILogger<HaWebSocketClient> logger)
  {
    _logger = logger;
    var opts = options.Value;

    // Endpoint: options override > env var > default supervisor endpoint
    _wsEndpoint = !string.IsNullOrWhiteSpace(opts.HaWsEndpoint)
        ? opts.HaWsEndpoint
        : Environment.GetEnvironmentVariable("HA_WS_ENDPOINT") ?? DefaultWsEndpoint;

    // Token: env var (injected by Supervisor in prod, set manually in dev)
    _supervisorToken = Environment.GetEnvironmentVariable("SUPERVISOR_TOKEN")
                       ?? Environment.GetEnvironmentVariable("HASSIO_TOKEN")
                       ?? Environment.GetEnvironmentVariable("HA_TOKEN")
                       ?? throw new InvalidOperationException(
                           "No HA token found. Set SUPERVISOR_TOKEN (prod) or HA_TOKEN (local dev).");
  }

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    _ws?.Dispose();
    _ws = new ClientWebSocket();

    _logger.LogInformation("Connecting to HA WebSocket at {Endpoint}", _wsEndpoint);
    await _ws.ConnectAsync(new Uri(_wsEndpoint), cancellationToken);

    // Step 1: Receive auth_required
    var authRequired = await ReceiveMessageAsync(cancellationToken);
    if (authRequired.Type != "auth_required")
      throw new InvalidOperationException($"Expected auth_required, got: {authRequired.Type}");

    _logger.LogInformation("HA version: {Version}", authRequired.HaVersion);

    // Step 2: Send auth
    var authMsg = JsonSerializer.Serialize(new { type = "auth", access_token = _supervisorToken });
    await SendRawAsync(authMsg, cancellationToken);

    // Step 3: Receive auth_ok or auth_invalid
    var authResult = await ReceiveMessageAsync(cancellationToken);
    if (authResult.Type == "auth_invalid")
      throw new UnauthorizedAccessException("HA WebSocket auth failed: invalid token");
    if (authResult.Type != "auth_ok")
      throw new InvalidOperationException($"Expected auth_ok, got: {authResult.Type}");

    _logger.LogInformation("HA WebSocket authenticated successfully");

    // Start receive loop
    _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _receiveLoop = Task.Run(() => ReceiveLoopAsync(_loopCts.Token), _loopCts.Token);

    // Subscribe to state_changed events
    await SubscribeEventsAsync("state_changed", cancellationToken);
    _reconnectDelayMs = 1000;
  }

  public async Task<HaMessage> CallServiceAsync(string domain, string service, string? entityId,
      Dictionary<string, object>? serviceData, CancellationToken cancellationToken)
  {
    var id = Interlocked.Increment(ref _messageId);
    var msg = new HaCallServiceMessage
    {
      Id = id,
      Domain = domain,
      Service = service,
      ServiceData = serviceData,
      Target = string.IsNullOrEmpty(entityId) ? null : new HaTarget { EntityId = entityId }
    };

    var tcs = new TaskCompletionSource<HaMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pending[id] = tcs;

    var json = JsonSerializer.Serialize(msg);
    await SendRawAsync(json, cancellationToken);

    // Await result with timeout
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromSeconds(15));
    await using (cts.Token.Register(() => tcs.TrySetCanceled()))
    {
      return await tcs.Task;
    }
  }

  public async Task<JsonElement> GetStatesAsync(CancellationToken cancellationToken)
  {
    var id = Interlocked.Increment(ref _messageId);

    var tcs = new TaskCompletionSource<HaMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pending[id] = tcs;

    var json = JsonSerializer.Serialize(new { id, type = "get_states" });
    await SendRawAsync(json, cancellationToken);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromSeconds(30));
    await using (cts.Token.Register(() => tcs.TrySetCanceled()))
    {
      var result = await tcs.Task;
      if (result.Success != true)
        throw new InvalidOperationException($"get_states failed: {result.Error?.Message}");

      return result.Result ?? default;
    }
  }

  public async Task DisconnectAsync(CancellationToken cancellationToken)
  {
    _loopCts?.Cancel();
    if (_ws is { State: WebSocketState.Open })
    {
      try
      {
        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", cancellationToken);
      }
      catch (Exception ex)
      {
        _logger.LogDebug(ex, "Error during WS close");
      }
    }
  }

  private async Task SubscribeEventsAsync(string eventType, CancellationToken cancellationToken)
  {
    var id = Interlocked.Increment(ref _messageId);
    var msg = JsonSerializer.Serialize(new { id, type = "subscribe_events", event_type = eventType });

    var tcs = new TaskCompletionSource<HaMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    _pending[id] = tcs;

    await SendRawAsync(msg, cancellationToken);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    cts.CancelAfter(TimeSpan.FromSeconds(10));
    await using (cts.Token.Register(() => tcs.TrySetCanceled()))
    {
      var result = await tcs.Task;
      if (result.Success != true)
        throw new InvalidOperationException($"Failed to subscribe to {eventType}: {result.Error?.Message}");
    }

    _logger.LogInformation("Subscribed to HA event: {EventType}", eventType);
  }

  private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
  {
    var buffer = new byte[8192];

    try
    {
      while (!cancellationToken.IsCancellationRequested && _ws?.State == WebSocketState.Open)
      {
        var msg = await ReceiveMessageAsync(cancellationToken);

        // Route result messages to pending calls
        if (msg.Type == "result" && msg.Id.HasValue)
        {
          if (_pending.TryRemove(msg.Id.Value, out var tcs))
            tcs.TrySetResult(msg);
          continue;
        }

        // Route event messages
        if (msg.Type == "event" && msg.Event is not null)
        {
          // Also complete subscription result if pending
          if (msg.Id.HasValue && _pending.TryRemove(msg.Id.Value, out var tcs))
            tcs.TrySetResult(msg);

          if (msg.Event.EventType == "state_changed" && StateChanged is not null)
          {
            try
            {
              await StateChanged.Invoke(msg.Event);
            }
            catch (Exception ex)
            {
              _logger.LogError(ex, "Error in StateChanged handler");
            }
          }
          continue;
        }
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Graceful shutdown
    }
    catch (WebSocketException ex)
    {
      _logger.LogWarning(ex, "HA WebSocket connection lost");
      // Store the task — avoids fire-and-forget; errors are logged inside ReconnectAsync
      _reconnectTask = ReconnectAsync();
    }
  }

  private async Task ReconnectAsync()
  {
    while (true)
    {
      _logger.LogInformation("Attempting HA WebSocket reconnection in {Delay}ms...", _reconnectDelayMs);
      await Task.Delay(_reconnectDelayMs);
      _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, MaxReconnectDelayMs);

      try
      {
        await ConnectAsync(CancellationToken.None);
        _logger.LogInformation("HA WebSocket reconnected");
        return;
      }
      catch (UnauthorizedAccessException ex)
      {
        // Token is invalid — retrying would loop forever; require a restart
        _logger.LogCritical(ex,
            "HA token is invalid. Reconnection aborted. Restart the add-on with a valid token.");
        return;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "HA WebSocket reconnection failed");
      }
    }
  }

  private async Task<HaMessage> ReceiveMessageAsync(CancellationToken cancellationToken)
  {
    var buffer = new byte[8192];
    using var ms = new MemoryStream();

    WebSocketReceiveResult result;
    do
    {
      result = await _ws!.ReceiveAsync(buffer, cancellationToken);
      ms.Write(buffer, 0, result.Count);
    }
    while (!result.EndOfMessage);

    if (result.MessageType == WebSocketMessageType.Close)
      throw new WebSocketException("Server closed the connection");

    var json = Encoding.UTF8.GetString(ms.ToArray());
    _logger.LogTrace("WS RX: {Json}", json);

    return JsonSerializer.Deserialize<HaMessage>(json)
           ?? throw new InvalidOperationException("Failed to deserialize HA message");
  }

  private async Task SendRawAsync(string json, CancellationToken cancellationToken)
  {
    _logger.LogTrace("WS TX: {Json}", json);
    var bytes = Encoding.UTF8.GetBytes(json);
    await _ws!.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
  }

  public async ValueTask DisposeAsync()
  {
    await DisconnectAsync(CancellationToken.None);
    _loopCts?.Dispose();
    _ws?.Dispose();
  }
}
