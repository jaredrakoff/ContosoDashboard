<!--
SYNC IMPACT REPORT
==================
Version change: (template, unversioned) → 1.0.0
Rationale: Initial ratification. First concrete constitution replacing the
unfilled template placeholders (MAJOR baseline for a new governing document).

Modified principles:
  - [PRINCIPLE_1_NAME] → I. Offline-First with a Cloud Migration Path
  - [PRINCIPLE_2_NAME] → II. Infrastructure Abstraction
  - [PRINCIPLE_3_NAME] → III. Security by Design
  - [PRINCIPLE_4_NAME] → IV. Layered Separation of Concerns
  - [PRINCIPLE_5_NAME] → V. Training Clarity Over Cleverness

Added sections:
  - Security & Compliance Requirements (was [SECTION_2_NAME])
  - Development Workflow & Quality Gates (was [SECTION_3_NAME])

Removed sections: None

Templates requiring updates:
  - .specify/templates/plan-template.md ......... ✅ reviewed (Constitution Check
    gate is generic; no hardcoded principles to reconcile)
  - .specify/templates/spec-template.md ......... ✅ reviewed (no conflicts)
  - .specify/templates/tasks-template.md ........ ✅ reviewed (task categories
    compatible with principle-driven work)
  - .github/prompts/speckit.*.prompt.md ......... ✅ reviewed (generic agent refs)
  - README.md ................................... ✅ aligned (principles derived
    from its "Architecture Principles" section)

Follow-up TODOs: None
-->

# ContosoDashboard Constitution

## Core Principles

### I. Offline-First with a Cloud Migration Path

The application MUST run fully offline on a developer machine with no external
service dependencies, cloud subscriptions, or network access required to build,
run, or exercise any feature. Local implementations (SQL Server LocalDB or
SQLite, local filesystem, cookie-based mock authentication) are the default.
Every offline dependency MUST have a documented production migration path to its
Azure counterpart (Azure SQL Database, Azure Blob Storage, Microsoft Entra ID)
that requires configuration and implementation swaps only — never changes to
business logic.

**Rationale**: The project is a training vehicle for Spec-Driven Development;
learners MUST be able to run it anywhere without cost or connectivity, while
still learning realistic cloud-ready patterns.

### II. Infrastructure Abstraction

All infrastructure dependencies (data access, file storage, authentication,
external services) MUST be consumed through interface abstractions, never by
referencing a concrete provider directly from business or UI code. Swapping a
local implementation for a cloud implementation MUST be achievable via
dependency injection registration alone. New infrastructure concerns MUST ship
with an interface before a concrete implementation is used elsewhere.

**Rationale**: Abstraction keeps the offline-first and cloud-migration
principles enforceable and teaches industry-standard dependency inversion.

### III. Security by Design

Every protected page MUST enforce authentication (`[Authorize]`), and every
service that returns or mutates user-scoped data MUST perform authorization
checks that prevent Insecure Direct Object Reference (IDOR) — UI-level checks
alone are NOT sufficient (defense in depth). Role-based access control MUST be
honored hierarchically (Employee → TeamLead → ProjectManager → Administrator).
Users MUST only ever see data they are authorized to access. Security-relevant
shortcuts introduced for training convenience MUST be explicitly documented as
training-only limitations.

**Rationale**: Security is taught as a first-class, non-optional concern even in
a mock environment, so learners internalize secure defaults.

### IV. Layered Separation of Concerns

Code MUST maintain a clean separation across Models, Services, Data (DbContext),
and Pages/UI layers. UI components MUST NOT contain business rules or direct data
access; business logic MUST live in services; data-shape and persistence concerns
MUST live in models and the data-access layer. Cross-layer leakage (e.g., UI
querying the DbContext directly) is prohibited.

**Rationale**: Consistent layering makes the codebase legible for teaching and
keeps abstraction and security boundaries intact.

### V. Training Clarity Over Cleverness

Code MUST favor readability and explicitness over clever or dense constructs.
Simplifications made for a training context are permitted but MUST be clearly
documented as known limitations rather than presented as production guidance.
Features MUST NOT be over-engineered: implement what the specification requires
and no more (YAGNI). New abstractions are justified only when they serve an
existing, demonstrated need or one of the principles above.

**Rationale**: The primary product of this repository is understanding; clarity
and honesty about limitations serve learners better than sophistication.

## Security & Compliance Requirements

- The mock authentication system is for training ONLY and MUST NOT be presented
  as production-ready. Production guidance MUST direct users to a real identity
  provider with password hashing, MFA, and OAuth 2.0/OpenID Connect.
- Security headers (CSP, X-Frame-Options, X-XSS-Protection, and related) MUST
  remain enabled.
- Authorization MUST be enforced in depth: middleware, page `[Authorize]`
  attributes, AND service-level checks.
- File uploads MUST generate unique file paths (e.g., using a GUID) before
  database insertion to prevent duplicate-key violations and orphaned records.
- No secret, credential, or connection string containing live credentials may be
  committed to the repository.

## Development Workflow & Quality Gates

- Feature work MUST follow the Spec-Driven Development cycle:
  specify → plan → tasks → implement, using the Spec Kit commands.
- Every plan MUST pass the Constitution Check gate before implementation begins,
  and MUST be re-checked after design. Any violation MUST be recorded and
  justified in the plan's Complexity Tracking section or the approach revised.
- Changes MUST preserve the ability to build and run the application offline.
- Documentation (README and stakeholder docs) MUST be kept consistent with the
  behavior actually implemented.

## Governance

This constitution supersedes other development practices for this repository.
Amendments MUST be made by editing this file, accompanied by a Sync Impact Report
and propagation of any consequent changes to dependent templates and guidance
docs. Versioning follows semantic versioning: MAJOR for backward-incompatible
governance or principle removals/redefinitions, MINOR for new or materially
expanded principles/sections, PATCH for clarifications and non-semantic edits.
All specs, plans, and reviews MUST verify compliance with these principles;
deviations MUST be justified against a stated need. Use the README's Architecture
Principles section for complementary runtime development guidance.

**Version**: 1.0.0 | **Ratified**: 2026-07-16 | **Last Amended**: 2026-07-16
