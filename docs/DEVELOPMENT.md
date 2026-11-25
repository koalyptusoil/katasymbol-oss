# Development

The solution targets .NET 10. Run `dotnet build -c Release` from the root and
`dotnet run --project desktop/LabelStudio.Tests -c Release` for all automated
checks. The checks use a headless renderer, send no print jobs, and save editor
screenshots under `out/`. Printer settings use the normal per-user configuration
path; the checks do not save settings.

## Code map

| Area | Files | Responsibility |
|---|---|---|
| Startup | `Program.cs`, `AppPaths.cs`, `Installer.cs` | Application lifetime, native configuration paths, optional Linux installation |
| Documents | `Document.cs`, `PrinterCatalog.cs` | Editable objects, validation, automatic length, file roundtrips, model identification |
| Rendering | `Rasterizer.cs`, `Raster.cs`, `TextGeometry.cs`, `LabelFonts.cs` | Composition, printer dots, preview/export, shared text metrics and font resolution |
| Window | `MainWindow.cs`, `MainWindow.Layout.cs`, `MainWindow.Events.cs` | State, startup overview, layout composition, event registration |
| Editing | `MainWindow.Editing.cs`, `MainWindow.Inspector.cs`, `MainWindow.Layers.cs`, `LabelCanvas*.cs` | History, object properties, layers, pointer input and inline typing |
| Files/printers UI | `MainWindow.Documents.cs`, `MainWindow.Printing.cs` | File pickers, unsaved-change prompts, background printer work and settings |
| Printing | `Protocol.cs`, `Printing/` | Firmware encoding, framed sessions, status, platform routing and system printing |
| Linux drivers | `Printing/Linux/` | BlueZ discovery, RFCOMM, GIO BLE and hidraw interop |
| Checks | `SelfTests.cs`, `LabelStudio.Tests/Program.cs` | Firmware fixtures, document/rendering regressions and actual editor interactions |

`vendor/lzma-sdk-csharp` contains upstream public-domain encoder sources. Keep
project-specific changes outside it. `.editorconfig` defines formatting; use
`dotnet format LabelStudio.slnx --include desktop/` when editing application code.

## Behavior to preserve

- Document coordinates are millimetres. Text geometry and raster pixels use
  printer dots. Convert only at the canvas or printer boundary.
- Preview smoothing is display-only. PNG export and protocol encoding consume
  the original monochrome raster.
- Text drawing, caret placement and automatic length must use the same layout
  engine and fonts. Check clipping, Unicode and undo after editing these paths.
- Capture an independent document/raster before background printing. Never
  touch UI controls from a printer worker or replay a partial print job.
- Keep native platform dependencies behind an OS check. New direct transports
  should implement `IWire` and reuse protocol/session logic where appropriate.
- Keep saved document fields and older `cups:` printer addresses readable.
  Review older designs after font changes because layout metrics can change.

A GitHub Actions workflow under `.github/workflows/desktop.yml` builds, runs the
same checks and publishes on Linux, Windows and macOS when pushed to GitHub.

## Platform checks

Build and run the headless checks on the target OS before claiming native
runtime support. Cross-publishing verifies compilation and packaging, not printer
hardware or OS behavior. Test Windows custom media and driver margins with a
physical label. Test macOS queues through CUPS. Direct BLE/USB/RFCOMM remain
Linux-only until separate native backends and hardware checks are implemented.

See the README for portable publish commands. Release signing, notarization,
installer bundles and new printer hardware certification are separate steps.
