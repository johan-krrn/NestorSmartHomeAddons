using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using NestorBridge.Configuration;

namespace NestorBridge.Mqtt;

public sealed class MqttBridge : IMqttBridge, IAsyncDisposable
{
  private readonly IMqttClient _client;
  private readonly BridgeOptions _options;
  private readonly ILogger<MqttBridge> _logger;
  private int _reconnectDelayMs = 1000;
  private const int MaxReconnectDelayMs = 60_000;

  private const string MqttHost = "eg-nestorsmarthome-poc.westeurope-1.ts.eventgrid.azure.net";
  private const int MqttPort = 8883;
  private const string CertPath = "/ssl/certs/device.crt";
  private const string KeyPath = "/ssl/certs/device.key";

  public event Func<string, byte[], Task>? MessageReceived;

  public MqttBridge(IOptions<BridgeOptions> options, ILogger<MqttBridge> logger)
  {
    _options = options.Value;
    _logger = logger;
    var factory = new MqttFactory();
    _client = factory.CreateMqttClient();

    _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    _client.DisconnectedAsync += OnDisconnectedAsync;
  }

  public async Task ConnectAsync(CancellationToken cancellationToken)
  {
    var optionsBuilder = new MqttClientOptionsBuilder()
        .WithProtocolVersion(MqttProtocolVersion.V500)
        .WithClientId(_options.MqttClientId)
        .WithTcpServer(MqttHost, MqttPort)
        .WithCleanSession(true)
        .WithKeepAlivePeriod(TimeSpan.FromSeconds(30));

    ConfigureX509(optionsBuilder);

    var mqttOptions = optionsBuilder.Build();

    _logger.LogInformation("Connecting to MQTT broker {Host}:{Port}", MqttHost, MqttPort);

    await _client.ConnectAsync(mqttOptions, cancellationToken);

    // Subscribe to commands topic (also covers devices/{boxId}/commands/requests via wildcard)
    var commandTopic = Topics.Commands(_options.BoxId);
    var cmdSubResult = await _client.SubscribeAsync(new MqttTopicFilterBuilder()
        .WithTopic(commandTopic)
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build(), cancellationToken);

    LogSubscribeResult(commandTopic, cmdSubResult);
    _reconnectDelayMs = 1000; // Reset on successful connect
  }

  public async Task PublishAsync(string topic, byte[] payload, MqttQualityOfServiceLevel qos,
      CancellationToken cancellationToken)
  {
    var message = new MqttApplicationMessageBuilder()
        .WithTopic(topic)
        .WithPayload(payload)
        .WithQualityOfServiceLevel(qos)
        .Build();

    await _client.PublishAsync(message, cancellationToken);
  }

  public async Task DisconnectAsync(CancellationToken cancellationToken)
  {
    if (_client.IsConnected)
    {
      await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
          .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
          .Build(), cancellationToken);
    }
  }

  private void ConfigureX509(MqttClientOptionsBuilder builder)
  {
    if (!File.Exists(CertPath))
      throw new FileNotFoundException($"Client certificate not found: {CertPath}");
    if (!File.Exists(KeyPath))
      throw new FileNotFoundException($"Client key not found: {KeyPath}");

    var cert = X509Certificate2.CreateFromPemFile(CertPath, KeyPath);
    // Re-export to PKCS12 (required on some platforms for TLS client auth)
    cert = new X509Certificate2(cert.Export(X509ContentType.Pkcs12));

    // Event Grid requires the client authentication name in the Username field
    builder.WithCredentials(_options.MqttClientId, "");

    var tlsOptions = new MqttClientTlsOptionsBuilder()
    .UseTls()
    .WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13)
    .WithClientCertificates(new List<X509Certificate2> { cert })
    .Build();

    builder.WithTlsOptions(tlsOptions);
  }

  private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
  {
    var topic = args.ApplicationMessage.Topic;
    var payload = args.ApplicationMessage.PayloadSegment.ToArray();

    // Log at Information for cloud request/response topics so they are always visible
    if (string.Equals(topic, Topics.CloudRequests(_options.BoxId), StringComparison.Ordinal) ||
        string.Equals(topic, Topics.CloudResponses(_options.BoxId), StringComparison.Ordinal))
      _logger.LogInformation("MQTT message received on {Topic} ({Bytes} bytes)", topic, payload.Length);
    else
      _logger.LogDebug("MQTT message received on {Topic} ({Bytes} bytes)", topic, payload.Length);

    if (MessageReceived is not null)
    {
      await MessageReceived.Invoke(topic, payload);
    }
  }

  private void LogSubscribeResult(string topic, MqttClientSubscribeResult result)
  {
    foreach (var item in result.Items)
    {
      var success = item.ResultCode is
          MqttClientSubscribeResultCode.GrantedQoS0 or
          MqttClientSubscribeResultCode.GrantedQoS1 or
          MqttClientSubscribeResultCode.GrantedQoS2;

      if (success)
        _logger.LogInformation("Subscribed to {Topic} (QoS={ResultCode})", topic, item.ResultCode);
      else
        _logger.LogError(
            "Subscription to {Topic} REFUSED by broker (ResultCode={ResultCode}). " +
            "Check Azure Event Grid topic space permissions for this client.",
            topic, item.ResultCode);
    }
  }

  private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
  {
    _logger.LogWarning("MQTT disconnected (reason={Reason}). Reconnecting in {Delay}ms...",
        args.Reason, _reconnectDelayMs);

    await Task.Delay(_reconnectDelayMs);
    _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, MaxReconnectDelayMs);

    try
    {
      await ConnectAsync(CancellationToken.None);
      _logger.LogInformation("MQTT reconnected successfully");
    }
    catch (Exception ex)
    {
      _logger.LogError(ex, "MQTT reconnection failed, will retry");
    }
  }

  public async ValueTask DisposeAsync()
  {
    await DisconnectAsync(CancellationToken.None);
    _client.Dispose();
  }
}
