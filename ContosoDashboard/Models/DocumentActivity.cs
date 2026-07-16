using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public DocumentAction Action { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;
}
