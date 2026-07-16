# Implementation Plan: Document Upload and Management

**Branch**: `001-document-management` | **Date**: 2026-07-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/001-document-management/spec.md`

## Summary

Add document upload and management to ContosoDashboard: authenticated users upload
supported files (≤25 MB) with metadata, then browse, search, download, preview,
manage (edit/replace/delete), and share documents, with role-based access and
in-app notifications, plus task and dashboard integration. Files are stored on the
local filesystem outside the web root behind an `IFileStorageService` abstraction
(swappable for Azure Blob Storage later); metadata lives in SQL Server LocalDB via
EF Core. The design mirrors the existing service + `requestingUserId` authorization
pattern to keep security in depth and preserve offline operation.

## Technical Context

**Language/Version**: C# 12 on .NET 8.0  
**Primary Dependencies**: ASP.NET Core 8.0, Blazor Server, Entity Framework Core 8.0 (SQL Server provider); Bootstrap 5.3 UI  
**Storage**: SQL Server LocalDB for metadata (EF Core, `EnsureCreated`); local filesystem for file bytes (outside `wwwroot`, e.g. `App_Data/uploads`) accessed only via authorized endpoints  
**Testing**: No automated test project exists in the repo; per constitution, tests are optional for this training feature. Validation is performed via the manual scenarios in `quickstart.md`.  
**Target Platform**: Windows developer machine, fully offline (no cloud/network dependencies)  
**Project Type**: Web application — single ASP.NET Core project (Blazor Server) at `ContosoDashboard/`  
**Performance Goals**: Document list ≤2 s for up to 500 documents; search ≤2 s; preview ≤3 s; upload ≤30 s for a 25 MB file (SC-006–SC-009)  
**Constraints**: Offline only; 25 MB max per file; whitelisted file types (PDF, Word, Excel, PowerPoint, text, JPEG, PNG); files stored outside `wwwroot`; GUID-based file paths (no path traversal, no user-supplied names); integer document IDs; category stored as text; MIME type field ≥255 chars  
**Scale/Scope**: Training-scale — a handful of seeded users, hundreds of documents; 6 user stories, ~23 functional requirements

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment | Status |
|-----------|------------|--------|
| I. Offline-First with Cloud Migration Path | Local filesystem + LocalDB only; `IFileStorageService` provides documented swap path to Azure Blob Storage with no business-logic change | ✅ PASS |
| II. Infrastructure Abstraction | New `IFileStorageService` interface with `LocalFileStorageService`; data access via existing EF Core `ApplicationDbContext`; DI-registered | ✅ PASS |
| III. Security by Design | Download/preview served via an authorized controller endpoint (files outside `wwwroot`); service-level authorization with `requestingUserId` prevents IDOR; extension whitelist + GUID filenames prevent traversal; `[Authorize]` on all new pages; uploaded files are quarantined until an asynchronous scan clears them | ✅ PASS |
| IV. Layered Separation of Concerns | Models (`Document`, `DocumentShare`, `DocumentActivity`), Services (`IDocumentService`, `IFileStorageService`, `IFileScanQueue`/`IFileScanner`), Data (DbContext sets/config), Pages (Razor) — no cross-layer leakage | ✅ PASS |
| V. Training Clarity Over Cleverness | Reuses established patterns; overwrite-on-replace (no versioning); hard delete; no new frameworks in the training build; assumptions documented in spec | ✅ PASS |
| I. (async scanning reconciliation) | Async scanning is consumed through an `IFileScanQueue` abstraction. The **default training implementation is fully in-process and offline** (an `IHostedService` background worker over an in-memory queue). The **Azure Queue Storage + Azure Function** implementation is the documented production swap and is NOT required to build or run offline. | ✅ PASS |

**Result**: PASS. The Azure Functions/Queue Storage scanner is an optional production implementation behind an abstraction; the offline default preserves Principle I. The added asynchronous mechanism (vs. a synchronous inline scan) is recorded in Complexity Tracking below.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-management/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── IDocumentService.md
│   ├── IFileStorageService.md
│   └── documents-download-endpoint.md
└── checklists/
    └── requirements.md  # From /speckit.specify
```

### Source Code (repository root)

Single existing ASP.NET Core project; new files added alongside current structure:

```text
ContosoDashboard/
├── Models/
│   ├── Document.cs            # NEW — document metadata entity (int key)
│   ├── DocumentShare.cs       # NEW — per-user share grant
│   └── DocumentActivity.cs    # NEW — audit log entry (upload/download/delete/share)
├── Services/
│   ├── DocumentService.cs     # NEW — IDocumentService + implementation (authz via requestingUserId)
│   ├── FileStorage/
│   │   ├── IFileStorageService.cs      # NEW — storage abstraction
│   │   └── LocalFileStorageService.cs  # NEW — System.IO implementation (App_Data/uploads)
│   └── Scanning/
│       ├── IFileScanQueue.cs            # NEW — enqueue scan requests (abstraction)
│       ├── IFileScanner.cs              # NEW — perform a scan (abstraction, pluggable)
│       ├── InMemoryFileScanQueue.cs     # NEW — offline default (Channel-based queue)
│       ├── FileScanBackgroundService.cs # NEW — IHostedService worker (offline default)
│       └── StubFileScanner.cs           # NEW — training scanner (whitelist/heuristic)
├── Data/
│   └── ApplicationDbContext.cs # MODIFIED — add DbSets, relationships, indexes for new entities
├── Controllers/
│   └── DocumentsController.cs  # NEW — authorized download/preview endpoint (files outside wwwroot)
├── Pages/
│   ├── Documents.razor         # NEW — My Documents (list/sort/filter/search)
│   ├── DocumentUpload.razor    # NEW — upload with metadata (or component reused elsewhere)
│   ├── SharedWithMe.razor      # NEW — documents shared with the user
│   ├── ProjectDetails.razor    # MODIFIED — project documents section
│   ├── Tasks.razor / task detail # MODIFIED — attach/upload/view task documents
│   └── Index.razor             # MODIFIED — Recent Documents widget + count card
├── Shared/
│   └── NavMenu.razor           # MODIFIED — Documents nav entry
└── Program.cs                  # MODIFIED — register IDocumentService, IFileStorageService, map controllers, upload size limits
```

**Structure Decision**: Extend the existing single Blazor Server project. No new
projects are introduced in the training build (Training Clarity + existing
single-project layout). File downloads require an MVC controller endpoint because
files live outside `wwwroot`; `app.MapControllers()` will be added to `Program.cs`.
The production Azure Function scanner (see below) lives in a **separate,
production-only project** that is not part of the offline training solution.

## Asynchronous Virus Scanning (background job)

Malware screening runs **asynchronously after upload** so large files (up to 25 MB)
do not block the upload response, while unscanned files remain unavailable for
download/preview.

### Upload → scan state machine

```text
Upload accepted → store bytes (IFileStorageService) → persist Document with
ScanStatus = Pending → enqueue scan request (IFileScanQueue) → return success.

Background consumer picks up the request → runs IFileScanner:
  - Clean    → ScanStatus = Clean    (document becomes downloadable/previewable)
  - Infected → ScanStatus = Infected (file deleted via IFileStorageService;
               DocumentActivity logged; uploader notified)
```

Download/preview and search results MUST exclude documents whose `ScanStatus` is not
`Clean` (the download endpoint returns `409 Conflict`/`404` for pending/infected).

### Abstractions (Principle II)

- `IFileScanQueue.EnqueueAsync(int documentId)` — hands off a scan request.
- `IFileScanner.ScanAsync(Stream content)` → `ScanResult { Clean | Infected }`.

`DocumentService` depends only on `IFileScanQueue`; it never knows whether the queue
is in-memory or Azure Queue Storage.

### Training/offline implementation (default — preserves Principle I)

- `InMemoryFileScanQueue`: a `System.Threading.Channels.Channel<int>`.
- `FileScanBackgroundService : BackgroundService` (`IHostedService`): dequeues
  request ids, loads the document, runs `StubFileScanner`, and updates `ScanStatus`.
- `StubFileScanner`: whitelist/heuristic checks (no external process); represents the
  real scanner's integration point. Registered via DI in `Program.cs`.

This keeps scanning fully in-process and offline — no cloud, no external services.

### Production implementation (Azure Functions + Queue Storage)

Swapped in via DI/configuration with **no change to `DocumentService`**:

- **`IFileScanQueue` → `AzureQueueScanQueue`**: `EnqueueAsync` writes a message
  (`{ documentId }`) to an **Azure Storage Queue** (e.g., `document-scan-requests`)
  using `Azure.Storage.Queues`.
- **Azure Function (separate project)** with a **Queue Storage trigger** bound to
  `document-scan-requests`:
  1. Reads the `documentId` from the dequeued message.
  2. Downloads the blob via the storage abstraction (`AzureBlobStorageService`).
  3. Runs a real scanning engine (e.g., Microsoft Defender / a scanning API).
  4. Updates the document's `ScanStatus` (Clean/Infected) and, on infection,
     deletes the blob and raises the uploader notification.
- **Reliability**: the queue trigger provides automatic retries; messages that
  exceed `maxDequeueCount` land on the `-poison` queue for investigation.
  Visibility timeout is set to exceed the maximum expected scan duration.
- **Config/bindings**: connection via `AzureWebJobsStorage`; queue name and scanner
  endpoint supplied through app settings — no business-logic or schema changes.

The blob-name/message contract is identical to the offline path, satisfying the
cloud-migration principle.

## Complexity Tracking

| Violation / Added complexity | Why Needed | Simpler Alternative Rejected Because |
|------------------------------|------------|--------------------------------------|
| Asynchronous scan pipeline (queue + background worker) instead of a synchronous inline scan | Explicit stakeholder requirement to process scans as a background job; keeps uploads responsive for 25 MB files and quarantines files until cleared | A synchronous inline scan is simpler but blocks the upload request and does not match the requested background-job design |
| Optional Azure Functions + Queue Storage scanner (production-only project) | Requested production implementation of the background scanner; provides retries/poison-queue reliability at cloud scale | Kept behind `IFileScanQueue` so the offline default has no cloud dependency; a hard Azure dependency was rejected because it would violate Constitution Principle I (offline-first) |
