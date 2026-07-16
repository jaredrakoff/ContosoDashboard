using System.Threading.Channels;

namespace ContosoDashboard.Services.Scanning;

/// <summary>
/// Offline default queue backed by an in-memory <see cref="Channel{T}"/>. Registered
/// as a singleton and exposed as both the producer (<see cref="IFileScanQueue"/>)
/// and consumer (<see cref="IFileScanQueueReader"/>) sides.
/// </summary>
public class InMemoryFileScanQueue : IFileScanQueue, IFileScanQueueReader
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public async Task EnqueueAsync(int documentId, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(documentId, cancellationToken);
    }

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
