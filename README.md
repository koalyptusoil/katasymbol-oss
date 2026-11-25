# Label Studio

A compiled **C# desktop label editor for Linux, Windows, and macOS**, using
Avalonia. The same source builds on each platform with the .NET 10 SDK.
Published executables bundle the runtime and native rendering libraries, so users
do not need .NET installed to run them.

Editing, layered designs, PNG export, and installed system printers are available
on all three platforms. Direct Bluetooth Classic, Bluetooth LE, and USB HID
connections currently use Linux's BlueZ/hidraw interfaces. On Windows and macOS,
install a compatible printer driver and choose **System printer**.

## Design directly on the label

- Add multiple text objects, artwork, and rounded borders.
- Click to select; drag to move; drag the bottom-right corner to resize.
- Double-click text and type directly on the label. Select text with the mouse
  or keyboard, use Ctrl+A/C/X/V (Command on macOS), and press Enter for another line. Escape or
  Ctrl+Enter (Command+Enter on macOS) finishes editing. The caret and selection follow the printed glyphs.
  The inspector controls fonts, size, alignment, and position in millimetres.
- Arrow keys move selected objects by 0.25 mm; Shift moves by 1 mm. Hold Alt while
  dragging for free movement instead of the 0.25 mm snap.
- The left **Layers** panel shows frontmost objects first. Select a layer,
  rename it, toggle its visibility or lock, and drag its type icon to reorder.
  Arrow buttons also change layer order. Hidden layers are excluded from printing;
  locked layers cannot be moved, resized, deleted, or have their text edited.
- Duplicate, delete, center, and reorder objects. Undo and redo restore edits; a
  continuous session of typing on the label is one undo step.
- Text wraps within its box at the chosen size. Overflow is clipped visibly;
  resize the box or reduce its text size to fit. Text is never silently shrunk.
- For continuous tape, enable **Auto length · fit text** below the dimensions.
  Typing grows or shrinks the label at the current font size, using the longest
  explicit line and leaving 2 mm after the text. Text positions set the left
  margin; full-width borders follow the edges. Artwork and locked or hidden
  layers retain their bounds and can set a minimum length. Height stays fixed.
  Length is rounded up to printer dots within 10–300 mm; a note warns at the
  maximum. Check the preview for wrapping or vertical overflow. Turn the option
  off to keep the current length and enter a fixed length again.
- Label dimensions are constrained to the selected printer's printhead. Reducing
  the label dimensions clamps objects into the printable area.

The editor shapes text with Avalonia and renders with Skia at the selected
printer's resolution. Arabic, Indic scripts, styles, and installed font fallback
use the same layout as caret placement, selection, and automatic length.
The printer receives dots, so it does not need your fonts.

**Preview, PNG export, and printing share the same monochrome raster.** The
preview has display-only optical softening and high-quality scaling to avoid
harsh jagged edges at magnification. **Show printer dots** switches to the raw
pixel view. Smoothing never changes the bytes sent to the printer or the PNG
export. Rulers and selection handles are editor overlays and do not print.

Physical label feed, tape tolerances, printhead condition, and density can still
affect the physical result. System printers depend on their installed driver
respecting the custom media dimensions.

## Connect and print

1. Turn on the printer, load its roll, and disconnect the phone's connection.
2. Choose **Find printer**, then select your device. Serial names such as
   `T0081E…` are normal. Check the model or choose it manually.
3. On Linux, leave the connection on **Automatic**, or choose Bluetooth Classic,
   Bluetooth LE, USB HID, or System printer. Use **Pair** for Classic devices
   when needed. On Windows/macOS, choose **System printer**.
4. Choose **Check status**. This does not print or feed a label.
5. Set dimensions to match the stock and review the canvas. E10's printable
   height is 12 mm even on wider stock. Set density, copies, die-cut stock and
   gap under **Print settings**.
6. Choose **Print label**. Direct jobs wait for printer confirmation. System
   jobs report acceptance by the operating system print queue. Failed partial jobs are never replayed.

54 model profiles have direct drivers. Another 47 APK models use system-driver
routing; there is also a generic system printer profile. See the
[compatibility table](docs/COMPATIBILITY.md) and [protocol notes](docs/PROTOCOL.md).
A listed profile does not certify a physical printer.

Bluetooth Classic defaults to RFCOMM channel 1. You can enter a Bluetooth address,
`/dev/hidrawN`, or `system:queue-name` manually on Linux. Existing `cups:`
addresses still work. BLE uses BlueZ directly and requires
one of the firmware's supported notify/write characteristic pairs. Direct USB is
restricted to vendor ID `1820` and the modern T50/T80/TL8 family. If USB access is
denied, an administrator can install `packaging/70-label-studio.rules` into
`/etc/udev/rules.d/`, reload udev rules, then reconnect the printer.

System printers must already be installed and support custom label sizes.
Linux and macOS submit a physically sized PDF through CUPS (`lp`/`lpstat`), with
image interpolation and automatic scaling disabled. Windows uses the installed
driver with a custom paper size and a raster drawn at its physical dimensions.
The Windows status button checks that the queue is installed; device status is
available in the Windows print queue. Driver margins and media support can affect
physical output.

## Designs and settings

New `.labelstudio` files store editable objects, layer names, visibility and lock
state, automatic-length setting, physical dimensions, model, fonts, and embedded
artwork. Open them from the application. The optional Linux installer also
registers a file association.
Fonts need to be installed on the computer opening the document.

Old `.e10label` designs can be opened. Their original fixed layout is imported
as a single artwork object using the shared renderer; add new editable objects
on top and save as `.labelstudio`. Old files are not overwritten during import.

Printer settings are stored under `e10-studio/settings.json` in the operating
system's application configuration folder: `$XDG_CONFIG_HOME` (or `~/.config`)
on Linux, `%APPDATA%` on Windows, and `~/Library/Application Support` on macOS.

The shared text renderer replaces the earlier Pango renderer. Existing editable
text and imported legacy text may have slightly different font metrics. Review
the preview when reopening older designs, especially when automatic length is on.
Already embedded artwork is unchanged.

Designs remain local. Closing, opening, and starting a new document prompt to
save unsaved work.

## Build and run

Install the .NET 10 SDK, then run these commands from the project root on Linux,
Windows, or macOS. No source changes or platform-specific build scripts are needed.

```sh
dotnet build -c Release
dotnet run --project desktop/LabelStudio -c Release
```

Create a portable executable for the computer you are building on:

```sh
dotnet publish desktop/LabelStudio -c Release -o out/portable
```

To build for another OS or CPU, select its runtime explicitly:

| Platform | Runtime | Publish command |
|---|---|---|
| Linux x64 | `linux-x64` | `dotnet publish desktop/LabelStudio -c Release -r linux-x64 -o out/linux-x64` |
| Windows x64 | `win-x64` | `dotnet publish desktop/LabelStudio -c Release -r win-x64 -o out/win-x64` |
| Windows ARM64 | `win-arm64` | `dotnet publish desktop/LabelStudio -c Release -r win-arm64 -o out/win-arm64` |
| macOS Apple Silicon | `osx-arm64` | `dotnet publish desktop/LabelStudio -c Release -r osx-arm64 -o out/osx-arm64` |
| macOS Intel | `osx-x64` | `dotnet publish desktop/LabelStudio -c Release -r osx-x64 -o out/osx-x64` |

Publish produces `LabelStudio.exe` on Windows and `LabelStudio` on Linux/macOS,
plus a `Licenses` folder to keep beside it when distributing. macOS output is a
portable executable; a signed/notarized `.app` bundle is a separate packaging
step. Linux needs its normal desktop/font libraries; direct Bluetooth needs BlueZ.
macOS system printing uses its built-in CUPS tools. Windows system printing needs
an installed compatible driver.

On Linux, optional per-user desktop installation remains available:

```sh
out/linux-x64/LabelStudio --install
```

It copies the published executable into `~/.local/share/label-studio` (or
`$XDG_DATA_HOME/label-studio`) and registers the menu entry and file association.
`--install` is Linux-only; run the published executable directly on Windows/macOS.

## Validation

```sh
dotnet run --project desktop/LabelStudio.Tests -c Release
```

This runs the protocol/raster checks and the real editor interactions through
Avalonia's headless platform. It checks all 54 direct profiles, golden Android
frames, LZMA, checksums, failure cleanup, multilingual rendering, caret and
selection boundaries, clipping, legacy imports, physical PDF dimensions, layers,
undo/redo, and automatic length during inline typing. It sends no printer jobs.
The published executable also supports `--self-test` without a desktop session.

See [DEVELOPMENT.md](docs/DEVELOPMENT.md) for the code map and change guidelines.

The original E10 protocol implementation previously passed a real connection
and a user-confirmed physical print. The C# driver matches its protocol fixtures;
a fresh physical print with the new build remains to be checked. Other models,
and the new BLE/USB implementations, are not physically certified.

[Third-party licenses](THIRD_PARTY_NOTICES.md)
