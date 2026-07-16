using Microsoft.Extensions.Options;

namespace ContosoDashboard.Services.FileStorage;

/// <summary>
/// Offline/training implementation of <see cref="IFileStorageService"/> using the
/// local filesystem. Files are stored OUTSIDE wwwroot so they can only be retrieved
/// through the authorized documents endpoint. Paths are GUID-based to prevent
/// collisions and path-traversal.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _root;

    public LocalFileStorageService(IHostEnvironment environment, IOptions<DocumentOptions> options)
    {
        _root = Path.Combine(environment.ContentRootPath, options.Value.StorageRoot);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> UploadAsync(Stream content, string originalFileName, int userId, int? projectId)
    {
        // Never use the user-supplied file name in the path. Only the extension is kept.
        var extension = Path.GetExtension(originalFileName);
        var projectSegment = projectId.HasValue ? projectId.Value.ToString() : "personal";
        var uniqueName = $"{Guid.NewGuid():N}{extension}";

        // Relative path uses forward slashes for portability (also valid as an Azure blob name).
        var relativePath = string.Join('/', userId.ToString(), projectSegment, uniqueName);
        var fullPath = ToFullPath(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var fileStream = File.Create(fullPath))
        {
            await content.CopyToAsync(fileStream);
        }

        return relativePath;
    }

    public Task<Stream?> DownloadAsync(string filePath)
    {
        var fullPath = ToFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string filePath)
    {
        var fullPath = ToFullPath(filePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public string GetUrl(string filePath) => filePath;

    private string ToFullPath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_root, normalized));

        // Defense in depth: ensure the resolved path stays within the storage root.
        var rootWithSep = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Resolved path escapes the storage root.");
        }

        return fullPath;
    }
}
