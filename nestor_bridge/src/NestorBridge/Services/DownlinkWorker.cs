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

/// <summary>
/// Downlink worker: receives MQTT commands from cloud, translates them to HA service calls,
/// and publishes ack results back to cloud.
/// </summary>
public sealed class DownlinkWorker : IHostedService
{
  private readonly IMqttBridge _mqtt;
  private readonly IHaWebSocketClient _haClient;
  private readonly HaServiceCaller _serviceCaller;
  private readonly CommandTranslator _translator;
  private readonly BridgeOptions _options;
  private readonly MessageLog _messageLog;
  private readonly ILogger<DownlinkWorker> _logger;

  public DownlinkWorker(
      IMqttBridge mqtt,
      IHaWebSocketClient haClient,
      HaServiceCaller serviceCaller,
      CommandTranslator translator,
      IOptions<BridgeOptions> options,
      MessageLog messageLog,
      ILogger<DownlinkWorker> logger)
  {
    _mqtt = mqtt;
    _haClient = haClient;
    _serviceCaller = serviceCaller;
    _translator = translator;
    _options = options.Value;
    _messageLog = messageLog;
    _logger = logger;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _mqtt.MessageReceived += OnMqttMessageAsync;
    _logger.LogInformation("DownlinkWorker started");
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _mqtt.MessageReceived -= OnMqttMessageAsync;
    _logger.LogInformation("DownlinkWorker stopped");
    return Task.CompletedTask;
  }

  private async Task OnMqttMessageAsync(string topic, byte[] payload)
  {
    var payloadStr = System.Text.Encoding.UTF8.GetString(payload);
    _logger.LogDebug("Downlink command received on {Topic}", topic);

    // Log inbound command
    _messageLog.Add(new MessageLogEntry(
        DateTime.UtcNow, MessageDirection.Inbound, topic, payloadStr));

    // ── Cloud request handling (devices/{boxId}/commands/requests) ─────
    if (string.Equals(topic, Topics.CloudRequests(_options.BoxId), StringComparison.Ordinal))
    {
      _ = Task.Run(() => HandleCloudRequestAsync(payloadStr));
      return;
    }

    // ── Existing command flow ────────────────────────────────────────
    var command = _translator.Translate(payload);
    if (command is null)
    {
      // Not a CloudCommand envelope — try MQTT passthrough:
      // extract the HA MQTT sub-topic from the MQTT topic path and forward the raw payload.
      var subTopic = Topics.ExtractSubTopic(_options.BoxId, topic);
      if (subTopic is null)
      {
        _logger.LogWarning("Malformed command on {Topic} and no sub-topic extractable, dropping", topic);
        return;
      }

      _logger.LogInformation("No CloudCommand parsed; forwarding raw payload to HA MQTT topic {SubTopic}", subTopic);

      var (ptSuccess, ptError) = await _serviceCaller.PublishMqttAsync(
          subTopic, payloadStr, CancellationToken.None);

      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound, subTopic, payloadStr,
          ptSuccess ? "mqtt-passthrough" : $"error: {ptError}"));
      return;
    }

    var (success, contextId, error) = await _serviceCaller.ExecuteCommandAsync(
        command, CancellationToken.None);

    var ackPayload = _translator.BuildAck(command.CommandId, success, error, contextId);
    var ackTopic = Topics.CommandAck(_options.BoxId, command.CommandId);

    await _mqtt.PublishAsync(ackTopic, ackPayload,
        MqttQualityOfServiceLevel.AtLeastOnce, CancellationToken.None);

    // Log outbound ack
    _messageLog.Add(new MessageLogEntry(
        DateTime.UtcNow, MessageDirection.Outbound, ackTopic,
        System.Text.Encoding.UTF8.GetString(ackPayload),
        success ? "success" : "error"));

    _logger.LogInformation("Command {CommandId} processed: {Status}",
        command.CommandId, success ? "success" : "error");
  }

  private async Task HandleCloudRequestAsync(string payloadStr)
  {
    CloudRequest? request = null;
    try
    {
      request = JsonSerializer.Deserialize<CloudRequest>(payloadStr);
    }
    catch (JsonException ex)
    {
      _logger.LogWarning(ex, "Failed to deserialize cloud request payload");
      return;
    }

    if (request is null || string.IsNullOrWhiteSpace(request.TargetConnectionId))
    {
      _logger.LogWarning("Cloud request missing TargetConnectionId, dropping");
      return;
    }

    _logger.LogInformation("Cloud request received: Command={Command}, ConnectionId={ConnectionId}",
        request.Command, request.TargetConnectionId);

    if (!string.Equals(request.Command, "get_states", StringComparison.OrdinalIgnoreCase))
    {
      _logger.LogWarning("Unsupported cloud request command: {Command}", request.Command);
      return;
    }

    try
    {
      var states = await _haClient.GetStatesAsync(CancellationToken.None);

      var response = new CloudRequestResponse
      {
        TargetConnectionId = request.TargetConnectionId,
        Data = states
      };

      var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response);

      var responseTopic = Topics.CloudResponses(_options.BoxId);
      await _mqtt.PublishAsync(responseTopic, responseBytes,
          MqttQualityOfServiceLevel.AtLeastOnce, CancellationToken.None);

      var responseStr = System.Text.Encoding.UTF8.GetString(responseBytes);
      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound, responseTopic,
          responseStr, "get_states"));

      _logger.LogInformation("get_states response published for ConnectionId={ConnectionId}",
          request.TargetConnectionId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to handle get_states request for ConnectionId={ConnectionId}",
          request.TargetConnectionId);
    }
  }
}
