using System.Text;

namespace ContosoDashboard.Services.Scanning;

/// <summary>
/// Training scanner. Runs fully offline: it accepts files and flags only the
/// industry-standard EICAR anti-virus test string as infected, so the infected
/// path can be exercised without real malware. Replace with a real scanning engine
/// in production (see plan.md).
/// </summary>
public class StubFileScanner : IFileScanner
{
    private const string EicarSignature = "EICAR-STANDARD-ANTIVIRUS-TEST-FILE";
    private const int MaxBytesToInspect = 1024 * 1024; // 1 MB is enough to find the signature

    public async Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[MaxBytesToInspect];
        var total = 0;
        int read;
        while (total < buffer.Length &&
               (read = await content.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken)) > 0)
        {
            total += read;
        }

        var text = Encoding.ASCII.GetString(buffer, 0, total);
        return text.Contains(EicarSignature, StringComparison.Ordinal)
            ? ScanResult.Infected
            : ScanResult.Clean;
    }
}
