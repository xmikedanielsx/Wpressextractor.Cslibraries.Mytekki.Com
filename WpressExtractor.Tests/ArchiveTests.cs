using MyTekki.WpressExtractor;
using Xunit;

#pragma warning disable CS1591

namespace WpressExtractor.Tests;

public class ArchiveTests
{
    [Fact]
    public void EncodeDecodeRoundTripPreservesContent()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wpress-test-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(root, "source");
        var outputDir = Path.Combine(root, "output");
        var archivePath = Path.Combine(root, "backup.wpress");

        Directory.CreateDirectory(sourceDir);
        var filePath = Path.Combine(sourceDir, "hello.txt");
        File.WriteAllText(filePath, "Hello from Wpress");
        var mtime = DateTime.UtcNow.AddMinutes(-5);
        File.SetLastWriteTimeUtc(filePath, mtime);

        WpressArchive.Encode(archivePath, new[] { sourceDir }, baseDirectory: sourceDir);
        WpressArchive.Decode(archivePath, outputDir);

        var restored = Path.Combine(outputDir, "hello.txt");
        Assert.True(File.Exists(restored));
        Assert.Equal("Hello from Wpress", File.ReadAllText(restored));
        Assert.Equal(mtime, File.GetLastWriteTimeUtc(restored), TimeSpan.FromSeconds(1));

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void DecodeRaisesProgressEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), $"wpress-progress-{Guid.NewGuid():N}");
        var sourceDir = Path.Combine(root, "source");
        var outputDir = Path.Combine(root, "output");
        var archivePath = Path.Combine(root, "backup.wpress");

        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "hello.txt"), "Hello");

        WpressArchive.Encode(archivePath, new[] { sourceDir }, baseDirectory: sourceDir);

        var maxPercent = 0.0;
        var callCount = 0;
        EventHandler<ExtractionProgressEventArgs>? handler = (_, e) =>
        {
            callCount++;
            maxPercent = Math.Max(maxPercent, e.Percent);
        };

        WpressArchive.ExtractionProgress += handler;
        try
        {
            WpressArchive.Decode(archivePath, outputDir);
        }
        finally
        {
            WpressArchive.ExtractionProgress -= handler;
        }

        Assert.True(callCount > 0);
        Assert.True(maxPercent >= 100.0);

        Directory.Delete(root, recursive: true);
    }
}
