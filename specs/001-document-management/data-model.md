# Phase 1 Data Model: Document Upload and Management

Entities follow existing conventions: integer `[Key]` identity columns, data
annotations for `MaxLength`/`Required`, enums for fixed sets where an enum does not
conflict with a stakeholder constraint, and navigation properties with `[ForeignKey]`.
New `DbSet`s, relationships, and indexes are added in `ApplicationDbContext.OnModelCreating`.

## Entity: Document

Represents an uploaded file plus its metadata.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `DocumentId` | int | `[Key]`, identity | Integer key (stakeholder constraint) |
| `Title` | string | `[Required]`, `MaxLength(255)` | User-provided |
| `Description` | string? | `MaxLength(2000)` | Optional |
| `Category` | string | `[Required]`, `MaxLength(100)` | Text value validated against fixed list |
| `Tags` | string? | `MaxLength(500)` | Comma-separated custom tags |
| `FileName` | string | `[Required]`, `MaxLength(255)` | Original display filename |
| `FilePath` | string | `[Required]`, `MaxLength(500)` | GUID-based relative storage path (never user-supplied) |
| `FileType` | string | `[Required]`, `MaxLength(255)` | MIME type; 255 for Office types |
| `FileSizeBytes` | long | `[Required]` | Captured from upload |
| `UploadedByUserId` | int | `[Required]`, FK → User | Uploader |
| `ProjectId` | int? | FK → Project | Optional association |
| `ScanStatus` | ScanStatus (enum) | `[Required]`, default `Pending` | Set by async scan worker |
| `UploadedDate` | DateTime | default `UtcNow` | Auto-captured |
| `UpdatedDate` | DateTime | default `UtcNow` | Updated on metadata edit / replace |

**Enum `ScanStatus`**: `Pending, Clean, Infected`. A document is only downloadable,
previewable, or returned in search/list results when `ScanStatus == Clean` (async
virus scanning — see plan.md). On `Infected`, the stored file is deleted and the
uploader is notified; the metadata row may be retained for audit.

**Navigation**: `UploadedByUser` (User), `Project` (Project?), `Shares`
(ICollection<DocumentShare>), `Activities` (ICollection<DocumentActivity>).

**Validation rules**:
- `Category` MUST be one of: Project Documents, Team Resources, Personal Files,
  Reports, Presentations, Other (FR-004).
- `FileSizeBytes` MUST be ≤ 26,214,400 (25 MB) (FR-003).
- `FileType`/extension MUST be in the supported whitelist (FR-002).
- `FilePath` MUST be unique and GUID-based (FR-008, security).

**Delete behavior**: Hard delete (FR-016 / spec assumption). Deleting a Document
removes its `DocumentShare` and `DocumentActivity` rows (`OnDelete(Cascade)`) and
its file on disk via `IFileStorageService.DeleteAsync`.

## Entity: DocumentShare

A grant giving a specific recipient access to a document.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `DocumentShareId` | int | `[Key]`, identity | |
| `DocumentId` | int | `[Required]`, FK → Document | |
| `SharedWithUserId` | int | `[Required]`, FK → User | Recipient |
| `SharedByUserId` | int | `[Required]`, FK → User | Owner who shared |
| `SharedDate` | DateTime | default `UtcNow` | |

**Navigation**: `Document`, `SharedWithUser` (User), `SharedByUser` (User).
**Uniqueness**: composite index on (`DocumentId`, `SharedWithUserId`) to prevent
duplicate shares. FK to User uses `DeleteBehavior.Restrict` to avoid multiple
cascade paths (consistent with existing User relationships).

## Entity: DocumentActivity

Audit log entry for document actions (FR-022).

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `DocumentActivityId` | int | `[Key]`, identity | |
| `DocumentId` | int | `[Required]`, FK → Document | |
| `UserId` | int | `[Required]`, FK → User | Actor |
| `Action` | DocumentAction (enum) | `[Required]` | Upload, Download, Delete, Share |
| `Timestamp` | DateTime | default `UtcNow` | |

**Enum `DocumentAction`**: `Upload, Download, Delete, Share`.
**Navigation**: `Document`, `User` (`Restrict`).

## Relationships summary

```text
User (1) ───< Document (uploader)            [Restrict]
Project (0..1) ───< Document                 [SetNull on project delete]
Document (1) ───< DocumentShare              [Cascade]
User (1) ───< DocumentShare (recipient)      [Restrict]
Document (1) ───< DocumentActivity           [Cascade]
User (1) ───< DocumentActivity (actor)       [Restrict]
```

## Indexes (performance — SC-007/SC-008)

- `Document.UploadedByUserId`
- `Document.ProjectId`
- `Document.Category`
- `Document.UploadedDate`
- `DocumentShare` unique (`DocumentId`, `SharedWithUserId`)
- `DocumentShare.SharedWithUserId` (for "Shared with Me")
- `DocumentActivity.DocumentId`

## Access rules (derived from FR-011, FR-017, FR-023, clarify Q1)

A user may access a document if ANY of:
- they are the uploader (`UploadedByUserId == userId`);
- the document has a `ProjectId` and the user is a member/manager of that project;
- a `DocumentShare` exists for (`DocumentId`, `userId`);
- the user is a **Team Lead** and the uploader is in the **same Department**;
- the user is an **Administrator**.

Management/delete: uploader, Project Manager of the document's project, or
Administrator. All checks are enforced in `DocumentService` via `requestingUserId`
(no reliance on UI), including on download/preview (IDOR protection).

## Existing entities touched

- **User**: add `Department`-based lookups (field already exists); optional inverse
  navigation `UploadedDocuments`.
- **Project**: optional inverse navigation `Documents`.
- **Notification**: add `NotificationType.DocumentShared` (and reuse for new
  project-document alerts).
