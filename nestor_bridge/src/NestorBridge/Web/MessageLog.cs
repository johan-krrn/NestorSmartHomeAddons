using System.Collections.Concurrent;

namespace NestorBridge.Web;

public enum MessageDirection
{
  Inbound,  // Cloud → HA (downlink commands)
  Outbound  // HA → Cloud (telemetry, acks, heartbeat)
}

public sealed record MessageLogEntry(
    DateTime Timestamp,
    MessageDirection Direction,
    string Topic,
    string Payload,
    string? Status = null);

/// <summary>
/// Thread-safe bounded ring buffer that stores recent messages
/// and notifies SSE subscribers in real time.
/// </summary>
public sealed class MessageLog
{
  private readonly ConcurrentQueue<MessageLogEntry> _entries = new();
  private readonly int _maxEntries;

  // SSE subscribers
  private readonly ConcurrentDictionary<Guid, Channel> _channels = new();

  public MessageLog(int maxEntries = 500)
  {
    _maxEntries = maxEntries;
  }

  public void Add(MessageLogEntry entry)
  {
    _entries.Enqueue(entry);

    // Trim oldest entries
    while (_entries.Count > _maxEntries)
      _entries.TryDequeue(out _);

    // Notify all SSE subscribers
    foreach (var ch in _channels.Values)
      ch.Writer.TryWrite(entry);
  }

  public IReadOnlyList<MessageLogEntry> GetRecent(int count = 100)
  {
    return _entries.Reverse().Take(count).Reverse().ToList();
  }

  public MessageLogSubscription Subscribe()
  {
    var id = Guid.NewGuid();
    var channel = new Channel();
    _channels[id] = channel;
    return new MessageLogSubscription(id, channel.Reader, () => _channels.TryRemove(id, out _));
  }

  // Simple wrapper around System.Threading.Channels
  private sealed class Channel
  {
    private readonly System.Threading.Channels.Channel<MessageLogEntry> _ch =
        System.Threading.Channels.Channel.CreateBounded<MessageLogEntry>(
            new System.Threading.Channels.BoundedChannelOptions(200)
            {
              FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest
            });

    public System.Threading.Channels.ChannelWriter<MessageLogEntry> Writer => _ch.Writer;
    public System.Threading.Channels.ChannelReader<MessageLogEntry> Reader => _ch.Reader;
  }
}

public sealed class MessageLogSubscription : IDisposable
{
  public Guid Id { get; }
  public System.Threading.Channels.ChannelReader<MessageLogEntry> Reader { get; }
  private readonly Action _unsubscribe;

  public MessageLogSubscription(Guid id,
      System.Threading.Channels.ChannelReader<MessageLogEntry> reader,
      Action unsubscribe)
  {
    Id = id;
    Reader = reader;
    _unsubscribe = unsubscribe;
  }

  public void Dispose() => _unsubscribe();
}
