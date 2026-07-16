namespace ContosoDashboard.Services.Scanning;

public enum ScanResult
{
    Clean,
    Infected
}

/// <summary>
/// Performs a malware scan of file content. The training implementation
/// (<see cref="StubFileScanner"/>) runs offline; a production implementation can
/// call a real scanning engine.
/// </summary>
public interface IFileScanner
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default);
}
