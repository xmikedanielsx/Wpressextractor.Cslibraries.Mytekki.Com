using System.Buffers;
using System.Globalization;

namespace MyTekki.WpressExtractor;

/// <summary>
/// Provides helpers for encoding and decoding WPress archives.
/// </summary>
public static class WpressArchive
{
    /// <summary>
    /// Raised during extraction whenever another 0.01% of the archive has been processed.
    /// </summary>
    public static event EventHandler<ExtractionProgressEventArgs>? ExtractionProgress;

    /// <summary>
    /// The fixed byte length reserved for the filename in the header.
    /// </summary>
    public const int FilenameSize = 255;
    /// <summary>
    /// The fixed byte length reserved for the file content size in the header.
    /// </summary>
    public const int ContentSize = 14;
    /// <summary>
    /// The fixed byte length reserved for the file modification time in the header.
    /// </summary>
    public const int MtimeSize = 12;
    /// <summary>
    /// The fixed byte length reserved for the file prefix in the header.
    /// </summary>
    public const int PrefixSize = 4096;
    /// <summary>
    /// The total header size in bytes for each archive entry.
    /// </summary>
    public const int HeaderSize = FilenameSize + ContentSize + MtimeSize + PrefixSize;

    private static readonly byte[] EofBlock = new byte[HeaderSize];

    /// <summary>
    /// Decodes an archive file to disk.
    /// </summary>
    /// <param name="archivePath">Path to the .wpress archive.</param>
    /// <param name="outputDirectory">Optional destination directory for extracted files.</param>
    public static void Decode(string archivePath, string? outputDirectory = null)
    {
        ThrowIfNullOrWhiteSpace(archivePath, nameof(archivePath));
        using var stream = File.OpenRead(archivePath);
        Decode(stream, outputDirectory);
    }

    /// <summary>
    /// Decodes an archive stream to disk.
    /// </summary>
    /// <param name="archiveStream">Readable stream containing the archive.</param>
    /// <param name="outputDirectory">Optional destination directory for extracted files.</param>
    public static void Decode(Stream archiveStream, string? outputDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        var outputRoot = outputDirectory is null
            ? null
            : Path.GetFullPath(outputDirectory);

        var totalBytes = archiveStream.CanSeek
            ? archiveStream.Length - archiveStream.Position
            : (long?)null;
        var bytesRead = 0L;
        var nextTick = 0;

        void ReportProgress(long delta)
        {
            bytesRead += delta;

            if (totalBytes is null || totalBytes.Value <= 0)
            {
                return;
            }

            var percent = Math.Min(100.0, bytesRead * 100.0 / totalBytes.Value);
            var tick = (int)Math.Min(10000, Math.Floor(percent * 100.0));
            while (tick >= nextTick)
            {
                var reported = Math.Min(100.0, nextTick / 100.0);
                RaiseExtractionProgress(reported, bytesRead, totalBytes.Value);
                nextTick += 1;
            }
        }

        if (outputRoot is not null)
        {
            Directory.CreateDirectory(outputRoot);
        }

        var header = new byte[HeaderSize];

        while (true)
        {
            ReadExactly(archiveStream, header, 0, header.Length, ReportProgress);

            if (IsEofBlock(header))
            {
                break;
            }

            var entry = DecodeHeader(header);
            var targetPath = ResolveOutputPath(entry.Path, outputRoot);

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                TrySetUnixPermissions(directory, isDirectory: true);
            }

            using (var output = File.Create(targetPath))
            {
                CopyExactly(archiveStream, output, entry.Size, ReportProgress);
            }

            TrySetUnixPermissions(targetPath, isDirectory: false);
            File.SetLastWriteTimeUtc(targetPath, DateTimeOffset.FromUnixTimeSeconds(entry.Mtime).UtcDateTime);
        }
    }

    /// <summary>
    /// Encodes files or directories into an archive file.
    /// </summary>
    /// <param name="archivePath">Path to write the archive to.</param>
    /// <param name="inputPaths">Files or directories to include.</param>
    /// <param name="baseDirectory">Optional base directory for relative paths.</param>
    public static void Encode(string archivePath, IEnumerable<string> inputPaths, string? baseDirectory = null)
    {
        ThrowIfNullOrWhiteSpace(archivePath, nameof(archivePath));
        ArgumentNullException.ThrowIfNull(inputPaths);

        using var stream = File.Create(archivePath);
        Encode(stream, inputPaths, baseDirectory);
    }

    /// <summary>
    /// Encodes files or directories into an archive stream.
    /// </summary>
    /// <param name="archiveStream">Writable stream to receive the archive.</param>
    /// <param name="inputPaths">Files or directories to include.</param>
    /// <param name="baseDirectory">Optional base directory for relative paths.</param>
    public static void Encode(Stream archiveStream, IEnumerable<string> inputPaths, string? baseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ArgumentNullException.ThrowIfNull(inputPaths);

        string? baseDirFull = baseDirectory is null ? null : Path.GetFullPath(baseDirectory);

        foreach (var filePath in ExpandInputPaths(inputPaths))
        {
            var archivePath = GetArchivePath(filePath, baseDirFull);
            var header = EncodeHeader(filePath, archivePath);
            archiveStream.Write(header, 0, header.Length);

            using var input = File.OpenRead(filePath);
            input.CopyTo(archiveStream);
        }

        archiveStream.Write(EofBlock, 0, EofBlock.Length);
    }

    private static IEnumerable<string> ExpandInputPaths(IEnumerable<string> inputPaths)
    {
        foreach (var path in inputPaths)
        {
            if (File.Exists(path))
            {
                yield return Path.GetFullPath(path);
                continue;
            }

            if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    yield return Path.GetFullPath(file);
                }

                continue;
            }

            throw new FileNotFoundException($"Input path '{path}' was not found.", path);
        }
    }

    private static string GetArchivePath(string filePath, string? baseDirectory)
    {
        var normalized = baseDirectory is null
            ? filePath
            : Path.GetRelativePath(baseDirectory, filePath);

        return NormalizeArchivePath(normalized);
    }

    private static byte[] EncodeHeader(string filePath, string archivePath)
    {
        var info = new FileInfo(filePath);
        var sizeText = info.Length.ToString(CultureInfo.InvariantCulture);
        var mtimeText = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        var (prefix, name) = SplitArchivePath(archivePath);

        var nameBytes = EncodingUtf8(name);
        var sizeBytes = EncodingUtf8(sizeText);
        var mtimeBytes = EncodingUtf8(mtimeText);
        var prefixBytes = EncodingUtf8(prefix.Length == 0 ? "." : prefix);

        EnsureLength(nameBytes, FilenameSize, "filename");
        EnsureLength(sizeBytes, ContentSize, "size");
        EnsureLength(mtimeBytes, MtimeSize, "mtime");
        EnsureLength(prefixBytes, PrefixSize, "prefix");

        var header = new byte[HeaderSize];
        var offset = 0;

        WriteSegment(header, ref offset, nameBytes, FilenameSize);
        WriteSegment(header, ref offset, sizeBytes, ContentSize);
        WriteSegment(header, ref offset, mtimeBytes, MtimeSize);
        WriteSegment(header, ref offset, prefixBytes, PrefixSize);

        return header;
    }

    private static ArchiveEntry DecodeHeader(ReadOnlySpan<byte> header)
    {
        var offset = 0;
        var name = ReadSegment(header.Slice(offset, FilenameSize));
        offset += FilenameSize;

        var sizeText = ReadSegment(header.Slice(offset, ContentSize));
        offset += ContentSize;

        var mtimeText = ReadSegment(header.Slice(offset, MtimeSize));
        offset += MtimeSize;

        var prefix = ReadSegment(header.Slice(offset, PrefixSize));
        var path = string.IsNullOrEmpty(prefix) || prefix == "." ? name : $"{prefix}/{name}";

        return new ArchiveEntry(path,
            long.Parse(sizeText, CultureInfo.InvariantCulture),
            long.Parse(mtimeText, CultureInfo.InvariantCulture));
    }

    private static string ResolveOutputPath(string archivePath, string? outputRoot)
    {
        var parts = archivePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (outputRoot is null)
        {
            return Path.Combine(parts);
        }

        var combined = Path.Combine(new[] { outputRoot }.Concat(parts).ToArray());
        var full = Path.GetFullPath(combined);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!IsSubPath(full, outputRoot, comparison))
        {
            throw new InvalidOperationException($"Archive entry '{archivePath}' resolves outside of output directory.");
        }

        return full;
    }

    private static string NormalizeArchivePath(string path)
    {
        return path.Replace('\u005c', '/');
    }

    private static bool IsSubPath(string candidate, string root, StringComparison comparison)
    {
        if (candidate.Length < root.Length)
        {
            return false;
        }

        if (!candidate.StartsWith(root, comparison))
        {
            return false;
        }

        if (candidate.Length == root.Length)
        {
            return true;
        }

        var separator = candidate[root.Length];
        return separator == Path.DirectorySeparatorChar || separator == Path.AltDirectorySeparatorChar;
    }

    private static (string Prefix, string Name) SplitArchivePath(string path)
    {
        var normalized = NormalizeArchivePath(path);
        var lastSlash = normalized.LastIndexOf('/');

        if (lastSlash < 0)
        {
            return (".", normalized);
        }

        var prefix = normalized.Substring(0, lastSlash);
        var name = normalized.Substring(lastSlash + 1);
        return (prefix, name);
    }

    private static string ReadSegment(ReadOnlySpan<byte> segment)
    {
        var trimIndex = segment.IndexOf((byte)0);
        var slice = trimIndex >= 0 ? segment[..trimIndex] : segment;
        return EncodingUtf8(slice);
    }

    private static void WriteSegment(byte[] header, ref int offset, ReadOnlySpan<byte> content, int size)
    {
        content.CopyTo(header.AsSpan(offset, content.Length));
        offset += size;
    }

    private static void EnsureLength(ReadOnlySpan<byte> content, int size, string label)
    {
        if (content.Length > size)
        {
            throw new InvalidOperationException($"Archive {label} exceeds the {size} byte limit.");
        }
    }

    private static byte[] EncodingUtf8(string value) => System.Text.Encoding.UTF8.GetBytes(value);

    private static string EncodingUtf8(ReadOnlySpan<byte> value) => System.Text.Encoding.UTF8.GetString(value);

    private static bool IsEofBlock(ReadOnlySpan<byte> header)
    {
        foreach (var b in header)
        {
            if (b != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count, Action<long> onRead)
    {
        var read = 0;
        while (read < count)
        {
            var bytes = stream.Read(buffer, offset + read, count - read);
            if (bytes == 0)
            {
                throw new EndOfStreamException("Unexpected end of archive stream.");
            }

            read += bytes;
            onRead(bytes);
        }
    }

    private static void CopyExactly(Stream source, Stream destination, long bytesToCopy, Action<long> onRead)
    {
        var remaining = bytesToCopy;
        var buffer = ArrayPool<byte>.Shared.Rent(81920);

        try
        {
            while (remaining > 0)
            {
                var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    throw new EndOfStreamException("Unexpected end of archive stream while reading file content.");
                }

                destination.Write(buffer, 0, read);
                remaining -= read;
                onRead(read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TrySetUnixPermissions(string path, bool isDirectory)
    {
        _ = path;
        _ = isDirectory;
    }

    private static void RaiseExtractionProgress(double percent, long bytesRead, long totalBytes)
    {
        ExtractionProgress?.Invoke(null, new ExtractionProgressEventArgs(percent, bytesRead, totalBytes));
    }

    private static void ThrowIfNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
    }

    private readonly record struct ArchiveEntry(string Path, long Size, long Mtime);
}

/// <summary>
/// Provides extraction progress information.
/// </summary>
public sealed class ExtractionProgressEventArgs : EventArgs
{
    internal ExtractionProgressEventArgs(double percent, long bytesRead, long totalBytes)
    {
        Percent = percent;
        BytesRead = bytesRead;
        TotalBytes = totalBytes;
    }

    /// <summary>
    /// The percent complete, from 0 to 100.
    /// </summary>
    public double Percent { get; }

    /// <summary>
    /// The number of bytes processed so far.
    /// </summary>
    public long BytesRead { get; }

    /// <summary>
    /// The total number of bytes expected.
    /// </summary>
    public long TotalBytes { get; }
}
