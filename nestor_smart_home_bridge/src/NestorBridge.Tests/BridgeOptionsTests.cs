using NestorBridge.Configuration;

namespace NestorBridge.Tests;

public class BridgeOptionsTests
{
  [Fact]
  public void Validate_MissingBoxId_Throws()
  {
    var opts = new BridgeOptions
    {
      BoxId = "",
      MqttClientId = "test"
    };

    Assert.Throws<InvalidOperationException>(() => opts.Validate());
  }

  [Fact]
  public void Validate_MissingClientId_Throws()
  {
    var opts = new BridgeOptions
    {
      BoxId = "box-1",
      MqttClientId = ""
    };

    Assert.Throws<InvalidOperationException>(() => opts.Validate());
  }

  [Fact]
  public void Validate_AllSet_DoesNotThrow()
  {
    var opts = new BridgeOptions
    {
      BoxId = "box-1",
      MqttClientId = "box-1"
    };

    opts.Validate(); // Should not throw
  }
}
