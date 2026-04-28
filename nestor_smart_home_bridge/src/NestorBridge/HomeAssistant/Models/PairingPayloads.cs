using System.Text.Json;
using System.Text.Json.Serialization;

namespace NestorBridge.HomeAssistant.Models;

/// <summary>
/// Envelope published to the cloud on devices/{boxId}/events/pairing_status,
/// wrapping a raw event received from the local Mosquitto broker.
/// </summary>
public sealed class PairingStatusPayload
{
  [JsonPropertyName("boxId")]
  public string BoxId { get; set; } = string.Empty;

  /// <summary>The original local topic the message arrived on (e.g. zigbee2mqtt/bridge/event).</summary>
  [JsonPropertyName("sourceTopic")]
  public string SourceTopic { get; set; } = string.Empty;

  [JsonPropertyName("timestamp")]
  public string Timestamp { get; set; } = string.Empty;

  /// <summary>
  /// Original payload preserved as-is if it was valid JSON,
  /// or wrapped as {"raw":"..."} for non-JSON payloads (e.g. plain-text log lines).
  /// </summary>
  [JsonPropertyName("data")]
  public JsonElement Data { get; set; }
}
