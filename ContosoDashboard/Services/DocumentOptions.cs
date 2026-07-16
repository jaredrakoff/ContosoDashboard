namespace ContosoDashboard.Services;

/// <summary>
/// Configuration for document storage and validation (bound from the
/// "DocumentStorage" configuration section).
/// </summary>
public class DocumentOptions
{
    public string StorageRoot { get; set; } = "App_Data/uploads";

    /// <summary>Maximum allowed file size in bytes (default 25 MB).</summary>
    public long MaxFileSizeBytes { get; set; } = 26_214_400;

    public string[] AllowedExtensions { get; set; } =
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png"
    };

    public string ScanQueueName { get; set; } = "document-scan-requests";
}

/// <summary>Fixed set of document categories (stored as text).</summary>
public static class DocumentCategories
{
    public const string ProjectDocuments = "Project Documents";
    public const string TeamResources = "Team Resources";
    public const string PersonalFiles = "Personal Files";
    public const string Reports = "Reports";
    public const string Presentations = "Presentations";
    public const string Other = "Other";

    public static readonly string[] All =
    {
        ProjectDocuments, TeamResources, PersonalFiles, Reports, Presentations, Other
    };
}
