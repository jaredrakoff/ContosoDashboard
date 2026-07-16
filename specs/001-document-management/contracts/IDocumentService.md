# Contract: IDocumentService

Business-logic service for documents, following the existing service pattern
(constructor-injected `ApplicationDbContext`, scoped lifetime, authorization via a
`requestingUserId` parameter that returns `null`/`false`/empty on denial — never
throwing for authorization, consistent with `ProjectService`).

```csharp
namespace ContosoDashboard.Services;

public interface IDocumentService
{
    // US1 - Upload. Validates type/size/category, stores bytes via IFileStorageService,
    // persists metadata, writes an Upload activity, and raises project notifications.
    Task<Document?> UploadDocumentAsync(DocumentUploadRequest request, int requestingUserId);

    // US2 - My Documents with sort/filter.
    Task<List<Document>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery query);

    // US2 - Search (title, description, tags, uploader, project), access-scoped.
    Task<List<Document>> SearchDocumentsAsync(string term, int requestingUserId);

    // US2/US6 - Documents for a project (members only).
    Task<List<Document>> GetProjectDocumentsAsync(int projectId, int requestingUserId);

    // US3 - Metadata fetch with access check (returns null if unauthorized/absent).
    Task<Document?> GetDocumentAsync(int documentId, int requestingUserId);

    // US3 - Authorized byte stream for download/preview; logs a Download activity.
    Task<DocumentContent?> GetDocumentContentAsync(int documentId, int requestingUserId);

    // US4 - Edit metadata (uploader / PM of project / Admin).
    Task<bool> UpdateMetadataAsync(int documentId, DocumentMetadata metadata, int requestingUserId);

    // US4 - Replace file (overwrite; no version history).
    Task<bool> ReplaceFileAsync(int documentId, Stream content, string fileName, int requestingUserId);

    // US4 - Delete (permanent): removes row, shares, activities, and the stored file.
    Task<bool> DeleteDocumentAsync(int documentId, int requestingUserId);

    // US5 - Share with a specific user; notifies recipient; logs a Share activity.
    Task<bool> ShareDocumentAsync(int documentId, int shareWithUserId, int requestingUserId);

    // US5 - Documents shared with the requesting user.
    Task<List<Document>> GetSharedWithMeAsync(int requestingUserId);

    // US6 - Attach/associate an uploaded document with a task (inherits task's project).
    Task<bool> AttachToTaskAsync(int documentId, int taskId, int requestingUserId);

    // US6 - Dashboard widget: most recent N uploads by the user.
    Task<List<Document>> GetRecentDocumentsAsync(int requestingUserId, int count = 5);

    // US6 - Dashboard summary count.
    Task<int> GetDocumentCountAsync(int requestingUserId);

    // Reporting (Administrator only) - returns empty if not Administrator.
    Task<DocumentReport?> GetActivityReportAsync(int requestingUserId);
}
```

## Supporting shapes (DTOs)

```csharp
public record DocumentUploadRequest(
    Stream Content, string FileName, long FileSizeBytes, string ContentType,
    string Title, string? Description, string Category, string? Tags, int? ProjectId);

public record DocumentQuery(
    string? Category, int? ProjectId, DateTime? FromDate, DateTime? ToDate,
    DocumentSortField SortBy, bool Descending);

public enum DocumentSortField { Title, UploadedDate, Category, FileSize }

public record DocumentMetadata(string Title, string? Description, string Category, string? Tags);

public record DocumentContent(Stream Stream, string ContentType, string FileName);

public record DocumentReport(
    IReadOnlyList<(string FileType, int Count)> MostUploadedTypes,
    IReadOnlyList<(string Uploader, int Count)> MostActiveUploaders,
    IReadOnlyList<(string Action, int Count)> AccessPatterns);
```

## Authorization contract (per method)

| Method | Allowed | On denial |
|--------|---------|-----------|
| Upload | any authenticated user (project association requires membership) | returns `null` |
| GetMyDocuments / Recent / Count | self only | scoped to `requestingUserId` |
| Search | any authenticated user | results scoped to accessible docs (FR-012) |
| GetProjectDocuments | project members/manager, Admin | returns empty |
| GetDocument / GetDocumentContent | access rules in data-model | returns `null` (IDOR-safe) |
| UpdateMetadata / ReplaceFile | uploader, PM of doc's project, Admin | returns `false` |
| DeleteDocument | uploader, PM of doc's project, Admin | returns `false` |
| ShareDocument | uploader/owner, Admin | returns `false` |
| GetSharedWithMe | self only | scoped |
| AttachToTask | user with access to both doc and task's project | returns `false` |
| GetActivityReport | Administrator only | returns `null` |

Validation failures (unsupported type, >25 MB, missing title/category) result in a
rejected upload with a clear message surfaced by the calling page (FR-002–FR-004).
