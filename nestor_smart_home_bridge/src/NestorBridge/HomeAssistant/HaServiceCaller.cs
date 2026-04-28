using Microsoft.Extensions.Logging;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.HomeAssistant;

/// <summary>
/// High-level API for calling HA services, wrapping the raw WebSocket call_service.
/// </summary>
public sealed class HaServiceCaller
{
  private readonly IHaWebSocketClient _client;
  private readonly IHaRestClient _restClient;
  private readonly ILogger<HaServiceCaller> _logger;

  // Actions on the "automation" domain that are routed to the REST API instead of WebSocket.
  private static readonly HashSet<string> RestAutomationActions =
      new(StringComparer.OrdinalIgnoreCase) { "create", "update", "delete" };

  public HaServiceCaller(
      IHaWebSocketClient client,
      IHaRestClient restClient,
      ILogger<HaServiceCaller> logger)
  {
    _client = client;
    _restClient = restClient;
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

    // Smart routing: automation CRUD → HA REST API; everything else → WebSocket
    if (string.Equals(domain, "automation", StringComparison.OrdinalIgnoreCase)
        && RestAutomationActions.Contains(service))
    {
      return await ExecuteAutomationRestAsync(
          command, entityId[(dotIdx + 1)..], service, cancellationToken);
    }

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
  /// Routes automation create/update/delete commands to the HA Config REST API.
  /// </summary>
  private async Task<(bool Success, string? ContextId, string? Error)> ExecuteAutomationRestAsync(
      CloudCommand command, string automationId, string action, CancellationToken cancellationToken)
  {
    _logger.LogInformation("REST routing: {Action} automation {Id}", action, automationId);

    if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
    {
      var (ok, err) = await _restClient.DeleteAutomationAsync(automationId, cancellationToken);
      return (ok, null, err);
    }

    // create or update
    if (command.Parameters is null || command.Parameters.Count == 0)
      return (false, null, "Automation config is required in 'parameters' for create/update");

    var (success, error) = await _restClient.CreateOrUpdateAutomationAsync(
        automationId, command.Parameters, cancellationToken);
    return (success, null, error);
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
