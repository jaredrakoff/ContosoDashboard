namespace ContosoDashboard.Services.FileStorage;

/// <summary>
/// Infrastructure abstraction for file byte storage (Constitution Principle II).
/// Training uses <see cref="LocalFileStorageService"/>; a production
/// AzureBlobStorageService can implement the same contract with no business-logic
/// changes.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Writes the stream to a unique, GUID-based location derived from the pattern
    /// {userId}/{projectSegment}/{guid}.{ext} and returns the stored relative path.
    /// The file is persisted before the caller commits metadata (prevents orphans).
    /// </summary>
    Task<string> UploadAsync(Stream content, string originalFileName, int userId, int? projectId);

    /// <summary>Opens a readable stream for a stored relative path, or null if missing.</summary>
    Task<Stream?> DownloadAsync(string filePath);

    /// <summary>Deletes the stored file. Safe no-op if already absent.</summary>
    Task DeleteAsync(string filePath);

    /// <summary>Returns a retrieval reference for the stored file.</summary>
    string GetUrl(string filePath);
}
