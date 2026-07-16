# Contract: IFileStorageService

Infrastructure abstraction for file byte storage (Constitution Principle II).
Local training implementation uses `System.IO`; a future `AzureBlobStorageService`
implements the same contract with no business-logic changes.

```csharp
namespace ContosoDashboard.Services.FileStorage;

public interface IFileStorageService
{
    // Writes the stream to a unique, GUID-based location derived from the pattern
    // {userId}/{projectSegment}/{guid}.{ext} and returns the stored relative path.
    // Implementations MUST create the file before the caller persists metadata.
    Task<string> UploadAsync(Stream content, string originalFileName,
                             int userId, int? projectId);

    // Opens a readable stream for a previously stored relative path.
    // Throws/returns null if the path does not exist.
    Task<Stream> DownloadAsync(string filePath);

    // Deletes the stored file. Safe no-op if already absent.
    Task DeleteAsync(string filePath);

    // Returns a retrieval URL/route for the stored file (authorized endpoint).
    string GetUrl(string filePath);
}
```

## Behavioral contract

| Aspect | Requirement |
|--------|-------------|
| Path generation | Relative path MUST be GUID-based; original file name MUST NOT be used in the path (path-traversal protection). |
| Ordering | `UploadAsync` MUST persist bytes before `DocumentService` commits metadata (FR-008). |
| Location | Local implementation MUST write outside `wwwroot` (e.g., `App_Data/uploads`). |
| Idempotent delete | `DeleteAsync` on a missing path MUST NOT throw. |
| Migration | The same relative-path/blob-name pattern MUST work for an Azure Blob implementation. |

## Local implementation notes (`LocalFileStorageService`)

- Root directory from configuration (default `App_Data/uploads`), created if absent.
- Uses `Path.Combine` with validated segments only; never concatenates user input.
- `GetUrl` returns the download route (e.g., `/documents/{id}/download`) rather than
  a filesystem path.
