# Tasks: Document Upload and Management

**Input**: Design documents from `specs/001-document-management/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [data-model.md](data-model.md), [research.md](research.md), [contracts/](contracts), [quickstart.md](quickstart.md)

**Tests**: Not included — no automated tests were requested and the repository has no test project. Validation is manual via [quickstart.md](quickstart.md).

**Organization**: Tasks are grouped by user story (from spec.md priorities) so each story can be implemented and validated independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: US1–US6 map to the spec's user stories
- All paths are under `ContosoDashboard/` unless noted

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project scaffolding and configuration for the feature

- [x] T001 Create feature folders: `ContosoDashboard/Services/FileStorage/`, `ContosoDashboard/Services/Scanning/`, and `ContosoDashboard/Controllers/`
- [x] T002 Add document configuration to `ContosoDashboard/appsettings.json` and `ContosoDashboard/appsettings.Development.json` (storage root `App_Data/uploads`, max size 25 MB, allowed file-type/extension whitelist, scan queue name)
- [x] T003 Configure upload size limits in `ContosoDashboard/Program.cs` (Blazor SignalR `HubOptions.MaximumReceiveMessageSize` and form/multipart limits to allow 25 MB uploads)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, data access, storage, and async-scan infrastructure that ALL user stories depend on. No user story can start until this phase is complete.

- [x] T004 [P] Add `ScanStatus` (`Pending, Clean, Infected`) and `DocumentAction` (`Upload, Download, Delete, Share`) enums in `ContosoDashboard/Models/DocumentEnums.cs`
- [x] T005 [P] Create `Document` entity in `ContosoDashboard/Models/Document.cs` (int key, Title, Description, Category text, Tags, FileName, FilePath[500], FileType[255], FileSizeBytes, UploadedByUserId, ProjectId?, ScanStatus, dates, navigations)
- [x] T006 [P] Create `DocumentShare` entity in `ContosoDashboard/Models/DocumentShare.cs` (int key, DocumentId, SharedWithUserId, SharedByUserId, SharedDate, navigations)
- [x] T007 [P] Create `DocumentActivity` entity in `ContosoDashboard/Models/DocumentActivity.cs` (int key, DocumentId, UserId, DocumentAction, Timestamp, navigations)
- [x] T008 [P] Add `NotificationType.DocumentShared` value in `ContosoDashboard/Models/Notification.cs`
- [x] T009 Register DbSets, relationships (Restrict/Cascade/SetNull per data-model), and indexes (UploadedByUserId, ProjectId, Category, UploadedDate; unique DocumentShare[DocumentId,SharedWithUserId]; DocumentActivity.DocumentId) in `ContosoDashboard/Data/ApplicationDbContext.cs` (depends on T004–T007)
- [x] T010 [P] Define `IFileStorageService` in `ContosoDashboard/Services/FileStorage/IFileStorageService.cs` per [contracts/IFileStorageService.md](contracts/IFileStorageService.md)
- [x] T011 Implement `LocalFileStorageService` in `ContosoDashboard/Services/FileStorage/LocalFileStorageService.cs` (GUID-based path `{userId}/{project|personal}/{guid}.{ext}` outside wwwroot, bytes-before-metadata ordering, idempotent delete) (depends on T010)
- [x] T012 [P] Define scanning abstractions `IFileScanQueue`, `IFileScanner`, and `ScanResult` in `ContosoDashboard/Services/Scanning/IFileScanQueue.cs` and `IFileScanner.cs`
- [x] T013 [P] Implement `InMemoryFileScanQueue` (`System.Threading.Channels`) in `ContosoDashboard/Services/Scanning/InMemoryFileScanQueue.cs` (depends on T012)
- [x] T014 [P] Implement `StubFileScanner` (whitelist/heuristic, offline) in `ContosoDashboard/Services/Scanning/StubFileScanner.cs` (depends on T012)
- [x] T015 Implement `FileScanBackgroundService : BackgroundService` in `ContosoDashboard/Services/Scanning/FileScanBackgroundService.cs` (dequeue documentId → scan → set ScanStatus Clean/Infected; on Infected delete file + notify uploader + log activity) (depends on T009, T011, T013, T014)
- [x] T016 [P] Define `IDocumentService` and DTOs (`DocumentUploadRequest`, `DocumentQuery`, `DocumentSortField`, `DocumentMetadata`, `DocumentContent`, `DocumentReport`) in `ContosoDashboard/Services/DocumentService.cs` per [contracts/IDocumentService.md](contracts/IDocumentService.md)
- [x] T017 Register services in `ContosoDashboard/Program.cs`: `IFileStorageService`, `IFileScanQueue`, `IFileScanner`, `IDocumentService` (scoped), `FileScanBackgroundService` (hosted), `AddControllers()` + `app.MapControllers()`, bind storage config (depends on T011, T013–T016)

**Checkpoint**: App builds; new tables auto-create on first run (reset LocalDB per quickstart); DI resolves.

---

## Phase 3: User Story 1 - Upload a document with metadata (Priority: P1) 🎯 MVP

**Goal**: Authenticated users upload supported files (≤25 MB) with required metadata; files are stored, attributed, and queued for scanning.

**Independent Test**: Log in as an Employee, upload a PDF with title + category; confirm it is stored, attributed, and listed; oversized/unsupported/missing-metadata uploads are rejected.

- [x] T018 [US1] Implement `UploadDocumentAsync` in `ContosoDashboard/Services/DocumentService.cs` (validate type/size/category; store bytes via `IFileStorageService`; persist `Document` with `ScanStatus=Pending`; log `Upload` activity; enqueue scan via `IFileScanQueue`; project membership check when `ProjectId` set)
- [x] T019 [US1] Create `ContosoDashboard/Pages/DocumentUpload.razor` (`InputFile`, metadata form with required Title/Category, category dropdown, optional description/project/tags, upload progress, success/error messages)
- [x] T020 [US1] Add "Documents" navigation entry in `ContosoDashboard/Shared/NavMenu.razor` and route the upload entry point (`[Authorize]`)

**Checkpoint**: US1 fully functional and demoable on its own (MVP).

---

## Phase 4: User Story 2 - Browse, sort, filter, and search (Priority: P2)

**Goal**: Users find their documents via a list with sort/filter and access-scoped search (only `Clean` documents).

**Independent Test**: With several uploads, open My Documents; sort by date, filter by category/date range, search a term; confirm inaccessible docs never appear.

- [x] T021 [US2] Implement `GetMyDocumentsAsync` (sort by title/date/category/size, filter by category/project/date range; `ScanStatus==Clean` only) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T022 [US2] Implement `SearchDocumentsAsync` (title/description/tags/uploader/project; access-scoped; `Clean` only) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T023 [US2] Implement `GetProjectDocumentsAsync` (project members/manager only) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T024 [US2] Create `ContosoDashboard/Pages/Documents.razor` (My Documents list with columns, sort controls, filters, and search box)

**Checkpoint**: US1 + US2 work independently.

---

## Phase 5: User Story 3 - Download and preview (Priority: P2)

**Goal**: Authorized users download any accessible document and preview PDFs/images inline; retrieval is authorized server-side and gated by scan status.

**Independent Test**: Download a file and preview a PDF/image; as an unauthorized user, a direct `/documents/{id}/download` returns 404; a `Pending` document returns 409.

- [x] T025 [US3] Implement `GetDocumentAsync` and `GetDocumentContentAsync` (access rules per data-model incl. Team-Lead-same-Department; scan gating; log `Download` activity) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T026 [US3] Implement `DocumentsController` in `ContosoDashboard/Controllers/DocumentsController.cs` (`GET /documents/{id}/download` and `/preview`; `[Authorize]`; user id from claims; 200/404/409/415 per [contracts/documents-download-endpoint.md](contracts/documents-download-endpoint.md))
- [x] T027 [US3] Add download and preview actions to `ContosoDashboard/Pages/Documents.razor`

**Checkpoint**: US1–US3 independently functional.

---

## Phase 6: User Story 4 - Manage: edit metadata, replace, delete (Priority: P2)

**Goal**: Uploaders (and PMs/Admins where authorized) edit metadata, replace files, and permanently delete documents.

**Independent Test**: Edit title/category, replace a file (new version served after re-scan), delete with confirmation; unauthorized users are denied.

- [x] T028 [US4] Implement `UpdateMetadataAsync` (uploader / PM of project / Admin) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T029 [US4] Implement `ReplaceFileAsync` (overwrite via `IFileStorageService`, reset `ScanStatus=Pending`, re-enqueue scan) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T030 [US4] Implement `DeleteDocumentAsync` (permanent: cascade shares/activities, delete file, confirm) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T031 [US4] Add edit/replace/delete UI (with delete confirmation) in `ContosoDashboard/Pages/Documents.razor` (and/or a `DocumentDetails` component)

**Checkpoint**: US1–US4 independently functional.

---

## Phase 7: User Story 5 - Share and notifications (Priority: P3)

**Goal**: Owners share documents with specific users; recipients are notified and see a "Shared with Me" section; project members are notified of new project documents.

**Independent Test**: Share a document to another user; recipient gets a notification and finds it under Shared with Me; adding a project document notifies members.

- [x] T032 [US5] Implement `ShareDocumentAsync` (create `DocumentShare`, `NotificationType.DocumentShared` via `INotificationService`, log `Share` activity, prevent duplicates) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T033 [US5] Implement `GetSharedWithMeAsync` in `ContosoDashboard/Services/DocumentService.cs`
- [x] T034 [US5] Create `ContosoDashboard/Pages/SharedWithMe.razor` and a share dialog/action (user picker) wired from the documents list
- [x] T035 [US5] Raise project-member notifications when a document is added to a project (extend the `UploadDocumentAsync` path in `ContosoDashboard/Services/DocumentService.cs`)

**Checkpoint**: US1–US5 independently functional.

---

## Phase 8: User Story 6 - Task and dashboard integration (Priority: P3)

**Goal**: Attach/upload documents from tasks (inherit the task's project); surface Recent Documents and a count on the dashboard.

**Independent Test**: Upload from a task detail page (associates to task's project); dashboard shows last 5 uploads and a count card.

- [x] T036 [US6] Implement `AttachToTaskAsync` (associate a document with a task's project; access checks) in `ContosoDashboard/Services/DocumentService.cs`
- [x] T037 [US6] Implement `GetRecentDocumentsAsync` and `GetDocumentCountAsync` in `ContosoDashboard/Services/DocumentService.cs`
- [x] T038 [US6] Add view/attach/upload documents section to the task detail UI in `ContosoDashboard/Pages/Tasks.razor`
- [x] T039 [US6] Add "Recent Documents" widget and document count summary card to `ContosoDashboard/Pages/Index.razor`
- [x] T040 [US6] Add project documents section to `ContosoDashboard/Pages/ProjectDetails.razor`

**Checkpoint**: All user stories independently functional.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [x] T041 [P] Implement `GetActivityReportAsync` (Admin-only) and an admin report view (most-uploaded types, most-active uploaders, access patterns) — FR-022
- [x] T042 [P] Document the first-run LocalDB reset step (new tables via `EnsureCreated`) in [quickstart.md](quickstart.md) if any detail changed during implementation
- [ ] T043 Execute all [quickstart.md](quickstart.md) validation scenarios: verify offline operation, IDOR returns 404, scan-pending returns 409, and performance targets (SC-006–SC-009)
- [ ] T044 [P] Add production-swap configuration comments in `ContosoDashboard/Program.cs` for `AzureQueueScanQueue`/`AzureBlobStorageService` (documentation only — no cloud dependency added to the training build)

---

## Dependencies & Execution Order

- **Setup (Phase 1)** → **Foundational (Phase 2)** must complete before any user story.
- **User stories** depend only on Foundational, not on each other, and are ordered by priority:
  - US1 (P1) → US2, US3, US4 (P2) → US5, US6 (P3)
  - US3 relies on the scan-gating from Foundational (T015) and US1 uploads to have data, but is independently testable with a seeded/uploaded document.
- **Polish (Phase 9)** runs after the targeted stories are complete.
- Within Foundational: T004–T008 [P] → T009 → (T010→T011), (T012→T013,T014)→T015, T016 → T017.

## Parallel Execution Examples

- **Foundational models/abstractions** (different files): run T004, T005, T006, T007, T008, T010, T012, T016 in parallel; then T009, T011, T013, T014; then T015; then T017.
- **Within a story**, service methods in the same `DocumentService.cs` are sequential (same file); the story's Razor page (different file) can proceed in parallel once its service method exists.
- **Polish**: T041, T042, T044 are parallel; T043 runs last.

## Implementation Strategy

- **MVP**: Complete Phase 1 + Phase 2 + Phase 3 (US1) → deliver upload with metadata and async scanning.
- **Incremental**: Add US2 and US3 (find + retrieve), then US4 (manage), then US5 and US6 (share + integration), then Polish.
- Each user-story phase ends at a checkpoint where the app builds, runs offline, and the story is independently demoable.
