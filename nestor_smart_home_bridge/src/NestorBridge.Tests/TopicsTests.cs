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
}
