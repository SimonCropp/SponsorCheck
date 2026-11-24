namespace SponsorCheck.Benchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help") ||
            args.Contains("-h"))
        {
            Console.WriteLine(Options.Usage);
            return 0;
        }

        if (!Options.TryParse(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Options.Usage);
            return 1;
        }

        var workDirectory = Path.Combine(Path.GetTempPath(), "sponsorcheck-bench", Guid.NewGuid().ToString("N"));
        try
        {
            var results = await Measure(options, workDirectory);
            Report.Write(Console.Out, options, results);
            return 0;
        }
        catch (BenchmarkException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            CleanUp(options, workDirectory);
        }
    }

    // Never the reason a run fails: by now the numbers are printed, and a file something still
    // holds open is not worth losing them over.
    static void CleanUp(Options options, string workDirectory)
    {
        if (options.Keep)
        {
            Console.WriteLine($"Kept {workDirectory}");
            return;
        }

        if (!Directory.Exists(workDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not delete {workDirectory}: {exception.Message}");
        }
    }

    static async Task<List<Result>> Measure(Options options, string workDirectory)
    {
        var packages = new List<(string Label, string Path)>();
        if (options.BaselinePackage != null)
        {
            packages.Add(("baseline", options.BaselinePackage));
        }

        packages.Add(("current", Repo.CurrentPackage()));

        var subjects = new List<Subject>();
        foreach (var (label, path) in packages)
        {
            Console.WriteLine($"Preparing {label}: {path}");
            subjects.Add(await Subject.Prepare(label, path, workDirectory, options));
        }

        // Runs are the outer loop so each round measures everything once before any is repeated:
        // whatever else the machine is doing lands on all of them rather than on one.
        var results = new List<Result>();
        var total = options.Runs * options.Scenarios.Count * subjects.Count;
        for (var run = 0; run < options.Runs; run++)
        {
            foreach (var scenario in options.Scenarios)
            {
                foreach (var subject in subjects)
                {
                    var measurement = await subject.Build(scenario, options);
                    results.Add(new(scenario, subject.Label, measurement));
                    Console.WriteLine($"[{results.Count}/{total}] {scenario.Name}, {subject.Label}: built in {measurement.Wall.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s, {measurement.Blocked.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)} s blocked across {measurement.Projects} consumers");
                }
            }
        }

        return results;
    }
}
