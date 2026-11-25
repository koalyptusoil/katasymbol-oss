# Printer compatibility

The supplied APK catalog contains 101 model entries. 54 have direct driver profiles; 47 require an installed system driver. A generic system-printer entry is also available.

**The local E10 passed a Bluetooth status check, and the user confirmed a working print. Other direct profiles have not been tested on physical hardware.** A successful raster test does not establish compatibility with every firmware or roll.

Automatic chooses the listed Bluetooth connection unless the target is a USB path or system queue. Modern-family profiles also permit direct USB HID with Supvan vendor ID `1820`. Any model can use a compatible installed system driver. Direct connections in
this table currently require Linux. Windows and macOS use installed system
printers; selecting a catalog model does not add a driver.

| Model | Default direct connection | Driver path |
|---|---|---|
| A10/A16 | Bluetooth Classic | e16 |
| A50 Plus | Bluetooth Classic | modern; USB HID optional |
| A55 | Bluetooth Classic | t50-old |
| A80 Pro | Bluetooth Classic | modern; USB HID optional |
| A85 | Bluetooth Classic | modern; USB HID optional |
| BP327 | — | Installed system driver required |
| BP337 | — | Installed system driver required |
| BP367 | — | Installed system driver required |
| CH203 | — | Installed system driver required |
| D15T | — | Installed system driver required |
| E10 | Bluetooth Classic | e10 |
| E11 | Bluetooth LE | g15 |
| E12 | Bluetooth LE | g15 |
| E16 | Bluetooth Classic | e16 |
| G10 | — | Installed system driver required |
| G11 | Bluetooth Classic | g15 |
| G11 Pro | Bluetooth Classic | g15 |
| G11M | Bluetooth LE | g15 |
| G11M Plus | Bluetooth LE | g15 |
| G12 Mini | Bluetooth LE | g15 |
| G15 | Bluetooth LE | g15 |
| G15 Max | Bluetooth LE | g15 |
| G15 Mini | Bluetooth LE | g15 |
| G15 Pro | Bluetooth LE | g15 |
| G15M | Bluetooth LE | g15 |
| G15M Pro | Bluetooth LE | g15 |
| G18 | Bluetooth Classic | g15 |
| G18 Pro | Bluetooth Classic | g15 |
| G19 Plus | Bluetooth LE | g15 |
| G19M Plus | Bluetooth LE | g15 |
| G21 Plus | — | Installed system driver required |
| G21 Pro | — | Installed system driver required |
| G25 | — | Installed system driver required |
| G25 Plus | — | Installed system driver required |
| G25M | — | Installed system driver required |
| G25M Plus | — | Installed system driver required |
| G28 | — | Installed system driver required |
| G7 Pro | Bluetooth Classic | g7 |
| G7M | Bluetooth Classic | g7 |
| G7M Pro | Bluetooth Classic | g7 |
| H618GR | — | Installed system driver required |
| HP220/CH203 | — | Installed system driver required |
| LP5125 | — | Installed system driver required |
| LP6125E | — | Installed system driver required |
| LP6245C | — | Installed system driver required |
| LP6245E | — | Installed system driver required |
| M50 | — | Installed system driver required |
| M50 Pro | — | Installed system driver required |
| M80 | — | Installed system driver required |
| M80 Pro | — | Installed system driver required |
| MMP50 | — | Installed system driver required |
| MP50 Max | — | Installed system driver required |
| MP80 | — | Installed system driver required |
| MP80 Plus | — | Installed system driver required |
| MT200 | — | Installed system driver required |
| P18 | — | Installed system driver required |
| P50 | — | Installed system driver required |
| P56 | — | Installed system driver required |
| P60 | — | Installed system driver required |
| P66 | — | Installed system driver required |
| Q11 Pro | Bluetooth Classic | g15 |
| Q15 | Bluetooth LE | g15 |
| Q15 Mini | Bluetooth LE | g15 |
| Q15 Pro | Bluetooth LE | g15 |
| Q18 | Bluetooth Classic | g15 |
| Q18 Pro | Bluetooth Classic | g15 |
| Q19 Plus | Bluetooth LE | g15 |
| Q25 | — | Installed system driver required |
| Q25 Plus | — | Installed system driver required |
| Q7 | Bluetooth Classic | g7 |
| Q7 Pro | Bluetooth Classic | g7 |
| T10/T16 | Bluetooth Classic | e16 |
| T200M | — | Installed system driver required |
| T50 Max | Bluetooth Classic | modern; USB HID optional |
| T50 Plus | Bluetooth Classic | modern; USB HID optional |
| T50 Plus+ | Bluetooth Classic | modern; USB HID optional |
| T50A | Bluetooth Classic | t50-old |
| T50M | Bluetooth Classic | t50-old |
| T50M Plus | Bluetooth Classic | modern; USB HID optional |
| T50M Pro | Bluetooth Classic | modern; USB HID optional |
| T50S | Bluetooth Classic | modern; USB HID optional |
| T80 Max | Bluetooth Classic | modern; USB HID optional |
| T80 Pro | Bluetooth Classic | modern; USB HID optional |
| T80 Pro+ | Bluetooth Classic | modern; USB HID optional |
| T80M | Bluetooth Classic | modern; USB HID optional |
| T80M Pro | Bluetooth Classic | modern; USB HID optional |
| T80S | Bluetooth Classic | modern; USB HID optional |
| TL8 | Bluetooth LE | modern; USB HID optional |
| TL8M | Bluetooth Classic | modern; USB HID optional |
| TP20 | — | Installed system driver required |
| TP20M | — | Installed system driver required |
| TP50 | — | Installed system driver required |
| TP56 | — | Installed system driver required |
| TP70E | — | Installed system driver required |
| TP76E | — | Installed system driver required |
| TP80 | — | Installed system driver required |
| TP80A | — | Installed system driver required |
| TP86E | — | Installed system driver required |
| TP96 | — | Installed system driver required |
| XG217 | — | Installed system driver required |
| 小七 | Bluetooth Classic | g7 |
| Other printer (system driver) | — | Installed system driver required |

Unsupported direct protocols are deliberately routed to system printing. For models without a compatible system driver, catalog selection alone cannot enable printing. Additional native families require their own raster/transport implementation and hardware validation.

See [README](../README.md) for setup and [PROTOCOL](PROTOCOL.md) for evidence and limitations.
