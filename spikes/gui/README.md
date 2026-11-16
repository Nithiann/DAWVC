# WP3 framework comparison (experimental)

These prototypes are isolated from `DawVcs.slnx` and do not select or install a
production Desktop framework. The three hosts share `Shared/SpikeSession.cs`:
Bootstrap/Application in process, worker-thread backend execution, fresh provider
per operation, sampled typed progress, cancellation that waits for completion,
and 10,000 deterministic synthetic asset rows. No CLI output is parsed.

Use a **disposable repository**. Status/doctor may write metadata and the existing
WP5-08 SQLite recovery concern remains. No staging, commit, checkout or remote
mutation UI is offered. Error details and raw machine paths are not exported.

```powershell
dotnet build spikes/gui/GuiSpikes.slnx -c Release
dotnet test spikes/gui/Tests/Tests.csproj -c Release
dotnet run --project spikes/gui/Harness -c Release -- --fixture <new-directory>
dotnet run --project spikes/gui/Harness -c Release -- --backend <fixture-directory>
dotnet run --project spikes/gui/Harness -c Release -- --model-benchmark
dotnet run --project spikes/gui/Avalonia -c Release
dotnet run --project spikes/gui/Wpf -c Release
dotnet run --project spikes/gui/Photino -c Release
./spikes/gui/publish.ps1
```

`--large-fixture <new-directory>` also creates a real 256 MiB synthetic asset.
`--cancel-status <large-fixture>` measures ten real status-hashing cancellations
with the request timestamp recorded before token cancellation. This is backend
evidence, not UI cancel latency. It only accepts the marked synthetic fixture.
The fixture generator links the existing synthetic FLP builder, not a commercial
project. Fixture creation refuses an existing destination.

WPF only runs on Windows. Photino requires WebView2 on Windows (self-contained
.NET does not bundle that runtime), WebKitGTK 4.1 on Linux, and a native display.
Avalonia native runs also require a display; its headless tests need none. Tests
use Avalonia 12's xUnit v3 requirement in this isolated solution; production tests
continue to use the existing xUnit v2 packages. No commercial controls are used.

Avalonia/WPF implement native directory drop. Photino has a native folderpicker
and typed path entry; native directory drag/drop is still an unresolved comparison
item and must not be marked as passed. WPF dark styles are intentionally minimal;
all three still require native Windows keyboard, focus, DPI and contrast checks.

`publish.ps1` publishes twice into fresh unique directories and compares all
non-PDB file hashes. A separate manual/PR Windows workflow builds/tests/publishes
artifacts; it does not approve, merge or release them. Cross-published Windows
binaries from Linux do not prove Windows startup or interaction quality.

See [protocol](../../Docs/gui/WP3_Measurement_Protocol.md),
[results](../../Docs/gui/WP3_Results.md) and
[proposed ADR](../../Docs/adr/ADR-GUI-001-desktop-framework.md). Missing Windows
measurements keep WP3 and the ADR open; do not start WP4 on an assumed decision.
