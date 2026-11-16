# ADR-GUI-001: Desktop UI framework for DAWVC

- **Status:** Accepted — explicit user decision, 13 November 2026
- **Date:** 2026-11-13
- **Work Package:** WP3-06
- **Requirements:** GUI plan §4.1, §12; NFR-MNT, NFR-PERF, NFR-REL

## Context

CLI and future Desktop reuse Bootstrap/Application. WP2 provides progress,
cancellation and typed failures. The original GUI plan requested comparable Avalonia, WPF
and web-based slices before selecting a framework. On 13 November the user made
Windows, macOS and Linux a requirement and explicitly selected Avalonia, authorizing
WP4 to proceed. That instruction replaces the comparative selection gate; it does
not certify unperformed platform tests. Experiments belong in
`spikes/gui`, outside the production solution.

## Options and current evidence

All three prototypes share the same 10,000-row model and real status/scan/doctor
backend path. Avalonia 12.1.6 uses FluentTheme and a virtualized ListBox; WPF uses
built-in controls and recycling virtualization; Photino.Blazor 4.0.13 uses CSS
and Blazor Virtualize. The original experiment dependencies were scoped to the spike directory; Avalonia
and CommunityToolkit.Mvvm are now centrally managed production Desktop dependencies.
No commercial control is referenced. See the [results](../gui/WP3_Results.md) and
[predeclared protocol](../gui/WP3_Measurement_Protocol.md) for raw evidence and limits.

Avalonia headless virtualization and the shared session can be tested on Linux.
Windows self-contained cross-publication can be checked from Linux. WPF native
execution, all first-render/memory/UI-latency/DPI/keyboard comparisons, and Photino
native directory drop still need Windows evidence. A native-window CI smoke is
provided but is not equivalent to the interactive performance protocol.

## Decision

**Select Avalonia 12.1.6**, FluentTheme, bundled Inter and CommunityToolkit.Mvvm
8.4.0 for a .NET 10 desktop targeting Windows, macOS and Linux. Reuse the shared
Bootstrap/Application engine; never call CLI or Infrastructure from Desktop.
Keep the existing prototype evidence as supporting evidence, not as a completed
cross-framework benchmark. WPF is excluded by the cross-platform requirement;
Photino remains an archived alternative with unresolved native/origin review.

This is the user's product/architecture decision, not PR approval or permission to
merge. WP4 may proceed. Native Windows/macOS/Linux performance, DPI, keyboard,
accessibility, install and signing validation remain explicit WP8 release gates.

## Licensing and distribution

NuGet metadata reports MIT for Avalonia/FluentTheme/headless testing and Apache-2.0
for Photino.Blazor/Photino.NET/Photino.Native. The ANGLE native package includes a
BSD-style license file with binary attribution obligations. Retain native notices
in any distribution and complete the broader runtime/OS dependency review.
Photino's .NET self-contained package still requires WebView2 on Windows. Linux
requires system WebKitGTK; no Linux distribution is being approved here. The
resolved package metadata is recorded, not represented as a complete legal audit.

## Consequences

WP3 selection is closed by the explicit scope amendment; its missing comparative
measurements are not marked passed. WP4 adds the real Desktop shell and tests.
The CLI retains its existing provider lifetimes. Desktop creates fresh Bootstrap
providers per operation so adapter/search-path caches cannot survive rescans or
repository changes. Contexts remain use-case-owned and are disposed before an
operation is reported complete. No commercial controls are introduced.
