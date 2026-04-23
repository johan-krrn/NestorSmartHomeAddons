using NestorBridge.Mqtt;

namespace NestorBridge.Tests;

public class TopicsTests
{
  private const string BoxId = "nestor-0a1b2c3d";

  [Fact]
  public void Commands_ReturnsWildcardTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/commands/#", Topics.Commands(BoxId));
  }

  [Fact]
  public void CommandAck_ReturnsCorrectTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/commands/cmd-123/ack",
        Topics.CommandAck(BoxId, "cmd-123"));
  }

  [Fact]
  public void TelemetryState_ReturnsCorrectTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/telemetry/state/light.salon",
        Topics.TelemetryState(BoxId, "light.salon"));
  }

  [Fact]
  public void TelemetryEvent_ReturnsCorrectTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/telemetry/event/button_press",
        Topics.TelemetryEvent(BoxId, "button_press"));
  }

  [Fact]
  public void Heartbeat_ReturnsCorrectTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/heartbeat", Topics.Heartbeat(BoxId));
  }

  [Fact]
  public void CloudRequests_ReturnsCorrectTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/commands/requests", Topics.CloudRequests(BoxId));
  }

  [Fact]
  public void CloudResponses_ReturnsCorrectTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/responses", Topics.CloudResponses(BoxId));
  }

  [Fact]
  public void EventsStateChanged_ReturnsCorrectTopic()
  {
    Assert.Equal("devices/nestor-0a1b2c3d/events/state_changed", Topics.EventsStateChanged(BoxId));
  }

  [Fact]
  public void ExtractSubTopic_ReservedRequests_ReturnsNull()
  {
    Assert.Null(Topics.ExtractSubTopic(BoxId, "devices/nestor-0a1b2c3d/commands/requests"));
  }

  [Fact]
  public void ExtractSubTopic_ValidSubTopic_ReturnsSubTopic()
  {
    Assert.Equal("zigbee2mqtt/prise/set",
        Topics.ExtractSubTopic(BoxId, "devices/nestor-0a1b2c3d/commands/zigbee2mqtt/prise/set"));
  }
}
