using System.Text.Json;

namespace NestorBridge.HomeAssistant;

/// <summary>
/// Client for the Home Assistant Config REST API.
/// Used for automation CRUD operations that are not supported via the WebSocket API.
/// </summary>
public interface IHaRestClient
{
  /// <summary>
  /// Create or update an automation via POST /api/config/automation/config/{automationId}.
  /// </summary>
  Task<(bool Success, string? Error)> CreateOrUpdateAutomationAsync(
      string automationId,
      Dictionary<string, object> config,
      CancellationToken cancellationToken);

  /// <summary>
  /// Delete an automation via DELETE /api/config/automation/config/{automationId}.
  /// Returns success if the automation does not exist (idempotent).
  /// </summary>
  Task<(bool Success, string? Error)> DeleteAutomationAsync(
      string automationId,
      CancellationToken cancellationToken);

  /// <summary>
  /// Perform a generic GET request against a HA REST API path (relative to /api/).
  /// Used as a fallback for commands that are not available via the WebSocket API.
  /// </summary>
  Task<(bool Success, JsonElement? Data, string? Error)> GetRawAsync(
      string path,
      CancellationToken cancellationToken);
}
