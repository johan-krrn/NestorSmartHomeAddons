using Microsoft.Extensions.Logging;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.HomeAssistant;

/// <summary>
/// High-level API for calling HA services, wrapping the raw WebSocket call_service.
/// </summary>
public sealed class HaServiceCaller
{
  private readonly IHaWebSocketClient _client;
  private readonly ILogger<HaServiceCaller> _logger;

  public HaServiceCaller(IHaWebSocketClient client, ILogger<HaServiceCaller> logger)
  {
    _client = client;
    _logger = logger;
  }

  /// <summary>
  /// Execute a generic cloud command against HA.
  /// Splits entity_id to extract domain, maps action to service name.
  /// Returns the HA context id from the result for ack purposes.
  /// </summary>
  public async Task<(bool Success, string? ContextId, string? Error)> ExecuteCommandAsync(
      CloudCommand command, CancellationToken cancellationToken)
  {
    var entityId = command.TargetEntityId;
    var dotIdx = entityId.IndexOf('.');
    if (dotIdx < 0)
      return (false, null, $"Invalid entity_id format: {entityId}");

    var domain = entityId[..dotIdx];
    var service = command.Action;

    _logger.LogInformation("Calling HA service {Domain}.{Service} on {Entity}",
        domain, service, entityId);

    try
    {
      var result = await _client.CallServiceAsync(
          domain, service, entityId, command.Parameters, cancellationToken);

      if (result.Success == true)
      {
        // Extract context id from: {"result": {"context": {"id": "01HW..."}}} 
        string? contextId = null;
        if (result.Result.HasValue && result.Result.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
          if (result.Result.Value.TryGetProperty("context", out var ctx) &&
              ctx.TryGetProperty("id", out var idProp))
          {
            contextId = idProp.GetString();
          }
        }

        _logger.LogInformation("Service call succeeded for {Entity} (context={ContextId})",
            entityId, contextId);
        return (true, contextId, null);
      }

      var error = result.Error?.Message ?? "Unknown HA error";
      _logger.LogError("Service call failed for {Entity}: {Error}", entityId, error);
      return (false, null, error);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Exception calling service {Domain}.{Service} on {Entity}",
          domain, service, entityId);
      return (false, null, ex.Message);
    }
  }

  /// <summary>
  /// Publish a raw payload to a local HA MQTT topic via the HA WebSocket mqtt.publish service.
  /// Used for MQTT passthrough commands (topic-encoded routing).
  /// </summary>
  public async Task<(bool Success, string? Error)> PublishMqttAsync(
      string mqttTopic, string payload, CancellationToken cancellationToken)
  {
    _logger.LogInformation("Publishing to HA MQTT topic {Topic}", mqttTopic);
    try
    {
      var result = await _client.CallServiceAsync(
          "mqtt", "publish", null,
          new Dictionary<string, object> { ["topic"] = mqttTopic, ["payload"] = payload },
          cancellationToken);

      if (result.Success == true)
        return (true, null);

      var error = result.Error?.Message ?? "Unknown HA error";
      _logger.LogError("mqtt.publish failed for {Topic}: {Error}", mqttTopic, error);
      return (false, error);
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Exception calling mqtt.publish for {Topic}", mqttTopic);
      return (false, ex.Message);
    }
  }
}
