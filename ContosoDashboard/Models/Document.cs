using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(100)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Tags { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    // GUID-based relative storage path (never user-supplied).
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    // MIME type; 255 chars to accommodate long Office content types.
    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    [Required]
    public long FileSizeBytes { get; set; }

    [Required]
    public int UploadedByUserId { get; set; }

    public int? ProjectId { get; set; }

    [Required]
    public ScanStatus ScanStatus { get; set; } = ScanStatus.Pending;

    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("UploadedByUserId")]
    public virtual User UploadedByUser { get; set; } = null!;

    [ForeignKey("ProjectId")]
    public virtual Project? Project { get; set; }

    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<DocumentActivity> Activities { get; set; } = new List<DocumentActivity>();
}
