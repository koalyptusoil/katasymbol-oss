# Protocol implementation and evidence

The supplied APK was inspected with JADX. The relevant sources are under
`com.fhit.app_iprinter.communication.device` and `.print`.
The model catalog comes from device subclasses, with explicit engine routing in
`desktop/LabelStudio/printers.json`. This is a reimplementation of printer operations,
not a source-level port of the proprietary Android UI.

## Driver families

| APK process | Linux engine | APK implementation |
|---|---|---|
| 15 | e10 | T15Print |
| 16 | e16 | T16Print |
| 2 | g7 | G7Print |
| 4, 27 | g15 | G15Print / Bluetooth variants |
| 17, 19, 30, 33 | modern | T50PlusPrint, T80Print, T80ProPrint, TL8Print |
| 18 | t50-old | T50Print |
| Other processes | cups | Separate device-specific protocols not implemented directly |

The first six routes cover 54 catalog profiles. Remaining profiles require a
separately installed system driver. The generic CUPS route is also usable with
other brands; selecting a model does not install a driver or guarantee that the
printer accepts arbitrary custom stock.

## Common frames and raster

Command frames are 16 bytes, starting `7e 5a 0c 00 10 01 aa`, followed by command,
a little-endian checksum and parameters. Replies start with `7e 5a`, carry a
length, and identify the corresponding command. The reader handles fragmented
and coalesced replies, skips unrelated replies, and checks bounded lengths.

Raster data is column-major. Each byte covers eight vertical dots with black as
one and the top dot in the least significant bit. The raster is centered across
the model's head. Fourteen-byte headers include column count, bytes per column,
page-start/page-end/job-end flags, density, material and margin fields.
Checksums combine header bytes with sampled data at 256-byte boundaries.

* E10/T16 use 4,000-byte buffers, 332 columns, 96-dot heads, material flag `0x10`,
  and initial blank-column trimming with a backfeed command.
* G7/G15 use 4,000-byte buffers, 332 columns and 80-dot heads (96 for E11/E12).
  G7 uses material flag `0x10`; G15 uses `0x40`. Both use 16-dot margin fields.
* Modern drivers use 4,096-byte buffers, capacity derived from head width, material
  flag `0x40`, and a separate density byte. Up to four buffers are compressed
  together, reducing the group until its compressed size fits 4,096 bytes.
* Older T50 drivers use 40,000-byte raster buffers and separate compressed blocks.

The bundled public-domain C# LZMA SDK matches the APK's settings: LZMA-alone,
8 KiB dictionary, lc=3/lp=0/pb=2, 128 fast bytes, definite uncompressed size, no
end-of-stream marker. The encoder is compiled into the executable. A golden
fixture checks that its output matches the original APK's encoder byte for byte.

Bluetooth uploads wrap compressed blocks in 512-byte data frames containing a
checksum, packet index, packet count and up to 500 compressed bytes. E10-family
firmware acknowledges every packet; modern firmware's last packet is followed
by BUF_FULL instead. Classic transfers use 128-byte writes with pacing matching
the APK's E10 E-revision path (50 ms between pieces; modern path 10 ms).

## Connections and session behavior

Classic Bluetooth uses RFCOMM channel 1 by default. BLE uses native GIO D-Bus
calls to BlueZ and the
FEC1/FEC1, FFE1/FFE9, C305/C302 or FF01/FF02 notification/write characteristic
pairs from the APK. Write type and size follow the discovered GATT capabilities.
USB uses `/dev/hidrawN`, 64-byte writes and the
modern-family command envelope; discovery and opening require vendor `1820`.

Jobs query status, reject printer errors or an active job, configure the family,
start printing, wait for buffer space, upload, and wait for completion. A failed
started session attempts one stop command, without replaying label data.
Firmware-update and consumable-provisioning operations are not implemented.

CUPS receives a monochrome PDF with physical stock dimensions. The feed direction
is rotated to map the label height to printer media width. Submission requests a
custom stock size, no scaling, and the requested copy count. Queue acceptance
cannot establish that the hardware printed correctly.

## Validation

Automated checks cover all 54 direct profiles' raster reconstruction and sampled
checksums, page flags, compression and packet formats; simulated sessions cover
setup order and failure handling. The local E10, advertising as `T0081E…`, passed
an actual RFCOMM connection and status query with no reported errors on 2026-10-10.
Its material query returned type 0, 15 mm stock width, 50 mm length and zero gap.
The user subsequently confirmed a working E10 print on this setup. Other direct profiles, BLE and USB are
pending physical validation. Decompiled control flow and incomplete knowledge
of firmware variants remain practical limits.

## References

* [tinythermal-rfcomm](https://github.com/dasarne/tinythermal-rfcomm), MIT,
  revision `9550f54766fd64ae19d4219ad2b82f7d498be447`: frame helpers, E10
  observations and encoder settings.
* [supvan-cups](https://github.com/heeen/supvan-cups), MIT,
  revision `9fd7af09580c3e710202e5471e77e0b88db0e3bb`: independent reference
  for grouped buffers and modern-family USB transport.
* [BlueZ GATT API](https://github.com/bluez/bluez/blob/master/doc/org.bluez.GattCharacteristic.rst).

See [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md) for license notices.

## Shared text rendering

Avalonia resolves installed font families and styles on each operating system.
The same `TextGeometry` layout shapes text, measures the longest explicit line
for automatic length, and supplies caret, selection, and pointer hit testing.
Skia composites text, artwork, and borders at printer resolution, then thresholds
the result to monochrome. Wrapping and clipping preserve the selected font size.
Only the resulting dot data goes to direct printers.

Saved font descriptions retain family and style names, including common suffixes
from older Pango-based designs. Fonts must exist on the computer opening the
file; fallback and metrics can differ across machines. Legacy files are flattened
once through the shared renderer. Their text may differ slightly from the older
Pango output; already embedded images retain their pixels.

Preview, PNG export, and direct-driver encoding use the same raster. Linux/macOS
system printing embeds it into a physically sized PDF with interpolation disabled.
Windows draws the rotated raster at physical size through its installed printer
driver. Native Linux connection code is isolated under `Printing/Linux`; its
framed command/session logic remains independent of the transport.
