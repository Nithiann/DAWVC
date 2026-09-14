# Contributing to DAWVC

Thank you for your interest in contributing to **DAWVC**! This document outlines the engineering standards, git branching conventions, and quality guidelines required for this project.

---

## 1. General Principles

- **Normative Documentation**: All code must conform to the normative specifications set forth in [Docs/Technical Design.md](Docs/Technical%20Design.md) and [Docs/MVP Requirements.md](Docs/MVP%20Requirements.md).
- **Scope Discipline**: Do not add features outside the approved MVP v0.1 scope without an approved Architecture Decision Record (ADR) or requirement amendment.
- **No Commercial Content**: Test fixtures must NEVER contain copyrighted commercial sample libraries or proprietary VST binaries. Use only custom synthetic or explicitly royalty-free public domain audio assets.

---

## 2. Git & Branching Conventions

- `main` is the protected release branch and must remain releasable at all times.
- Work within short-lived feature branches using descriptive prefixes:
  - `feat/issue-123-opaque-commit`
  - `fix/issue-456-atomic-rename`
  - `test/issue-789-flp-fixtures`
  - `docs/issue-012-adr-update`
- Commit messages must follow [Conventional Commits](https://www.conventionalcommits.org/):
  - `feat(domain): add AggregateContentHash calculation`
  - `fix(infra): prevent partial blob write on process termination`
  - `test(flstudio): add regression test for truncated flp header`
  - `docs(adr): add ADR-OBJ-001 for envelope format`

---

## 3. Definition of Ready & Definition of Done

### Definition of Ready (DoR)
A task may be started when:
1. Linked requirement IDs are identified (`FR-...`, `NFR-...`, `INV-...`).
2. Inputs, outputs, and failure modes are explicitly specified.
3. Required test fixtures are prepared or planned as subtasks.

### Definition of Done (DoD)
A pull request may be merged only when:
1. All unit, integration, and architecture tests pass (`dotnet test`).
2. The build succeeds with 0 errors and 0 warnings (`TreatWarningsAsErrors=true`).
3. Code formatting conforms to `.editorconfig` (`dotnet format --verify-no-changes`).
4. Error handling leverages domain-typed errors (no generic raw exceptions).
5. Any modifications to persisted schemas or architectural boundaries are documented in an ADR.

---

## 4. Development & Testing

```powershell
# Restore and build all projects in Release configuration
dotnet build -c Release

# Run all tests
dotnet test -c Release --logger "console;verbosity=normal"

# Verify code formatting
dotnet format --verify-no-changes
```
