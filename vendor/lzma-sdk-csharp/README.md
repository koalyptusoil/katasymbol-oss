Public-domain C# LZMA encoder/decoder by Igor Pavlov, from the official LZMA SDK 23.01:
https://www.7-zip.org/a/lzma2301.7z

The original SDK sources are unchanged. LzmaSdk.csproj builds the library for the
desktop app. See lzma-sdk.txt for the upstream documentation and license statement.
The settings in Protocol.Compress match the existing APK Java encoder, including
the definite uncompressed length and disabled end marker.
