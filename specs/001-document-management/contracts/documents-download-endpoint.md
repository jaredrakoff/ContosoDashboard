# Contract: Documents Download/Preview Endpoint

Files are stored outside `wwwroot`, so retrieval goes through an authorized MVC
controller action (added via `app.MapControllers()` in `Program.cs`). This enforces
authorization on every retrieval (Security by Design, IDOR protection — FR-007,
FR-013, FR-014, FR-023).

## Routes

| Method | Route | Purpose | Response |
|--------|-------|---------|----------|
| GET | `/documents/{id}/download` | Download the file | `200` `FileStreamResult` with `Content-Disposition: attachment` |
| GET | `/documents/{id}/preview` | Inline preview (PDF/images only) | `200` file with `Content-Disposition: inline`; `415` for non-previewable types |

## Behavior

- Endpoint requires authentication (`[Authorize]`); the current user's id is taken
  from claims (never from the request body/query).
- Delegates to `IDocumentService.GetDocumentContentAsync(id, requestingUserId)`.
- If the service returns `null` (not found OR not authorized), respond `404`
  (indistinguishable to avoid leaking existence — IDOR-safe).
- If the document exists and is authorized but its `ScanStatus` is not `Clean`
  (still `Pending`, or `Infected`), respond `409 Conflict` (file withheld pending
  virus scan / blocked as infected) — the file is never streamed.
- On success, stream the file with the stored `ContentType` and original `FileName`.
- A `Download` activity is logged by the service (FR-022).

## Status codes

| Code | Condition |
|------|-----------|
| `200` | Authorized; file streamed |
| `401` | Unauthenticated (handled by cookie auth → redirect to `/login`) |
| `404` | Document missing or requester not authorized |
| `409` | Document not yet cleared by the async virus scan (`Pending`) or blocked (`Infected`) |
| `415` | Preview requested for a non-previewable type |

## Security notes

- No user-supplied path ever reaches the filesystem; the controller only passes the
  integer `id` to the service, which resolves the stored GUID path.
- Preview restricts `Content-Type` to `application/pdf`, `image/jpeg`, `image/png`.
- Response sets `X-Content-Type-Options: nosniff` (already applied globally).
