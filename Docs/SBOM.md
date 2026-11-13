# Software Bill of Materials (SBOM) & Dependency Audit

**Product:** DAWVC (Digital Audio Workstation Version Control)  
**Version:** `0.1.0-preview.3`  
**License:** MIT License  
**Audit Date:** 2026-09-14  
**Audit Status:** Passed (0 High/Critical Vulnerabilities, 100% Permissive Licenses)

---

## 1. Direct NuGet Dependencies

| Package | Version | Purpose | License | Copyleft? |
|---|:---:|---|:---:|:---:|
| **Blake3** | `3.0.2` | Cryptographic content-addressed hashing | MIT | No |
| **Microsoft.Data.Sqlite** | `9.0.2` | Workspace staging index database | MIT | No |
| **Dapper** | `2.1.66` | Lightweight query mapper for SQLite | Apache-2.0 | No |
| **Spectre.Console** | `0.49.1` | Rich terminal rendering & tables | MIT | No |
| **System.CommandLine** | `2.0.0-beta4.22272.1` | Command-line argument parsing | MIT | No |
| **Microsoft.Extensions.DependencyInjection** | `10.0.0-preview.1.25080.5` | IoC container for Clean Architecture | MIT | No |
| **Microsoft.Extensions.FileSystemGlobbing** | `10.0.0-preview.1.25080.5` | File pattern matching and scanning | MIT | No |
| **YamlDotNet** | `18.1.0` | Parsing & generating `dawvc.yaml` | MIT | No |

---

## 2. Transitive Runtime Dependencies

| Package | Version | Direct Parent | License |
|---|:---:|---|:---:|
| `SQLitePCLRaw.core` | `2.1.13` | Microsoft.Data.Sqlite | Apache-2.0 |
| `SQLitePCLRaw.bundle_e_sqlite3` | `2.1.13` | Microsoft.Data.Sqlite | Apache-2.0 |
| `SQLitePCLRaw.provider.dynamic_cdecl` | `2.1.13` | Microsoft.Data.Sqlite | Apache-2.0 |
| `System.Collections.Immutable` | `9.0.2` | System.CommandLine | MIT |

---

## 3. License Compliance & Distribution Policy

- **All runtime packages** are distributed under open, highly permissive licenses (**MIT** and **Apache-2.0**).
- **Zero Copyleft**: No GPL, AGPL, LGPL, or restrictive copyleft libraries are incorporated in DAWVC binaries.
- **Embedded C Native Binaries**:
  - `e_sqlite3` (SQLite engine) is in the public domain.
  - `blake3` native C acceleration is dual-licensed under CC0 and Apache-2.0.
- **Single-File Bundling**:
  All assemblies and required native libraries are compressed into the single `dawvc.exe` binary without external runtime obligations.

## GUI WP3 experiment inventory (13 November 2026)

These dependencies are confined to `spikes/gui/Directory.Packages.props` and the
isolated spike solution. They are not a production Desktop framework selection.

| Dependency | Pinned version | NuGet license metadata | Scope |
| --- | --- | --- | --- |
| Avalonia / Avalonia.Desktop / Avalonia.Themes.Fluent | 12.1.6 | MIT | Avalonia prototype |
| Photino.Blazor | 4.0.13 | Apache-2.0 | Photino prototype |
| Photino.NET (resolved) | 4.0.16 | Apache-2.0 | Native window/dialog wrapper |
| Photino.Native (resolved) | 4.0.22 | Apache-2.0 | Native Photino binaries |
| Avalonia.Headless.XUnit | 12.1.6 | MIT | Prototype tests only |
| xunit.v3 | 3.2.2 | Apache-2.0 | Avalonia 12 test requirement; production tests unchanged |

WPF uses the WindowsDesktop framework; Photino additionally requires the Windows
WebView2 runtime. That runtime is not bundled by .NET self-contained publication.
The resolved [package-license metadata](gui/evidence/package-license-metadata.json)
records all three prototypes' NuGet package graphs. ANGLE's native license is a
file containing BSD-style redistribution/attribution requirements; preserve its
notices in a future distributable. This inventory is not a full native/OS license
or vulnerability audit. No commercial Avalonia controls are referenced. The
historical product audit above is not extended to these experimental dependencies.

## Avalonia Desktop foundation — WP4 (13 November 2026)

The user selected Avalonia for Windows, macOS and Linux. Avalonia,
Avalonia.Desktop, FluentTheme, Fonts.Inter and headless testing are pinned at
12.1.6 in the root package catalog. CommunityToolkit.Mvvm 8.4.0 (MIT) is added.
Inter is bundled to avoid system-font-dependent startup failures; the font is
under SIL OFL 1.1, while the Avalonia.Fonts.Inter wrapper package declares MIT.
xUnit v3 3.2.2 is confined to Desktop/spike tests; existing test projects remain v2.

Desktop uses no commercial controls. The earlier WP3 inventory remains useful,
but this is now an actual Desktop dependency. `src/DawVcs.Desktop/ThirdPartyNotices.md`
records preview dependencies; the complete native/font redistribution notices,
platform installers and final vulnerability/license review remain WP8 work.
