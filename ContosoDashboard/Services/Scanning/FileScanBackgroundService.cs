using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services.Scanning;

/// <summary>
/// Offline background worker that drains the in-process scan queue and updates each
/// document's <see cref="ScanStatus"/>. Infected files are deleted and their
/// uploader is notified. In production this role is performed by an Azure Function
/// triggered by Azure Queue Storage (see plan.md).
/// </summary>
public class FileScanBackgroundService : BackgroundService
{
    private readonly IFileScanQueueReader _queueReader;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileScanBackgroundService> _logger;

    public FileScanBackgroundService(
        IFileScanQueueReader queueReader,
        IServiceScopeFactory scopeFactory,
        ILogger<FileScanBackgroundService> logger)
    {
        _queueReader = queueReader;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int documentId;
            try
            {
                documentId = await _queueReader.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessAsync(documentId, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scanning document {DocumentId}", documentId);
            }
        }
    }

    private async Task ProcessAsync(int documentId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
        var scanner = scope.ServiceProvider.GetRequiredService<IFileScanner>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var document = await db.Documents.FindAsync(new object[] { documentId }, cancellationToken);
        if (document == null)
        {
            return;
        }

        ScanResult result;
        await using (var stream = await storage.DownloadAsync(document.FilePath))
        {
            if (stream == null)
            {
                // File missing — treat as infected/failed and remove metadata state.
                result = ScanResult.Infected;
            }
            else
            {
                result = await scanner.ScanAsync(stream, cancellationToken);
            }
        }

        if (result == ScanResult.Clean)
        {
            document.ScanStatus = ScanStatus.Clean;
        }
        else
        {
            document.ScanStatus = ScanStatus.Infected;
            await storage.DeleteAsync(document.FilePath);

            db.DocumentActivities.Add(new DocumentActivity
            {
                DocumentId = document.DocumentId,
                UserId = document.UploadedByUserId,
                Action = DocumentAction.Delete,
                Timestamp = DateTime.UtcNow
            });

            await notifications.CreateNotificationAsync(new Notification
            {
                UserId = document.UploadedByUserId,
                Title = "Document blocked by virus scan",
                Message = $"Your document '{document.Title}' was blocked and removed because it failed the virus scan.",
                Type = NotificationType.DocumentBlocked,
                Priority = NotificationPriority.Important
            });
        }

        document.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }
}
