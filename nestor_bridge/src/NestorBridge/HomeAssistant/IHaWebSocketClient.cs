using System.Text.Json;
using NestorBridge.HomeAssistant.Models;

namespace NestorBridge.HomeAssistant;

public interface IHaWebSocketClient
{
  /// <summary>Raised when a state_changed event is received.</summary>
  event Func<HaEvent, Task>? StateChanged;

  /// <summary>Connect, authenticate, and subscribe to events.</summary>
  Task ConnectAsync(CancellationToken cancellationToken);

  /// <summary>Call a service on HA (e.g. light.turn_on). entityId is optional (e.g. not needed for mqtt.publish).</summary>
  Task<HaMessage> CallServiceAsync(string domain, string service, string? entityId,
      Dictionary<string, object>? serviceData, CancellationToken cancellationToken);

  /// <summary>Retrieve all entity states from HA via get_states.</summary>
  Task<JsonElement> GetStatesAsync(CancellationToken cancellationToken);

  /// <summary>Disconnect cleanly.</summary>
  Task DisconnectAsync(CancellationToken cancellationToken);
}
