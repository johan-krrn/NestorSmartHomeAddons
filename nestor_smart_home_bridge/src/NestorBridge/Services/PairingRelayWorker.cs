using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant.Models;
using NestorBridge.Mqtt;
using NestorBridge.Web;

namespace NestorBridge.Services;

/// <summary>
/// Subscribes to the local Mosquitto broker via <see cref="ILocalMqttBridge"/> and relays
/// each incoming message to the cloud MQTT broker as a <see cref="PairingStatusPayload"/>
/// on the topic <c>devices/{boxId}/events/pairing_status</c>.
///
/// The worker is a no-op when <c>local_mqtt.enabled</c> is false.
/// </summary>
public sealed class PairingRelayWorker : IHostedService
{
  private readonly ILocalMqttBridge _localMqtt;
  private readonly IMqttBridge _cloudMqtt;
  private readonly BridgeOptions _options;
  private readonly MessageLog _messageLog;
  private readonly ILogger<PairingRelayWorker> _logger;

  public PairingRelayWorker(
      ILocalMqttBridge localMqtt,
      IMqttBridge cloudMqtt,
      IOptions<BridgeOptions> options,
      MessageLog messageLog,
      ILogger<PairingRelayWorker> logger)
  {
    _localMqtt = localMqtt;
    _cloudMqtt = cloudMqtt;
    _options = options.Value;
    _messageLog = messageLog;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (!_options.LocalMqtt.Enabled)
    {
      _logger.LogInformation("PairingRelayWorker disabled (local_mqtt.enabled=false)");
      return Task.CompletedTask;
    }

    _localMqtt.MessageReceived += OnLocalMessageAsync;
    _logger.LogInformation("PairingRelayWorker started — relaying {Count} topic(s) to cloud",
        _options.LocalMqtt.Topics.Count);

    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _localMqtt.MessageReceived -= OnLocalMessageAsync;
    return Task.CompletedTask;
  }

  private async Task OnLocalMessageAsync(string sourceTopic, byte[] payload)
  {
    var cloudTopic = Topics.EventsPairingStatus(_options.BoxId);

    // Try to preserve the original payload as a JSON object.
    // Fall back to {"raw": "..."} for non-JSON messages (e.g. plain-text log lines).
    JsonElement dataElement;
    try
    {
      using var doc = JsonDocument.Parse(payload);
      dataElement = doc.RootElement.Clone();
    }
    catch (JsonException)
    {
      dataElement = JsonSerializer.SerializeToElement(
          new { raw = Encoding.UTF8.GetString(payload) });
    }

    var envelope = new PairingStatusPayload
    {
      BoxId = _options.BoxId,
      SourceTopic = sourceTopic,
      Timestamp = DateTime.UtcNow.ToString("o"),
      Data = dataElement
    };

    var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope);

    try
    {
      await _cloudMqtt.PublishAsync(cloudTopic, bytes,
          MqttQualityOfServiceLevel.AtMostOnce, CancellationToken.None);

      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound, cloudTopic,
          Encoding.UTF8.GetString(bytes)));

      _logger.LogDebug("Pairing event relayed {SourceTopic} → {CloudTopic}", sourceTopic, cloudTopic);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to relay pairing event from {SourceTopic}", sourceTopic);
    }
  }
}
