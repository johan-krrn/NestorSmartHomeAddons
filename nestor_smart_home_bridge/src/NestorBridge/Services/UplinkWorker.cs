using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant;
using NestorBridge.HomeAssistant.Models;
using NestorBridge.Mqtt;
using NestorBridge.Translation;
using NestorBridge.Web;

namespace NestorBridge.Services;

public sealed class UplinkWorker : IHostedService
{
  private readonly IHaWebSocketClient _haClient;
  private readonly IMqttBridge _mqtt;
  private readonly TelemetryTranslator _translator;
  private readonly BridgeOptions _options;
  private readonly MessageLog _messageLog;
  private readonly ILogger<UplinkWorker> _logger;

  public UplinkWorker(
      IHaWebSocketClient haClient,
      IMqttBridge mqtt,
      TelemetryTranslator translator,
      IOptions<BridgeOptions> options,
      MessageLog messageLog,
      ILogger<UplinkWorker> logger)
  {
    _haClient = haClient;
    _mqtt = mqtt;
    _translator = translator;
    _options = options.Value;
    _messageLog = messageLog;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _haClient.StateChanged += OnStateChangedAsync;
    _logger.LogInformation("UplinkWorker started");
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _haClient.StateChanged -= OnStateChangedAsync;
    _logger.LogInformation("UplinkWorker stopped");
    return Task.CompletedTask;
  }

  private async Task OnStateChangedAsync(HaEvent haEvent)
  {
    // ── Raw event streaming → devices/{boxId}/events/state_changed ──
    try
    {
      var rawEventBytes = JsonSerializer.SerializeToUtf8Bytes(haEvent);
      var eventTopic = Topics.EventsStateChanged(_options.BoxId);

      await _mqtt.PublishAsync(eventTopic, rawEventBytes,
          MqttQualityOfServiceLevel.AtMostOnce, CancellationToken.None);

      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound, eventTopic,
          System.Text.Encoding.UTF8.GetString(rawEventBytes)));

      _logger.LogDebug("Raw state_changed event published for {EntityId}",
          haEvent.Data?.NewState?.EntityId ?? "unknown");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to publish raw state_changed event");
    }

    // ── Filtered telemetry → devices/{boxId}/telemetry/state/{entityId} ──
    var result = _translator.Translate(haEvent);
    if (result is null)
      return;

    var (entityId, payload) = result.Value;
    var topic = Topics.TelemetryState(_options.BoxId, entityId);

    try
    {
      await _mqtt.PublishAsync(topic, payload,
          MqttQualityOfServiceLevel.AtMostOnce, CancellationToken.None);

      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound, topic,
          System.Text.Encoding.UTF8.GetString(payload)));

      _logger.LogDebug("Telemetry published for {EntityId}", entityId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to publish telemetry for {EntityId}", entityId);
    }
  }
}
