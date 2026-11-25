# Third-party notices

USB HID framing, modern raster grouping and buffer layout also draw on
[supvan-cups](https://github.com/heeen/supvan-cups), revision
`9fd7af09580c3e710202e5471e77e0b88db0e3bb`, under the MIT license below.
Copyright (c) 2026 Florian Hänel.

The command framing, LZMA settings and chunk layout in
`desktop/LabelStudio/Protocol.cs` draw on
[tinythermal-rfcomm](https://github.com/dasarne/tinythermal-rfcomm), revision
`9550f54766fd64ae19d4219ad2b82f7d498be447`, under the MIT license below.
The app implements the E10/T15 command sequence inspected in the supplied APK.

MIT License

Copyright (c) 2026 tinythermal-rfcomm contributors
Copyright (c) 2026 Florian Hänel

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## Compiled C# desktop application

The desktop app uses Avalonia 11.3.22 (MIT), SkiaSharp 2.88.9 (MIT), Skia (BSD),
HarfBuzzSharp/HarfBuzz (MIT), Tmds.DBus.Protocol (MIT),
System.Drawing.Common 10.0.6 (MIT, Windows system printing), and the .NET 10 runtime
(MIT and associated third-party notices). Package license files and .NET's
ThirdPartyNotices are distributed beside the executable under `Licenses/`.

The C# LZMA SDK in `vendor/lzma-sdk-csharp` is by Igor Pavlov and is in the public
domain. It was obtained from the official LZMA SDK 23.01 archive:
https://www.7-zip.org/a/lzma2301.7z
The original sources and upstream documentation/license declaration are retained.
The app compiles this encoder into its executable.
