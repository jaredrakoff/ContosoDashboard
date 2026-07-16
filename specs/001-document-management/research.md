# Phase 0 Research: Document Upload and Management

All Technical Context items were resolvable from the stakeholder document, the
existing codebase conventions, and the constitution. No open `NEEDS CLARIFICATION`
items remain. Decisions below record the rationale and rejected alternatives.

## 1. File byte storage location & access

- **Decision**: Store file bytes on the local filesystem in a directory outside
  `wwwroot` (e.g., `App_Data/uploads`), using the path pattern
  `{userId}/{projectId or "personal"}/{guid}.{ext}`. Serve files only through an
  authorized MVC controller endpoint (`DocumentsController`).
- **Rationale**: Files outside `wwwroot` cannot be fetched directly by URL, so
  every retrieval passes through authorization (Security by Design, IDOR
  protection). GUID filenames prevent path traversal and name collisions.
- **Alternatives considered**: Storing under `wwwroot` (rejected — bypasses
  authorization); storing bytes in the database as `varbinary` (rejected —
  complicates the future Azure Blob migration and bloats the DB for a training app).

## 2. Storage abstraction for cloud migration

- **Decision**: Introduce `IFileStorageService` with `UploadAsync`, `DownloadAsync`,
  `DeleteAsync`, `GetUrlAsync`; implement `LocalFileStorageService` using `System.IO`.
- **Rationale**: Directly satisfies constitution Principle II (Infrastructure
  Abstraction) and Principle I (cloud migration path). A future
  `AzureBlobStorageService` can be swapped via DI with the same blob-name pattern.
- **Alternatives considered**: Calling `System.IO` directly from the service
  (rejected — violates abstraction principle and blocks the migration path).

## 3. Upload ordering to avoid orphans / duplicate keys

- **Decision**: Generate the unique GUID-based path → write the file to disk →
  then persist the metadata row. Integer `DocumentId` is DB-generated.
- **Rationale**: Matches the stakeholder "Implementation Notes" and prevents
  orphaned DB rows if the file write fails and duplicate-key errors from empty or
  non-unique paths.
- **Alternatives considered**: Insert metadata first to obtain the ID, then write
  the file (rejected — leaves orphaned rows on write failure).

## 4. Malware screening in an offline app (asynchronous background job)

- **Decision**: Enforce synchronous validation (file-type **whitelist** + size) at
  upload, then run the malware scan **asynchronously** via an `IFileScanQueue`
  abstraction. Uploaded documents start as `ScanStatus = Pending` and are withheld
  from download/preview/search until marked `Clean`. The **offline default** is an
  in-process `IHostedService` worker over a `Channel`-based queue with a stub
  scanner; the **production** implementation uses **Azure Queue Storage** plus an
  **Azure Function** with a **Queue Storage trigger** (details in plan.md).
- **Rationale**: Matches the stakeholder background-job requirement, keeps uploads
  responsive for 25 MB files, quarantines files until cleared (Security by Design),
  and preserves offline-first via the abstraction (Principle I & II).
- **Alternatives considered**: Synchronous inline scan (rejected — blocks upload and
  is not a background job); a hard Azure Functions dependency in the training build
  (rejected — violates offline-first; kept as an optional production swap); bundling
  ClamAV locally (rejected — external dependency breaks offline simplicity).

## 5. Authorization model & Team Lead scope

- **Decision**: Reuse the existing service-level `requestingUserId` pattern. Access
  rules: uploader (owner) full control of own docs; project members can view/download
  project documents; Project Managers manage all docs in their projects; **Team Leads
  can view/manage documents uploaded by users in the same `Department`** (clarify Q1);
  Administrators have full access; share recipients get view/download.
- **Rationale**: Consistent with `ProjectService`/`NotificationService` patterns and
  the confirmed clarification; `User.Department` already exists.
- **Alternatives considered**: Project- or manager-hierarchy-based team scoping
  (rejected per clarify Q1 answer A).

## 6. Data typing constraints

- **Decision**: `DocumentId` is `int` (identity); `Category` stored as text
  (string) validated against a fixed list; `FileType` (MIME) column `MaxLength(255)`;
  `FilePath` `MaxLength(500)` for GUID-based paths.
- **Rationale**: Directly from stakeholder "Technical Constraints" and consistent
  with existing entities (`User.Email` 255, int keys).
- **Alternatives considered**: GUID document keys / enum category (rejected by
  stakeholder constraints for consistency and simplicity).

## 7. Serving downloads/preview from Blazor Server

- **Decision**: Add an MVC controller (`DocumentsController`) and
  `app.MapControllers()`; download returns `FileStreamResult`, preview returns the
  file inline (`Content-Disposition: inline`) for PDF/images.
- **Rationale**: Blazor Server cannot stream authorized file responses as cleanly as
  a controller action; controllers integrate with the existing cookie auth pipeline.
- **Alternatives considered**: Base64 streaming through SignalR (rejected — poor
  performance for 25 MB files and no clean content-type/inline handling).

## 8. Persistence & migrations

- **Decision**: Continue using `context.Database.EnsureCreated()` as the app does
  today; add the new `DbSet`s, relationships, and indexes (uploader, project,
  category, upload date) in `OnModelCreating`.
- **Rationale**: Matches existing training setup (no migrations pipeline) and keeps
  offline first-run auto-creation working.
- **Alternatives considered**: Introducing EF migrations (rejected — inconsistent
  with the current `EnsureCreated` training approach; out of scope).

## 9. Notifications & integration reuse

- **Decision**: Reuse `INotificationService.CreateNotificationAsync` for share and
  new-project-document notifications; add a `NotificationType` value for documents.
  Reuse dashboard/project/task pages for integration points.
- **Rationale**: Layered reuse avoids duplicate infrastructure (Training Clarity).
- **Alternatives considered**: A separate document-notification mechanism (rejected —
  duplicates existing capability).
