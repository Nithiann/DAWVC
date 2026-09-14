# DAWVC Quick Start Guide

This guide walks you through setting up and using **DAWVC** on a music project from start to finish.

---

## Scenario Overview

We have an FL Studio project named **"Midnight_Drive"**:
```text
C:\Music\Midnight_Drive\
├── Midnight_Drive.flp
└── vocals.wav
```

---

## Step 1: Initialize the Repository (`dawvc init`)

Navigate into your project folder and initialize DAWVC:

```powershell
cd C:\Music\Midnight_Drive
dawvc init
```

**Output:**
```text
✓ Initialized empty DAWVC repository in C:\Music\Midnight_Drive
  Repository ID: 01953cb4-5f40-7e88-b223-1d6837943c21
  Project Name:  Midnight_Drive
  Primary File:  Midnight_Drive.flp
  Branch:        main
```

DAWVC creates a `.dawvc` directory containing the BLAKE3 object store, staging index, and configuration file `dawvc.yaml`.

---

## Step 2: Inspect Dependencies (`dawvc scan`)

Scan your project to see what samples, audio clips, and VST/AU plugins your `.flp` references:

```powershell
dawvc scan
```

**Output:**
```text
Inspecting DAW project: Midnight_Drive.flp (Adapter: FLStudio)
Discovered 2 dependencies:
  [Sample]   vocals.wav (Bundle) -> C:\Music\Midnight_Drive\vocals.wav
  [Plugin]   Serum (Referenced)  -> VST3: Xfer Records: Serum
```

---

## Step 3: Check Workspace State (`dawvc status`)

Check what has changed and what is staged:

```powershell
dawvc status
```

**Output:**
```text
On branch main
Primary Project File:
  [A] Midnight_Drive.flp (ready for commit)

Discovered Untracked Dependencies:
  (use "dawvc add <path>" to stage for bundling)
  [?] vocals.wav (C:\Music\Midnight_Drive\vocals.wav)
```

---

## Step 4: Stage External Samples (`dawvc add`)

Under DAWVC's **hybrid staging model**, the primary `.flp` is automatically tracked, but external audio files must be explicitly staged so you stay in complete control of repository size:

```powershell
dawvc add vocals.wav
```

**Output:**
```text
✓ Staged dependency: vocals.wav (Bundle)
```

---

## Step 5: Record Your First Snapshot (`dawvc commit`)

Create an immutable commit snapshot:

```powershell
dawvc commit -m "feat: initial arrangement and vocal stems"
```

**Output:**
```text
✓ Created commit 4f98d1a (Branch: main)
  Snapshot: 8a1b2c3d...
  Message:  feat: initial arrangement and vocal stems
  Bundled:  2 objects (1 project file, 1 audio sample)
```

You can view the commit history at any time with:

```powershell
dawvc log
```

---

## Step 6: Create an Experimental Branch (`dawvc branch` & `switch`)

Want to try a different drum pattern or VIP mix without touching your stable project?

```powershell
# Create a new branch
dawvc branch club-mix

# Switch workspace to the new branch
dawvc switch club-mix
```

**Output:**
```text
✓ Switched to branch 'club-mix' (HEAD -> 4f98d1a)
```

Work in FL Studio, record changes, and commit on the `club-mix` branch. You can safely switch back to `main` at any time with `dawvc switch main`.

---

## Step 7: Verify Portability & Environment (`dawvc doctor`)

Run `dawvc doctor` to verify whether all audio samples and third-party plugins are present on the current computer:

```powershell
dawvc doctor
```

**Output:**
```text
DAWVC Doctor — Environment Diagnostic Report
Project: Midnight_Drive (Branch: main)

Dependencies Health:
  [✓ OK] vocals.wav (Local hash matches snapshot)
  [✓ OK] Serum (Plugin registered in local VST3 folder)

Status: Healthy. All assets and plugins verified.
```

---

## Step 8: Restore a Clean Snapshot (`dawvc checkout --restore-to`)

Want to export an exact version for a collaborator or archive?

```powershell
dawvc checkout 4f98d1a --restore-to "D:\Collab\Midnight_Drive_V1"
```

**Output:**
```text
✓ Verified 2 candidate artifacts
✓ Restored commit 4f98d1a into clean target directory: D:\Collab\Midnight_Drive_V1
  - Midnight_Drive.flp (byte-identical)
  - vocals.wav (byte-identical)
```
