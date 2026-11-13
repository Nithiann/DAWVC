# ADR-GUI-001: Desktop UI framework for DAWVC

- **Status:** Proposed — decision deferred pending Windows evidence
- **Date:** 2026-11-13
- **Work Package:** WP3-06
- **Requirements:** GUI plan §4.1, §12; NFR-MNT, NFR-PERF, NFR-REL

## Context

CLI and future Desktop reuse Bootstrap/Application. WP2 provides progress,
cancellation and typed failures. The GUI plan requires comparable Avalonia, WPF
and web-based slices before selecting a framework. Experiments belong in
`spikes/gui`, outside the production solution.

## Options and current evidence

All three prototypes share the same 10,000-row model and real status/scan/doctor
backend path. Avalonia 12.1.6 uses FluentTheme and a virtualized ListBox; WPF uses
built-in controls and recycling virtualization; Photino.Blazor 4.0.13 uses CSS
and Blazor Virtualize. Framework dependencies are scoped to the spike directory.
No commercial control is referenced. See the [results](../gui/WP3_Results.md) and
[predeclared protocol](../gui/WP3_Measurement_Protocol.md) for raw evidence and limits.

Avalonia headless virtualization and the shared session can be tested on Linux.
Windows self-contained cross-publication can be checked from Linux. WPF native
execution, all first-render/memory/UI-latency/DPI/keyboard comparisons, and Photino
native directory drop still need Windows evidence. A native-window CI smoke is
provided but is not equivalent to the interactive performance protocol.

## Decision

**No framework selected yet.** Avalonia remains the design document's provisional
recommendation. Do not turn this ADR into Accepted, add a production Desktop
project, or begin WP4 until the three required slices and knock-outchecks have
been evaluated on the same Windows comparison machine. An unavailable native
runtime here is not evidence that a framework fails its Windows requirements.

## Licensing and distribution

NuGet metadata reports MIT for Avalonia/FluentTheme/headless testing and Apache-2.0
for Photino.Blazor/Photino.NET/Photino.Native. The ANGLE native package includes a
BSD-style license file with binary attribution obligations. Retain native notices
in any distribution and complete the broader runtime/OS dependency review.
Photino's .NET self-contained package still requires WebView2 on Windows. Linux
requires system WebKitGTK; no Linux distribution is being approved here. The
resolved package metadata is recorded, not represented as a complete legal audit.

## Consequences

WP3 remains in progress; WP4 stays blocked on a substantiated framework decision.
The prototypes and separate Windows workflow allow the missing checks to be run
without introducing a production framework dependency. Shared prototype session
coordination is experimental and does not complete WP4 lifecycle requirements.
