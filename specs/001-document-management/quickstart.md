# Quickstart: Validate Document Upload and Management

A run/validation guide proving the feature works end-to-end. Implementation details
live in [plan.md](plan.md), [data-model.md](data-model.md), and
[contracts/](contracts/); acceptance criteria are in [spec.md](spec.md).

## Prerequisites

- .NET 8 SDK (or newer SDK able to build `net8.0`) and the ASP.NET Core 8 runtime
- SQL Server LocalDB (`MSSQLLocalDB`)
- The app builds and runs today: `dotnet run` from `ContosoDashboard/`

## Setup

```powershell
cd ContosoDashboard
dotnet run
```

Open the shown URL (typically `http://localhost:5000`). The database is
auto-created/seeded (`EnsureCreated`). Log in by selecting a seeded user (no
password): `ni.kang@contoso.com` (Employee), `floris.kregel@contoso.com`
(Team Lead, Engineering), `camille.nicole@contoso.com` (Project Manager),
`admin@contoso.com` (Administrator).

> First run after adding entities: because the app uses `EnsureCreated` (not
> migrations), delete the existing `ContosoDashboard` LocalDB database once so the
> new document tables are created:
> `sqllocaldb stop MSSQLLocalDB; sqllocaldb start MSSQLLocalDB` then drop the DB via
> your SQL tool, or use a fresh database name in the connection string.

## Validation scenarios (map to spec user stories)

### US1 — Upload with metadata (P1)
1. Log in as Ni Kang → open **Documents** → **Upload**.
2. Choose a PDF ≤25 MB, set Title + Category (e.g., "Reports"), submit.
3. Expected: success message; document appears in **My Documents** with uploader,
   upload date, size, and type recorded.
4. Try a >25 MB file and an unsupported type (e.g., `.exe`) → each is rejected with a
   clear message. Submit with no title/category → blocked.

### US2 — Browse, sort, filter, search (P2)
1. Upload a few documents across categories/projects.
2. In **My Documents**, sort by upload date and filter by category and date range.
3. Search a term matching a title/tag/uploader/project → matching accessible docs
   returned; docs you cannot access never appear.

### US3 — Download & preview (P2)
1. From a document, choose **Download** → file downloads intact.
2. For a PDF/image, choose **Preview** → renders inline in the browser.
3. As a different user with no access, hit `/documents/{id}/download` directly →
   `404` (IDOR-safe).

### US4 — Manage: edit, replace, delete (P2)
1. As the uploader, edit Title/Category → changes reflected in list and search.
2. Replace the file → subsequent download returns the new version.
3. Delete with confirmation → document and its file are permanently removed.
4. As an unrelated user, attempt edit/delete → denied.

### US5 — Share & notifications (P3)
1. As uploader, share a document with Floris Kregel.
2. Log in as Floris → in-app notification received; document appears under
   **Shared with Me** and can be downloaded.
3. Add a document to a project → project members receive a notification.

### US6 — Task & dashboard integration (P3)
1. From a task detail page, upload/attach a document → it associates with the task's
   project.
2. Open the dashboard → **Recent Documents** shows your last 5 uploads and a summary
   card shows the document count.

## Cross-cutting checks (non-functional)

- **Security/IDOR**: every download/preview enforces authorization server-side;
  guessing another user's document id returns `404`.
- **Performance targets**: list ≤2 s for up to 500 docs; search ≤2 s; preview ≤3 s;
  upload ≤30 s for 25 MB (SC-006–SC-009).
- **Offline**: no network/cloud calls; files stored under `App_Data/uploads`.
- **Audit**: uploads/downloads/deletes/shares logged; Admin can view the activity
  report.

## Done when

All six user stories pass their scenarios, unauthorized access returns `404`, and
the app still builds and runs fully offline.
