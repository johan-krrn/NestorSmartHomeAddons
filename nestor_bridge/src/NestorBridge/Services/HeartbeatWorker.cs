using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using NestorBridge.Configuration;
using NestorBridge.Mqtt;
using NestorBridge.Web;

namespace NestorBridge.Services;

public sealed class HeartbeatWorker : BackgroundService
{
  private readonly IMqttBridge _mqtt;
  private readonly BridgeOptions _options;
  private readonly MessageLog _messageLog;
  private readonly ILogger<HeartbeatWorker> _logger;
  private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

  public HeartbeatWorker(
      IMqttBridge mqtt,
      IOptions<BridgeOptions> options,
      MessageLog messageLog,
      ILogger<HeartbeatWorker> logger)
  {
    _mqtt = mqtt;
    _options = options.Value;
    _messageLog = messageLog;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("HeartbeatWorker started (interval={Interval}s)", Interval.TotalSeconds);

    using var timer = new PeriodicTimer(Interval);

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      try
      {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
          boxId = _options.BoxId,
          timestamp = DateTime.UtcNow.ToString("o"),
          status = "alive"
        });

        var topic = Topics.Heartbeat(_options.BoxId);
        await _mqtt.PublishAsync(topic, payload,
            MqttQualityOfServiceLevel.AtMostOnce, stoppingToken);

        _messageLog.Add(new MessageLogEntry(
            DateTime.UtcNow, MessageDirection.Outbound, topic,
            System.Text.Encoding.UTF8.GetString(payload)));

        _logger.LogDebug("Heartbeat published");
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Failed to publish heartbeat");
      }
    }

    _logger.LogInformation("HeartbeatWorker stopped");
  }
}
