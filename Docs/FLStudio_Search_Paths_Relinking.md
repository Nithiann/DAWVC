# FL Studio Search Paths & Asset Relinking Guide

**Target Audience:** FL Studio producers using DAWVC  
**Requirement Mapping:** `NFR-REL-008`, `R-002`, `FR-FLP-001`, `FR-DOC-001`

---

## 1. How FL Studio Locates Audio Assets

FL Studio resolves referenced audio files (audio clips, sampler channels, Slicex, Edison) in the following order:
1. **The directory of the current `.flp` project file** and its immediate subdirectories.
2. **Recorded / Rendered audio folders** defined in Project Settings.
3. **Extra Search Folders** defined in FL Studio's *File Settings*.
4. **Hardcoded absolute path** that was saved inside the `.flp` binary at the time the file was added.

---

## 2. Why DAWVC Does NOT Modify Native `.flp` Files

A core architectural invariant of DAWVC is that **native project bytes are sacred** (`INV-001`):
- DAWVC **never** mutates, binary-patches, or re-writes your `.flp` project file.
- Re-writing proprietary binary project files risks project corruption, breaking complex automation links, or stripping undocumented plugin state chunks.
- Instead, DAWVC versions your exact `.flp` file unmodified and materializes bundled audio dependencies in your project workspace.

---

---

## 3. Automatic Host Search-Path Discovery (Producer Machine)

When preparing or diagnosing a project on the authoring computer, samples are often scattered across external sample libraries (e.g. `D:\Sounds`, external SSDs, or Splice folders) and are referenced in the `.flp` solely by filename.

DAWVC features **Automatic Host Search-Path Discovery**:
- `FLStudioAdapter` automatically reads all configured **Browser extra search folders** from the Windows Registry:
  - `HKCU\Software\Image-Line\FL Studio *\Search paths` (supports FL Studio 20, 21, 2024, 2025, 2026+)
  - `HKCU\Software\Image-Line\Shared\Paths` (`Shared data` and `Install path`)
- It also scans standard host paths:
  - `%USERPROFILE%\Documents\Image-Line\Data`
  - `%ProgramFiles%\Image-Line\FL Studio *\Data\Patches\Packs`
  - Splice user directories (`%USERPROFILE%\Splice\sounds`)
- **Fast O(1) In-Memory Indexing:** When resolving referenced audio samples, DAWVC builds a lazy index of available audio files across these folders. This enables resolving dozens of external samples across libraries with 50,000+ files in just a few milliseconds.
- This allows `dawvc doctor` and `dawvc scan` to locate and verify assets immediately without requiring manual file copies or path changes by the producer.

---

## 4. Relinking Audio When Moving Between Machines (Collaborator Checkout)

When you checkout a project on a second computer (or restore to a new folder), the absolute paths from the original machine (e.g., `D:\Samples\Kick.wav`) will not match the new machine's paths (e.g., `C:\Audio\Samples\Kick.wav`).

Because DAWVC materializes all bundled dependencies inside your project directory (or managed asset folder), FL Studio can automatically find them **if search paths are configured correctly**.

### Recommended Configuration: Extra Search Folders
To ensure FL Studio automatically finds all materialized assets without prompting you with a *"Missing samples"* dialog:

1. In FL Studio, open **Options** → **File Settings**.
2. Under **Browser extra search folders**, add the root folder where your DAWVC projects and sample libraries reside (for example, `C:\Music\Projects` or your dedicated sample drive).
3. Ensure the folder icon next to the path is green (enabled).

```text
┌─────────────────────────────────────────────────────────────┐
│ FL Studio File Settings: Browser Extra Search Folders        │
├─────────────────────────────────────────────────────────────┤
│  Folder Path                                    Folder Name │
│  C:\Music\Projects\                             [Projects]  │
│  C:\Audio\Samples\                              [Samples]   │
└─────────────────────────────────────────────────────────────┘
```

When FL Studio opens a restored `.flp`, it searches these configured folders recursively and automatically connects the materialized files!

---

## 5. Using `dawvc doctor` to Diagnose Asset Mismatches

If FL Studio warns that a sample or plugin is missing, use `dawvc doctor`:

```powershell
dawvc doctor
```

`dawvc doctor` performs two levels of diagnostic checks:
1. **Repository Health (Artifacts)**:
   - Validates that all files committed to the project snapshot are physically present in the workspace.
   - Verifies that file content matches the cryptographic BLAKE3 content hash.
2. **Host Environment Health (DAW & Plugins)**:
   - Checks whether third-party VST, VST3, or CLAP plugins referenced in the project are installed on the local system.
   - Outputs clear remediation steps for any missing dependencies.

### Example Remediation Output

```text
[!] Missing Dependency:
    Name:     drum_loop_128bpm.wav
    Expected: D:\Samples\Packs\drum_loop_128bpm.wav
    Status:   Not found at original path.
    Action:   Asset is bundled in DAWVC repository. Run:
              dawvc checkout HEAD --force
              and add your project root to FL Studio Extra Search Folders.
```
