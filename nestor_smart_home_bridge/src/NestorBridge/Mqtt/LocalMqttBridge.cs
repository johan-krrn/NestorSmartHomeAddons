using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using NestorBridge.Configuration;

namespace NestorBridge.Mqtt;

/// <summary>
/// Subscribe-only MQTT client that connects to the local Home Assistant Mosquitto broker
/// (plain TCP, no TLS). Subscribes to zigbee2mqtt pairing topics and raises
/// <see cref="MessageReceived"/> for each incoming message.
/// Reconnects automatically with exponential backoff on disconnection.
/// </summary>
public sealed class LocalMqttBridge : ILocalMqttBridge, IAsyncDisposable
{
  private readonly IMqttClient _client;
  private readonly LocalMqttOptions _options;
  private readonly string _boxId;
  private readonly ILogger<LocalMqttBridge> _logger;
  private int _reconnectDelayMs = 1000;
  private const int MaxReconnectDelayMs = 60_000;

  public event Func<string, byte[], Task>? MessageReceived;

  public LocalMqttBridge(IOptions<BridgeOptions> options, ILogger<LocalMqttBridge> logger)
  {
    _options = options.Value.LocalMqtt;
    _boxId = options.Value.BoxId;
    _logger = logger;

    var factory = new MqttFactory();
    _client = factory.CreateMqttClient();
    _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    _client.DisconnectedAsync += OnDisconnectedAsync;
  }

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    var optionsBuilder = new MqttClientOptionsBuilder()
        .WithProtocolVersion(MqttProtocolVersion.V311)
        .WithClientId($"nestor-local-{_boxId}")
        .WithTcpServer(_options.Host, _options.Port)
        .WithCleanSession(true)
        .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

    if (!string.IsNullOrEmpty(_options.Username))
      optionsBuilder.WithCredentials(_options.Username, _options.Password);

    _logger.LogInformation(
        "Connecting to local MQTT broker {Host}:{Port} ({TopicCount} topic(s))",
        _options.Host, _options.Port, _options.Topics.Count);

    await _client.ConnectAsync(optionsBuilder.Build(), cancellationToken);

    foreach (var topic in _options.Topics)
    {
      var result = await _client.SubscribeAsync(
          new MqttTopicFilterBuilder()
              .WithTopic(topic)
              .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
              .Build(),
          cancellationToken);

      LogSubscribeResult(topic, result);
    }

    _reconnectDelayMs = 1000;
    _logger.LogInformation("Local MQTT connected and subscribed");
  }

  public async Task DisconnectAsync(CancellationToken cancellationToken)
  {
    if (_client.IsConnected)
    {
      await _client.DisconnectAsync(
          new MqttClientDisconnectOptionsBuilder()
              .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
              .Build(),
          cancellationToken);
    }
  }

  private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
  {
    var topic = args.ApplicationMessage.Topic;
    var payload = args.ApplicationMessage.PayloadSegment.ToArray();

    _logger.LogDebug("Local MQTT: {Topic} ({Bytes} bytes)", topic, payload.Length);

    if (MessageReceived is not null)
      await MessageReceived.Invoke(topic, payload);
  }

  private void LogSubscribeResult(string topic, MqttClientSubscribeResult result)
  {
    foreach (var item in result.Items)
    {
      var granted = item.ResultCode is
          MqttClientSubscribeResultCode.GrantedQoS0 or
          MqttClientSubscribeResultCode.GrantedQoS1 or
          MqttClientSubscribeResultCode.GrantedQoS2;

      if (granted)
        _logger.LogInformation("Subscribed to local topic {Topic}", topic);
      else
        _logger.LogError(
            "Local subscription to {Topic} REFUSED (ResultCode={ResultCode})",
            topic, item.ResultCode);
    }
  }

  private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
  {
    _logger.LogWarning(
        "Local MQTT disconnected (reason={Reason}). Reconnecting in {Delay}ms...",
        args.Reason, _reconnectDelayMs);

    await Task.Delay(_reconnectDelayMs);
    _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, MaxReconnectDelayMs);

    try
    {
      await ConnectAsync(CancellationToken.None);
      _logger.LogInformation("Local MQTT reconnected");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "Local MQTT reconnection failed, will retry on next disconnect event");
    }
  }

  public async ValueTask DisposeAsync()
  {
    await DisconnectAsync(CancellationToken.None);
    _client.Dispose();
  }
}
