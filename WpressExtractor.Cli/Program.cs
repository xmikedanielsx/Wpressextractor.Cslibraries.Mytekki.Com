using MyTekki.WpressExtractor;

if (args.Length < 2 || (args[0] != "-a" && args[0] != "-e"))
{
    Console.WriteLine("Usage: WpressExtractor.Cli -a|-e <archive> [files...] [outputDir] [--progress-step <percent>]");
    Console.WriteLine("  --progress-step <percent>  Report progress at the given percent interval (e.g. 10). Use 0.01 for fine-grained updates.");
    return;
}

var archivePath = args[1];
double? progressStep = null;
string? outputDir = null;

for (var i = 2; i < args.Length; i++)
{
    var arg = args[i];
    if (string.Equals(arg, "--progress-step", StringComparison.OrdinalIgnoreCase))
    {
        if (i + 1 >= args.Length || !double.TryParse(args[i + 1], out var step))
        {
            Console.WriteLine("Invalid --progress-step value. Provide a numeric percent.");
            return;
        }

        progressStep = step;
        i++;
        continue;
    }

    if (!arg.StartsWith("--", StringComparison.Ordinal) && outputDir is null)
    {
        outputDir = arg;
        continue;
    }
}

if (args[0] == "-a")
{
    if (args.Length < 3)
    {
        Console.WriteLine("Provide at least one file or directory to encode.");
        return;
    }

    WpressArchive.Encode(archivePath, args.Skip(2));
    Console.WriteLine($"Created {archivePath}");
}
else
{
    EventHandler<ExtractionProgressEventArgs>? handler = null;
    if (progressStep is not null && progressStep > 0)
    {
        var step = progressStep.Value;
        var lastReportedTick = -1;
        handler = (_, e) =>
        {
            var tick = (int)Math.Round(e.Percent * 100.0, MidpointRounding.AwayFromZero);
            var stepTicks = (int)Math.Round(step * 100.0, MidpointRounding.AwayFromZero);
            if (stepTicks <= 0)
            {
                return;
            }

            var bucket = (tick / stepTicks) * stepTicks;
            if (tick >= 10000 || bucket > lastReportedTick)
            {
                Console.WriteLine($"Progress: {bucket / 100.0:0.00}% ({e.BytesRead}/{e.TotalBytes} bytes)");
                lastReportedTick = bucket;
            }
        };

        WpressArchive.ExtractionProgress += handler;
    }

    try
    {
        WpressArchive.Decode(archivePath, outputDir);
    }
    finally
    {
        if (handler is not null)
        {
            WpressArchive.ExtractionProgress -= handler;
        }
    }

    Console.WriteLine(outputDir is null
        ? $"Extracted {archivePath}"
        : $"Extracted {archivePath} to {outputDir}");
}
