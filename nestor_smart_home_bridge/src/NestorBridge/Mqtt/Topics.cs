namespace NestorBridge.Mqtt;

/// <summary>
/// Helper methods for constructing MQTT topic strings following Nestor convention.
/// </summary>
public static class Topics
{
  public static string Commands(string boxId) =>
      $"devices/{boxId}/commands/#";

  public static string CommandAck(string boxId, string commandId) =>
      $"devices/{boxId}/commands/{commandId}/ack";

  public static string TelemetryState(string boxId, string entityId) =>
      $"devices/{boxId}/telemetry/state/{entityId}";

  public static string TelemetryEvent(string boxId, string eventType) =>
      $"devices/{boxId}/telemetry/event/{eventType}";

  public static string Heartbeat(string boxId) =>
      $"devices/{boxId}/heartbeat";

  /// <summary>Topic for receiving cloud requests (get_states, etc.).
  /// Covered by the existing Commands wildcard — no extra subscription needed.
  /// </summary>
  public static string CloudRequests(string boxId) =>
      $"devices/{boxId}/commands/requests";

  /// <summary>Topic for publishing cloud request responses.</summary>
  public static string CloudResponses(string boxId) =>
      $"devices/{boxId}/responses";

  /// <summary>Topic for publishing raw state_changed events in real time.</summary>
  public static string EventsStateChanged(string boxId) =>
      $"devices/{boxId}/events/state_changed";

  /// <summary>Topic for publishing pairing/bridge events relayed from local Mosquitto.</summary>
  public static string EventsPairingStatus(string boxId) =>
      $"devices/{boxId}/events/pairing_status";

  /// <summary>
  /// Extract the HA MQTT sub-topic from a full downlink topic.
  /// e.g. "devices/mybox/commands/zigbee2mqtt/prise/set" → "zigbee2mqtt/prise/set"
  /// Returns null if the topic does not match the expected prefix.
  /// Returns null for reserved segments ("requests") that must not be treated as passthrough.
  /// </summary>
  public static string? ExtractSubTopic(string boxId, string fullTopic)
  {
    var prefix = $"devices/{boxId}/commands/";
    if (!fullTopic.StartsWith(prefix, StringComparison.Ordinal) || fullTopic.Length <= prefix.Length)
      return null;

    var sub = fullTopic[prefix.Length..];

    // Reserved segments handled internally — not forwarded as passthrough
    if (string.Equals(sub, "requests", StringComparison.OrdinalIgnoreCase))
      return null;

    return sub;
  }
}
