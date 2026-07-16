using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services.FileStorage;
using ContosoDashboard.Services.Scanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<Document?> UploadDocumentAsync(DocumentUploadRequest request, int requestingUserId);
    Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query);
    Task<List<Document>> SearchDocumentsAsync(string term, int requestingUserId);
    Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId);
    Task<Document?> GetDocumentAsync(int documentId, int requestingUserId);
    Task<DocumentContent?> GetDocumentContentAsync(int documentId, int requestingUserId);
    Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadata metadata, int requestingUserId);
    Task<bool> ReplaceFileAsync(int documentId, Stream content, string fileName, int requestingUserId);
    Task<bool> DeleteDocumentAsync(int documentId, int requestingUserId);
    Task<bool> ShareDocumentAsync(int documentId, int shareWithUserId, int requestingUserId);
    Task<List<Document>> GetSharedWithMeAsync(int requestingUserId);
    Task<bool> AttachToTaskAsync(int documentId, int taskId, int requestingUserId);
    Task<List<Document>> GetRecentDocumentsAsync(int requestingUserId, int count = 5);
    Task<int> GetDocumentCountAsync(int requestingUserId);
    Task<DocumentReport?> GetActivityReportAsync(int requestingUserId);
}

public class DocumentService : IDocumentService
{
    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IFileScanQueue _scanQueue;
    private readonly INotificationService _notificationService;
    private readonly DocumentOptions _options;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorage,
        IFileScanQueue scanQueue,
        INotificationService notificationService,
        IOptions<DocumentOptions> options)
    {
        _context = context;
        _fileStorage = fileStorage;
        _scanQueue = scanQueue;
        _notificationService = notificationService;
        _options = options.Value;
    }

    public async Task<Document?> UploadDocumentAsync(DocumentUploadRequest request, int requestingUserId)
    {
        // Server-side validation guard (the page also validates for friendly messages).
        if (!IsValidUpload(request.Title, request.Category, request.FileName, request.FileSizeBytes))
        {
            return null;
        }

        // If associating with a project, the user must belong to it.
        if (request.ProjectId.HasValue &&
            !await IsProjectMemberAsync(request.ProjectId.Value, requestingUserId))
        {
            return null;
        }

        // Store bytes BEFORE persisting metadata (prevents orphaned rows / duplicate paths).
        var storedPath = await _fileStorage.UploadAsync(
            request.Content, request.FileName, requestingUserId, request.ProjectId);

        var document = new Document
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            Category = request.Category,
            Tags = request.Tags,
            FileName = request.FileName,
            FilePath = storedPath,
            FileType = string.IsNullOrWhiteSpace(request.ContentType)
                ? "application/octet-stream"
                : request.ContentType,
            FileSizeBytes = request.FileSizeBytes,
            UploadedByUserId = requestingUserId,
            ProjectId = request.ProjectId,
            ScanStatus = ScanStatus.Pending,
            UploadedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _context.Documents.Add(document);
        await _context.SaveChangesAsync();

        await LogActivityAsync(document.DocumentId, requestingUserId, DocumentAction.Upload);

        // Queue asynchronous virus scan; the document stays hidden until Clean.
        await _scanQueue.EnqueueAsync(document.DocumentId);

        // Notify project members (excluding the uploader) of the new project document.
        await NotifyProjectMembersAsync(document, requestingUserId);

        return document;
    }

    public async Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query)
    {
        var documents = _context.Documents
            .Include(d => d.Project)
            .Where(d => d.UploadedByUserId == requestingUserId && d.ScanStatus == ScanStatus.Clean);

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            documents = documents.Where(d => d.Category == query.Category);
        }

        if (query.ProjectId.HasValue)
        {
            documents = documents.Where(d => d.ProjectId == query.ProjectId);
        }

        if (query.FromDate.HasValue)
        {
            documents = documents.Where(d => d.UploadedDate >= query.FromDate.Value);
        }

        if (query.ToDate.HasValue)
        {
            documents = documents.Where(d => d.UploadedDate <= query.ToDate.Value);
        }

        documents = (query.SortBy, query.Descending) switch
        {
            (DocumentSortField.Title, false) => documents.OrderBy(d => d.Title),
            (DocumentSortField.Title, true) => documents.OrderByDescending(d => d.Title),
            (DocumentSortField.Category, false) => documents.OrderBy(d => d.Category),
            (DocumentSortField.Category, true) => documents.OrderByDescending(d => d.Category),
            (DocumentSortField.FileSize, false) => documents.OrderBy(d => d.FileSizeBytes),
            (DocumentSortField.FileSize, true) => documents.OrderByDescending(d => d.FileSizeBytes),
            (DocumentSortField.UploadedDate, false) => documents.OrderBy(d => d.UploadedDate),
            _ => documents.OrderByDescending(d => d.UploadedDate)
        };

        return await documents.ToListAsync();
    }

    public async Task<List<Document>> SearchDocumentsAsync(string term, int requestingUserId)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return new List<Document>();
        }

        var lower = term.ToLower();

        var candidates = await _context.Documents
            .Include(d => d.UploadedByUser)
            .Include(d => d.Project)
            .Where(d => d.ScanStatus == ScanStatus.Clean && (
                d.Title.ToLower().Contains(lower) ||
                (d.Description != null && d.Description.ToLower().Contains(lower)) ||
                (d.Tags != null && d.Tags.ToLower().Contains(lower)) ||
                d.UploadedByUser.DisplayName.ToLower().Contains(lower) ||
                (d.Project != null && d.Project.Name.ToLower().Contains(lower))))
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();

        var accessible = new List<Document>();
        foreach (var candidate in candidates)
        {
            if (await CanAccessAsync(candidate, requestingUserId))
            {
                accessible.Add(candidate);
            }
        }

        return accessible;
    }

    public async Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId)
    {
        if (!await IsProjectMemberAsync(projectId, requestingUserId))
        {
            return new List<Document>();
        }

        return await _context.Documents
            .Include(d => d.UploadedByUser)
            .Where(d => d.ProjectId == projectId && d.ScanStatus == ScanStatus.Clean)
            .OrderByDescending(d => d.UploadedDate)
            .ToListAsync();
    }

    public async Task<Document?> GetDocumentAsync(int documentId, int requestingUserId)
    {
        var document = await _context.Documents
            .Include(d => d.UploadedByUser)
            .Include(d => d.Project)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (document == null || !await CanAccessAsync(document, requestingUserId))
        {
            return null;
        }

        return document;
    }

    public async Task<DocumentContent?> GetDocumentContentAsync(int documentId, int requestingUserId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || !await CanAccessAsync(document, requestingUserId))
        {
            return null;
        }

        if (document.ScanStatus != ScanStatus.Clean)
        {
            return null; // Caller distinguishes scan state via GetDocumentAsync.
        }

        var stream = await _fileStorage.DownloadAsync(document.FilePath);
        if (stream == null)
        {
            return null;
        }

        await LogActivityAsync(document.DocumentId, requestingUserId, DocumentAction.Download);

        return new DocumentContent(stream, document.FileType, document.FileName);
    }

    public async Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadata metadata, int requestingUserId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || !await CanManageAsync(document, requestingUserId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(metadata.Title) || !DocumentCategories.All.Contains(metadata.Category))
        {
            return false;
        }

        document.Title = metadata.Title.Trim();
        document.Description = metadata.Description;
        document.Category = metadata.Category;
        document.Tags = metadata.Tags;
        document.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReplaceFileAsync(int documentId, Stream content, string fileName, int requestingUserId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || !await CanManageAsync(document, requestingUserId))
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Buffer to determine size and enforce the size limit without trusting the stream length.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        if (buffer.Length > _options.MaxFileSizeBytes)
        {
            return false;
        }

        buffer.Position = 0;
        var newPath = await _fileStorage.UploadAsync(buffer, fileName, document.UploadedByUserId, document.ProjectId);

        // Remove the previous file after the new one is safely stored.
        var oldPath = document.FilePath;

        document.FilePath = newPath;
        document.FileName = fileName;
        document.FileType = ResolveContentType(fileName);
        document.FileSizeBytes = buffer.Length;
        document.ScanStatus = ScanStatus.Pending; // must be re-scanned
        document.UpdatedDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (!string.Equals(oldPath, newPath, StringComparison.Ordinal))
        {
            await _fileStorage.DeleteAsync(oldPath);
        }

        await _scanQueue.EnqueueAsync(document.DocumentId);
        return true;
    }

    public async Task<bool> DeleteDocumentAsync(int documentId, int requestingUserId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || !await CanManageAsync(document, requestingUserId))
        {
            return false;
        }

        await _fileStorage.DeleteAsync(document.FilePath);

        // Shares and activities cascade-delete with the document (see DbContext config).
        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ShareDocumentAsync(int documentId, int shareWithUserId, int requestingUserId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null)
        {
            return false;
        }

        var requester = await _context.Users.FindAsync(requestingUserId);
        var isOwner = document.UploadedByUserId == requestingUserId;
        var isAdmin = requester?.Role == UserRole.Administrator;
        if (!isOwner && !isAdmin)
        {
            return false;
        }

        var recipient = await _context.Users.FindAsync(shareWithUserId);
        if (recipient == null || shareWithUserId == document.UploadedByUserId)
        {
            return false;
        }

        var alreadyShared = await _context.DocumentShares
            .AnyAsync(s => s.DocumentId == documentId && s.SharedWithUserId == shareWithUserId);
        if (alreadyShared)
        {
            return false;
        }

        _context.DocumentShares.Add(new DocumentShare
        {
            DocumentId = documentId,
            SharedWithUserId = shareWithUserId,
            SharedByUserId = requestingUserId,
            SharedDate = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        await LogActivityAsync(documentId, requestingUserId, DocumentAction.Share);

        await _notificationService.CreateNotificationAsync(new Notification
        {
            UserId = shareWithUserId,
            Title = "A document was shared with you",
            Message = $"{requester?.DisplayName ?? "A colleague"} shared '{document.Title}' with you.",
            Type = NotificationType.DocumentShared,
            Priority = NotificationPriority.Informational
        });

        return true;
    }

    public async Task<List<Document>> GetSharedWithMeAsync(int requestingUserId)
    {
        return await _context.DocumentShares
            .Where(s => s.SharedWithUserId == requestingUserId && s.Document.ScanStatus == ScanStatus.Clean)
            .Include(s => s.Document).ThenInclude(d => d.UploadedByUser)
            .OrderByDescending(s => s.SharedDate)
            .Select(s => s.Document)
            .ToListAsync();
    }

    public async Task<bool> AttachToTaskAsync(int documentId, int taskId, int requestingUserId)
    {
        var document = await _context.Documents.FindAsync(documentId);
        if (document == null || !await CanManageAsync(document, requestingUserId))
        {
            return false;
        }

        var task = await _context.Tasks.FindAsync(taskId);
        if (task == null || task.ProjectId == null)
        {
            return false;
        }

        if (!await IsProjectMemberAsync(task.ProjectId.Value, requestingUserId))
        {
            return false;
        }

        // Attaching to a task associates the document with the task's project.
        document.ProjectId = task.ProjectId;
        document.UpdatedDate = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<List<Document>> GetRecentDocumentsAsync(int requestingUserId, int count = 5)
    {
        return await _context.Documents
            .Where(d => d.UploadedByUserId == requestingUserId && d.ScanStatus == ScanStatus.Clean)
            .OrderByDescending(d => d.UploadedDate)
            .Take(count)
            .ToListAsync();
    }

    public async Task<int> GetDocumentCountAsync(int requestingUserId)
    {
        return await _context.Documents
            .CountAsync(d => d.UploadedByUserId == requestingUserId && d.ScanStatus == ScanStatus.Clean);
    }

    public async Task<DocumentReport?> GetActivityReportAsync(int requestingUserId)
    {
        var requester = await _context.Users.FindAsync(requestingUserId);
        if (requester?.Role != UserRole.Administrator)
        {
            return null;
        }

        var mostUploadedTypes = await _context.Documents
            .GroupBy(d => d.FileType)
            .Select(g => new FileTypeCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var mostActiveUploaders = await _context.Documents
            .GroupBy(d => d.UploadedByUser.DisplayName)
            .Select(g => new UploaderCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync();

        var accessPatterns = await _context.DocumentActivities
            .GroupBy(a => a.Action)
            .Select(g => new ActionCount(g.Key.ToString(), g.Count()))
            .ToListAsync();

        return new DocumentReport(mostUploadedTypes, mostActiveUploaders, accessPatterns);
    }

    // ---- Helpers -------------------------------------------------------------

    private bool IsValidUpload(string title, string category, string fileName, long size)
    {
        if (string.IsNullOrWhiteSpace(title) || !DocumentCategories.All.Contains(category))
        {
            return false;
        }

        if (size <= 0 || size > _options.MaxFileSizeBytes)
        {
            return false;
        }

        var extension = Path.GetExtension(fileName);
        return !string.IsNullOrEmpty(extension) &&
               _options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> IsProjectMemberAsync(int projectId, int userId)
    {
        var isManager = await _context.Projects
            .AnyAsync(p => p.ProjectId == projectId && p.ProjectManagerId == userId);
        if (isManager)
        {
            return true;
        }

        return await _context.ProjectMembers
            .AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == userId);
    }

    private async Task<bool> CanAccessAsync(Document document, int userId)
    {
        if (document.UploadedByUserId == userId)
        {
            return true;
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        if (user.Role == UserRole.Administrator)
        {
            return true;
        }

        if (document.ProjectId.HasValue && await IsProjectMemberAsync(document.ProjectId.Value, userId))
        {
            return true;
        }

        var isSharedWith = await _context.DocumentShares
            .AnyAsync(s => s.DocumentId == document.DocumentId && s.SharedWithUserId == userId);
        if (isSharedWith)
        {
            return true;
        }

        // Team Leads may access documents uploaded by users in the same department (clarify Q1).
        if (user.Role == UserRole.TeamLead && !string.IsNullOrEmpty(user.Department))
        {
            var uploader = await _context.Users.FindAsync(document.UploadedByUserId);
            if (uploader != null &&
                string.Equals(uploader.Department, user.Department, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> CanManageAsync(Document document, int userId)
    {
        if (document.UploadedByUserId == userId)
        {
            return true;
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        if (user.Role == UserRole.Administrator)
        {
            return true;
        }

        // Project Manager of the document's project.
        if (document.ProjectId.HasValue)
        {
            return await _context.Projects
                .AnyAsync(p => p.ProjectId == document.ProjectId && p.ProjectManagerId == userId);
        }

        return false;
    }

    private async Task NotifyProjectMembersAsync(Document document, int uploaderId)
    {
        if (!document.ProjectId.HasValue)
        {
            return;
        }

        var project = await _context.Projects
            .Include(p => p.ProjectMembers)
            .FirstOrDefaultAsync(p => p.ProjectId == document.ProjectId);
        if (project == null)
        {
            return;
        }

        var recipientIds = project.ProjectMembers.Select(pm => pm.UserId).ToList();
        recipientIds.Add(project.ProjectManagerId);

        foreach (var recipientId in recipientIds.Distinct().Where(id => id != uploaderId))
        {
            await _notificationService.CreateNotificationAsync(new Notification
            {
                UserId = recipientId,
                Title = "New project document",
                Message = $"A new document '{document.Title}' was added to project '{project.Name}'.",
                Type = NotificationType.ProjectUpdate,
                Priority = NotificationPriority.Informational
            });
        }
    }

    private async Task LogActivityAsync(int documentId, int userId, DocumentAction action)
    {
        _context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = documentId,
            UserId = userId,
            Action = action,
            Timestamp = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
    }

    private static string ResolveContentType(string fileName)
    {
        var provider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
        return provider.TryGetContentType(fileName, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}

// ---- DTOs --------------------------------------------------------------------

public record DocumentUploadRequest(
    Stream Content,
    string FileName,
    long FileSizeBytes,
    string ContentType,
    string Title,
    string? Description,
    string Category,
    string? Tags,
    int? ProjectId);

public enum DocumentSortField
{
    Title,
    UploadedDate,
    Category,
    FileSize
}

public record DocumentQuery(
    string? Category,
    int? ProjectId,
    DateTime? FromDate,
    DateTime? ToDate,
    DocumentSortField SortBy,
    bool Descending);

public record DocumentMetadata(string Title, string? Description, string Category, string? Tags);

public record DocumentContent(Stream Stream, string ContentType, string FileName);

public record FileTypeCount(string FileType, int Count);
public record UploaderCount(string Uploader, int Count);
public record ActionCount(string Action, int Count);

public record DocumentReport(
    IReadOnlyList<FileTypeCount> MostUploadedTypes,
    IReadOnlyList<UploaderCount> MostActiveUploaders,
    IReadOnlyList<ActionCount> AccessPatterns);
