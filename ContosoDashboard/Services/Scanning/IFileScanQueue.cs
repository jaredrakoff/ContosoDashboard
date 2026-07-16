namespace ContosoDashboard.Services.Scanning;

/// <summary>
/// Producer side of the asynchronous scan pipeline. Business code depends only on
/// this abstraction. The offline default is an in-process queue; the production
/// swap is Azure Queue Storage (consumed by an Azure Function).
/// </summary>
public interface IFileScanQueue
{
    Task EnqueueAsync(int documentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumer side used by the offline background worker to drain the in-process
/// queue. Not implemented by the Azure Queue Storage swap (the Azure Function reads
/// the queue instead).
/// </summary>
public interface IFileScanQueueReader
{
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}
