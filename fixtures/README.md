# Test Fixtures

This directory contains synthetic, royalty-free test fixtures utilized by unit, integration, and end-to-end tests.

## Policy & Guidelines

1. **NO Commercial Content (`DEC-MVP-014`)**:
   DO NOT add licensed sample packs (such as Splice, Vengeance, Cymatics), third-party commercial FL Studio projects, or copyrighted VST presets.
2. **Exclusively Custom or Public Domain Files**:
   All audio (`.wav`, `.flac`) and `.flp` files must be minimal, programmatically generated, or in the public domain.
3. **Zero Personal Workstation Data & No Autosave/Backup Dumps**:
   Fixtures must never contain embedded local user profile paths, personal credentials, or collaborator details. Autosave and backup folders (`Backup/`) must never be checked into the repository.
4. **Git History Notice**:
   If legacy commits contained sensitive local paths or collaborator details in large fixture backups, sanitize git history upstream using `git filter-repo` before publishing publicly.

## Directory Structure

- `flstudio/`: Minimal and synthetic `.flp` projects (e.g., empty, single sample, missing sample, corrupt header).
- `repositories/`: Pre-configured DAWVC repositories for migration and integrity testing.

