# wpressextractor.cslibraries.apps.mytekki.com

A small, cross-platform .NET library for encoding and decoding `.wpress` archives. It mirrors the header and payload layout from the original Python script while adding safe path handling and streaming IO.

## Features

- Works on Windows, macOS, and Linux.
- Supports encoding files/directories and decoding archives.
- Streams data instead of buffering entire files in memory.

## Install

Add the NuGet package:

```
Install-Package wpressextractor.cslibraries.apps.mytekki.com
```

## Repository

[GitHub: Wpressextractor.Cslibraries.Mytekki.Com](https://github.com/xmikedanielsx/Wpressextractor.Cslibraries.Mytekki.Com)

## Usage

### Encode

```csharp
using MyTekki.WpressExtractor;

var files = new[] { "site-backup", "config.php" };
WpressArchive.Encode("backup.wpress", files);
```

### Decode

```csharp
using MyTekki.WpressExtractor;

WpressArchive.Decode("backup.wpress", outputDirectory: "restore");
```

### Progress events

```csharp
using MyTekki.WpressExtractor;

WpressArchive.ExtractionProgress += (_, e) =>
{
	Console.WriteLine($"{e.Percent:0.00}% ({e.BytesRead}/{e.TotalBytes} bytes)");
};

WpressArchive.Decode("backup.wpress", outputDirectory: "restore");
```

## CLI (sample)

The solution includes a small console runner in `WpressExtractor.Cli`:

```
WpressExtractor.Cli -a backup.wpress site-backup
WpressExtractor.Cli -e backup.wpress
WpressExtractor.Cli -e backup.wpress restore --progress-step 10
```

### CLI releases

Tagged releases (e.g. `v1.0.0`) publish self-contained CLI binaries for Windows, Linux, and macOS (x64 + ARM). Check the GitHub Releases page for downloads.

## Notes

- When decoding, file timestamps are preserved.

## Special thanks

Big thanks to **Matt Billenstein** and his repo [wpress-extractor](https://github.com/mattbillenstein/wpress-extractor/tree/main) 🌟—that trailblazing effort helped us crack the format from scratch, and then we sprinkled in our special sauce 🥫🔥 to make it extra silky for .NET devs.
