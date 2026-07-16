using System.Security.Claims;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ContosoDashboard.Controllers;

/// <summary>
/// Serves document downloads/previews. Files live outside wwwroot, so retrieval
/// goes through this authorized endpoint (Security by Design / IDOR protection).
/// </summary>
[Authorize]
[Route("documents")]
public class DocumentsController : Controller
{
    private static readonly string[] PreviewableTypes = { "application/pdf", "image/jpeg", "image/png" };

    private readonly IDocumentService _documentService;

    public DocumentsController(IDocumentService documentService)
    {
        _documentService = documentService;
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var document = await _documentService.GetDocumentAsync(id, userId.Value);
        if (document == null)
        {
            return NotFound(); // missing OR unauthorized — indistinguishable (IDOR-safe)
        }

        if (document.ScanStatus != ScanStatus.Clean)
        {
            return Conflict(); // 409 — not yet cleared by the async virus scan (or blocked)
        }

        var content = await _documentService.GetDocumentContentAsync(id, userId.Value);
        if (content == null)
        {
            return NotFound();
        }

        return File(content.Stream, content.ContentType, content.FileName);
    }

    [HttpGet("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id)
    {
        var userId = GetUserId();
        if (userId == null)
        {
            return Unauthorized();
        }

        var document = await _documentService.GetDocumentAsync(id, userId.Value);
        if (document == null)
        {
            return NotFound();
        }

        if (document.ScanStatus != ScanStatus.Clean)
        {
            return Conflict();
        }

        if (!PreviewableTypes.Contains(document.FileType))
        {
            return StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var content = await _documentService.GetDocumentContentAsync(id, userId.Value);
        if (content == null)
        {
            return NotFound();
        }

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{content.FileName}\"";
        return File(content.Stream, content.ContentType);
    }

    private int? GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : null;
    }
}
