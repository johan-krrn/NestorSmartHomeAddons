using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NestorBridge.Configuration;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.Translation;

/// <summary>
/// Translates HA state_changed events into MQTT telemetry payloads.
/// Applies domain filtering based on configuration.
/// </summary>
public sealed class TelemetryTranslator
{
  private readonly BridgeOptions _options;
  private readonly ILogger<TelemetryTranslator> _logger;
  private readonly HashSet<string> _allowedDomains;

  public TelemetryTranslator(IOptions<BridgeOptions> options, ILogger<TelemetryTranslator> logger)
  {
    _options = options.Value;
    _logger = logger;
    _allowedDomains = new HashSet<string>(
        _options.TelemetryFilter.Domains,
        StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Convert a state_changed event into a telemetry payload.
  /// Returns null if the entity should be filtered out.
  /// </summary>
  public (string EntityId, byte[] Payload)? Translate(HaEvent haEvent)
  {
    var newState = haEvent.Data?.NewState;
    if (newState is null)
      return null;

    var entityId = newState.EntityId;
    var dotIdx = entityId.IndexOf('.');
    if (dotIdx < 0)
      return null;

    var domain = entityId[..dotIdx];

    // Filter by allowed domains
    if (_allowedDomains.Count > 0 && !_allowedDomains.Contains(domain))
    {
      _logger.LogTrace("Filtering out entity {EntityId} (domain {Domain})", entityId, domain);
      return null;
    }

    var telemetry = new TelemetryPayload
    {
      EntityId = entityId,
      State = newState.State,
      Attributes = newState.Attributes,
      LastChanged = newState.LastChanged,
      BoxId = _options.BoxId
    };

    var payload = JsonSerializer.SerializeToUtf8Bytes(telemetry);
    return (entityId, payload);
  }
}
