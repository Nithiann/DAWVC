# Installation & Setup Guide

**Target Platform:** Windows 11 x64  
**Runtime Requirement:** None (Self-contained single executable)  
**License:** MIT

---

## 1. System Requirements

- **Operating System:** Windows 11 x64 (or Windows 10 build 19041+)
- **Architecture:** x64
- **Runtime:** Completely self-contained. **No** .NET Runtime, SDK, or C++ redistributable installation is required.
- **DAW Support:** FL Studio 2026.x baseline (with backward compatibility and opaque fallback for unknown versions).

---

## 2. Installation Steps

### Step 1: Download the Release Archive
Download the latest distribution archive and checksum file from the [GitHub Releases](https://github.com/Nithiann/DAWVC/releases) page:
- `dawvc-v0.1.0-preview.3-win-x64.zip`
- `SHA256SUMS.txt`

### Step 2: Verify Cryptographic Checksum
Open PowerShell and verify the archive's integrity against the published SHA-256 hash:

```powershell
$expectedHash = (Get-Content SHA256SUMS.txt).Split(' ')[0]
$actualHash = (Get-FileHash dawvc-v0.1.0-preview.3-win-x64.zip -Algorithm SHA256).Hash.ToLowerInvariant()

if ($actualHash -eq $expectedHash) {
    Write-Host "Verification successful: SHA-256 matches!" -ForegroundColor Green
} else {
    Write-Error "Verification FAILED: Hash mismatch! Expected $expectedHash but got $actualHash"
}
```

### Step 3: Extract and Place Binary
Extract the contents to a permanent location, such as `C:\Tools\dawvc` or `%LOCALAPPDATA%\Programs\dawvc`:

```powershell
Expand-Archive -Path dawvc-v0.1.0-preview.3-win-x64.zip -DestinationPath "C:\Tools\dawvc"
```

The directory will contain:
- `dawvc.exe` (Self-contained executable)
- `README.md`
- `LICENSE`

### Step 4: Add to User PATH
To run `dawvc` from any terminal, add its directory to your User `PATH`:

```powershell
[Environment]::SetEnvironmentVariable(
    "Path",
    [Environment]::GetEnvironmentVariable("Path", [EnvironmentVariableTarget]::User) + ";C:\Tools\dawvc",
    [EnvironmentVariableTarget]::User
)
```

Restart your terminal (PowerShell, Windows Terminal, or CMD).

### Step 5: Verify Installation
Verify that DAWVC runs properly:

```powershell
dawvc --version
# Output: 0.1.0-preview.3

dawvc --help
```

---

## 3. Upgrading DAWVC

To upgrade to a new preview version:
1. Download the new release ZIP.
2. Extract the new `dawvc.exe` and overwrite the previous executable in `C:\Tools\dawvc`.
3. Verify repository compatibility by running `dawvc fsck` in any existing repository.

---

## 4. Uninstallation

Because DAWVC is distributed as a clean, portable single executable with no system registry hooks or background daemons:
1. Remove `C:\Tools\dawvc` (or your custom directory).
2. Remove the directory from your User `PATH` environment variable.
3. Repositories tracked with DAWVC keep their internal metadata inside `.dawvc/` within their project folders. To remove version control from a project without deleting music files, simply delete the `.dawvc` folder in that project.

## Avalonia desktop development preview (WP4)

The Desktop foundation targets Windows, macOS and Linux. It provides a shell,
existing-repository selection, local recent projects and theme switching. WP5 now adds repository views and diagnostics; project/staging/ref mutators
remain outside this preview.

With the .NET 10 SDK:

```sh
dotnet run --project src/DawVcs.Desktop -c Release
```

Self-contained publication (choose `win-x64`, `linux-x64`, `osx-arm64` or `osx-x64`):

```sh
dotnet publish src/DawVcs.Desktop -c Release -r linux-x64 --self-contained true -p:PublishTrimmed=false -p:PublishSingleFile=false -o artifacts/desktop/linux-x64
```

Run `DawVcs.Desktop.exe` on Windows or `./DawVcs.Desktop` on Linux/macOS from the
published directory. Inter is bundled. Linux still needs an X11/compatible display
and the native libraries required by Avalonia/Skia, such as fontconfig. Self-contained
.NET does not eliminate OS library requirements. macOS `.app` bundles, signing,
notarization, installers and native accessibility/DPI validation remain WP8 work.

Recent paths are stored under the current user's local application-data directory
in `DAWVC/recent-repositories.json`. Appearance changes currently apply for the
running session. Opening a repository validates its configuration without loading
the staging index; context construction can still create object temporary directories.
No machine paths are added to shared repository configuration.

## WP5 viewer preview

The same Avalonia launch/publication commands now provide overview/status, grouped
changes, paged first-parent history and snapshot details, paged snapshot dependencies,
and scan/doctor/fsck. Diagnostic summary export is JSON with allowlisted counts only;
choose a location outside the repository. Viewer operation does not change project
content, staging, branches or refs, but status may update the local hash cache.

Windows preview publication is cross-built; native Windows/macOS release validation,
installers/signing and physical-desktop accessibility/performance remain WP8 gates.
See [WP5 validation](gui/WP5_Validation.md) for the actually executed checks and screenshots.
