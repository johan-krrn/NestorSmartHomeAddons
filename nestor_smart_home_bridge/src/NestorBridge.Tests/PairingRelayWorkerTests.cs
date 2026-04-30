using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NSubstitute;
using NestorBridge.Configuration;
using NestorBridge.Mqtt;
using NestorBridge.Services;
using NestorBridge.Web;

namespace NestorBridge.Tests;

public class PairingRelayWorkerTests
{
  private readonly FakeLocalMqttBridge _localMqtt = new();
  private readonly IMqttBridge _cloudMqtt = Substitute.For<IMqttBridge>();
  private readonly MessageLog _messageLog = new();

  private PairingRelayWorker CreateWorker(bool enabled = true, string boxId = "box-test")
  {
    var opts = Options.Create(new BridgeOptions
    {
      BoxId = boxId,
      MqttClientId = "client",
      LocalMqtt = new LocalMqttOptions
      {
        Enabled = enabled,
        Topics = new List<string> { "zigbee2mqtt/bridge/event", "zigbee2mqtt/bridge/state" }
      }
    });
    return new PairingRelayWorker(
        _localMqtt, _cloudMqtt, opts, _messageLog,
        NullLogger<PairingRelayWorker>.Instance);
  }

  // ── Subscription lifecycle ────────────────────────────────────────

  [Fact]
  public async Task WhenEnabled_SubscribesToLocalMessages()
  {
    var worker = CreateWorker(enabled: true);
    await worker.StartAsync(CancellationToken.None);

    Assert.True(_localMqtt.HasSubscribers);
  }

  [Fact]
  public async Task WhenDisabled_DoesNotSubscribeOrPublish()
  {
    var worker = CreateWorker(enabled: false);
    await worker.StartAsync(CancellationToken.None);

    Assert.False(_localMqtt.HasSubscribers);
    await _cloudMqtt.DidNotReceive().PublishAsync(
        Arg.Any<string>(), Arg.Any<byte[]>(),
        Arg.Any<MqttQualityOfServiceLevel>(), Arg.Any<CancellationToken>());
  }

  [Fact]
  public async Task StopAsync_UnsubscribesFromLocalMessages()
  {
    var worker = CreateWorker();
    await worker.StartAsync(CancellationToken.None);
    Assert.True(_localMqtt.HasSubscribers);

    await worker.StopAsync(CancellationToken.None);
    Assert.False(_localMqtt.HasSubscribers);
  }

  // ── Cloud topic routing ───────────────────────────────────────────

  [Fact]
  public async Task LocalMessage_PublishedToCorrectCloudTopic()
  {
    var worker = CreateWorker(boxId: "my-box");
    await worker.StartAsync(CancellationToken.None);

    await _localMqtt.RaiseAsync(
        "zigbee2mqtt/bridge/event",
        """{"type":"device_joined"}"""u8.ToArray());

    await _cloudMqtt.Received(1).PublishAsync(
        "devices/my-box/events/pairing_status",
        Arg.Any<byte[]>(),
        MqttQualityOfServiceLevel.AtMostOnce,
        Arg.Any<CancellationToken>());
  }

  // ── Envelope structure ────────────────────────────────────────────

  [Fact]
  public async Task Envelope_ContainsBoxIdSourceTopicTimestampAndData()
  {
    var worker = CreateWorker(boxId: "my-box");
    await worker.StartAsync(CancellationToken.None);

    byte[]? captured = null;
    _cloudMqtt
        .When(x => x.PublishAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<MqttQualityOfServiceLevel>(), Arg.Any<CancellationToken>()))
        .Do(x => captured = x.ArgAt<byte[]>(1));

    await _localMqtt.RaiseAsync(
        "zigbee2mqtt/bridge/event",
        """{"type":"device_joined","data":{"friendly_name":"0x12345"}}"""u8.ToArray());

    Assert.NotNull(captured);
    using var doc = JsonDocument.Parse(captured);
    var root = doc.RootElement;

    Assert.Equal("my-box", root.GetProperty("boxId").GetString());
    Assert.Equal("zigbee2mqtt/bridge/event", root.GetProperty("sourceTopic").GetString());
    Assert.True(root.TryGetProperty("timestamp", out var ts) && ts.GetString()!.Length > 0);
    Assert.Equal(JsonValueKind.Object, root.GetProperty("data").ValueKind);
    Assert.Equal("device_joined", root.GetProperty("data").GetProperty("type").GetString());
  }

  [Fact]
  public async Task Envelope_JsonPayload_DataPreservedAsObject()
  {
    var worker = CreateWorker();
    await worker.StartAsync(CancellationToken.None);

    byte[]? captured = null;
    _cloudMqtt
        .When(x => x.PublishAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<MqttQualityOfServiceLevel>(), Arg.Any<CancellationToken>()))
        .Do(x => captured = x.ArgAt<byte[]>(1));

    await _localMqtt.RaiseAsync(
        "zigbee2mqtt/bridge/state",
        """{"state":"online","permit_join":false}"""u8.ToArray());

    Assert.NotNull(captured);
    using var doc = JsonDocument.Parse(captured);
    var data = doc.RootElement.GetProperty("data");
    Assert.Equal(JsonValueKind.Object, data.ValueKind);
    Assert.Equal("online", data.GetProperty("state").GetString());
  }

  [Fact]
  public async Task Envelope_NonJsonPayload_WrappedWithRawKey()
  {
    var worker = CreateWorker();
    await worker.StartAsync(CancellationToken.None);

    byte[]? captured = null;
    _cloudMqtt
        .When(x => x.PublishAsync(
            Arg.Any<string>(), Arg.Any<byte[]>(),
            Arg.Any<MqttQualityOfServiceLevel>(), Arg.Any<CancellationToken>()))
        .Do(x => captured = x.ArgAt<byte[]>(1));

    await _localMqtt.RaiseAsync(
        "zigbee2mqtt/bridge/logging",
        Encoding.UTF8.GetBytes("Zigbee2MQTT started"));

    Assert.NotNull(captured);
    using var doc = JsonDocument.Parse(captured);
    var data = doc.RootElement.GetProperty("data");
    Assert.Equal(JsonValueKind.Object, data.ValueKind);
    Assert.Equal("Zigbee2MQTT started", data.GetProperty("raw").GetString());
  }

  // ── MessageLog ────────────────────────────────────────────────────

  [Fact]
  public async Task LocalMessage_AddedToMessageLog()
  {
    var worker = CreateWorker(boxId: "box-log");
    await worker.StartAsync(CancellationToken.None);

    _cloudMqtt.PublishAsync(
        Arg.Any<string>(), Arg.Any<byte[]>(),
        Arg.Any<MqttQualityOfServiceLevel>(), Arg.Any<CancellationToken>())
        .Returns(Task.CompletedTask);

    await _localMqtt.RaiseAsync(
        "zigbee2mqtt/bridge/state",
        """{"state":"online"}"""u8.ToArray());

    var recent = _messageLog.GetRecent(10);
    Assert.Contains(recent, e => e.Topic == "devices/box-log/events/pairing_status");
  }

  // ── Multiple messages ─────────────────────────────────────────────

  [Fact]
  public async Task MultipleLocalMessages_EachPublishedSeparately()
  {
    var worker = CreateWorker();
    await worker.StartAsync(CancellationToken.None);

    await _localMqtt.RaiseAsync("zigbee2mqtt/bridge/event", """{"seq":1}"""u8.ToArray());
    await _localMqtt.RaiseAsync("zigbee2mqtt/bridge/event", """{"seq":2}"""u8.ToArray());
    await _localMqtt.RaiseAsync("zigbee2mqtt/bridge/state", """{"state":"online"}"""u8.ToArray());

    await _cloudMqtt.Received(3).PublishAsync(
        Arg.Any<string>(), Arg.Any<byte[]>(),
        Arg.Any<MqttQualityOfServiceLevel>(), Arg.Any<CancellationToken>());
  }
}

// ── In-process test double for ILocalMqttBridge ───────────────────────────────
/// <summary>
/// Simple test double — allows tests to raise MessageReceived without a real broker.
/// </summary>
internal sealed class FakeLocalMqttBridge : ILocalMqttBridge
{
  private Func<string, byte[], Task>? _handler;

  public event Func<string, byte[], Task>? MessageReceived
  {
    add => _handler += value;
    remove => _handler -= value;
  }

  public bool HasSubscribers => _handler is not null;

  public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

  public Task RaiseAsync(string topic, byte[] payload) =>
      _handler?.Invoke(topic, payload) ?? Task.CompletedTask;
}
