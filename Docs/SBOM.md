# Software Bill of Materials (SBOM) & Dependency Audit

**Product:** DAWVC (Digital Audio Workstation Version Control)  
**Version:** `0.1.0-preview.1`  
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
