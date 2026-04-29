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
  private readonly ExposedEntitiesStore _exposedEntities;
  private readonly ILogger<DownlinkWorker> _logger;

  public DownlinkWorker(
      IMqttBridge mqtt,
      IHaWebSocketClient haClient,
      HaServiceCaller serviceCaller,
      CommandTranslator translator,
      IOptions<BridgeOptions> options,
      MessageLog messageLog,
      ExposedEntitiesStore exposedEntities,
      ILogger<DownlinkWorker> logger)
  {
    _mqtt = mqtt;
    _haClient = haClient;
    _serviceCaller = serviceCaller;
    _translator = translator;
    _options = options.Value;
    _messageLog = messageLog;
    _exposedEntities = exposedEntities;
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

    try
    {
      JsonElement responseData;

      switch (request.Command.ToLowerInvariant())
      {
        case "get_states":
          responseData = await _haClient.GetStatesAsync(CancellationToken.None);
          break;

        case "call_service":
          responseData = await ExecuteCallServiceAsync(request);
          break;

        case "get_exposed_entities":
          var entities = _exposedEntities.GetAll();
          using (var doc = JsonDocument.Parse(JsonSerializer.Serialize(entities)))
          {
            responseData = doc.RootElement.Clone();
          }
          break;

        default:
          // Generic HA WebSocket passthrough — forwards any HA command type
          // (e.g. config/area_registry/list, config/floor_registry/list, ...)
          responseData = await ExecuteGenericCommandAsync(request);
          break;
      }

      var response = new CloudRequestResponse
      {
        TargetConnectionId = request.TargetConnectionId,
        Command = request.Command,
        Data = responseData
      };

      var responseBytes = JsonSerializer.SerializeToUtf8Bytes(response);

      var responseTopic = Topics.CloudResponses(_options.BoxId);
      await _mqtt.PublishAsync(responseTopic, responseBytes,
          MqttQualityOfServiceLevel.AtLeastOnce, CancellationToken.None);

      var responseStr = System.Text.Encoding.UTF8.GetString(responseBytes);
      _messageLog.Add(new MessageLogEntry(
          DateTime.UtcNow, MessageDirection.Outbound, responseTopic,
          responseStr, request.Command));

      _logger.LogInformation("{Command} response published for ConnectionId={ConnectionId}",
          request.Command, request.TargetConnectionId);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Failed to handle {Command} request for ConnectionId={ConnectionId}",
          request.Command, request.TargetConnectionId);
    }
  }

  /// <summary>
  /// Resolves a Payload <see cref="JsonElement"/> that may have been double-serialized as a
  /// JSON string by the backend. Returns a cloned <see cref="JsonElement"/> of the object,
  /// or null if the payload is absent/not an object.
  /// </summary>
  private static JsonElement? ResolvePayload(JsonElement? raw)
  {
    if (!raw.HasValue) return null;

    if (raw.Value.ValueKind == JsonValueKind.String)
    {
      var inner = raw.Value.GetString();
      if (string.IsNullOrEmpty(inner)) return null;
      using var doc = JsonDocument.Parse(inner);
      return doc.RootElement.Clone();
    }

    return raw.Value.ValueKind == JsonValueKind.Object ? raw.Value : null;
  }

  private async Task<JsonElement> ExecuteGenericCommandAsync(CloudRequest request)
  {
    var extraProps = ResolvePayload(request.Payload);
    _logger.LogInformation("Generic HA WebSocket command: {Type}", request.Command);
    return await _haClient.SendCommandAsync(request.Command, extraProps, CancellationToken.None);
  }

  private async Task<JsonElement> ExecuteCallServiceAsync(CloudRequest request)
  {
    var payload = ResolvePayload(request.Payload)
        ?? throw new InvalidOperationException("call_service request missing or invalid Payload");

    var domain = payload.GetProperty("domain").GetString()
        ?? throw new InvalidOperationException("call_service Payload missing domain");
    var service = payload.GetProperty("service").GetString()
        ?? throw new InvalidOperationException("call_service Payload missing service");

    string? entityId = null;
    Dictionary<string, object>? serviceData = null;

    if (payload.TryGetProperty("service_data", out var sdEl))
    {
      serviceData = JsonSerializer.Deserialize<Dictionary<string, object>>(sdEl);
    }

    if (payload.TryGetProperty("entity_id", out var eidEl))
      entityId = eidEl.GetString();

    _logger.LogInformation("Calling HA service {Domain}.{Service} (entity={EntityId})",
        domain, service, entityId ?? "none");

    var result = await _haClient.CallServiceAsync(domain, service, entityId, serviceData, CancellationToken.None);

    if (result.Success == true)
      return result.Result ?? default;

    var errorMsg = result.Error?.Message ?? "Service call failed";
    _logger.LogError("Service call {Domain}.{Service} failed: {Error}", domain, service, errorMsg);

    using var errDoc = JsonDocument.Parse(
        JsonSerializer.Serialize(new { success = false, error = errorMsg }));
    return errDoc.RootElement.Clone();
  }
}
