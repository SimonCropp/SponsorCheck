namespace SponsorCheck.Benchmarks;

public sealed record Options
{
    public const string Usage =
        """
        Times the SponsorCheck verifier in a generated multi-project solution.

        dotnet run --project IntegrationTests/Benchmarks --configuration Release --no-build -- [options]

          --baseline <nupkg>   A second SponsorCheck package to measure alongside the one in nugets/,
                               for example one built from another commit.
          --scenarios <list>   Comma-separated: sponsor, exemption, ignored. Default: all three.
          --libraries <n>      Libraries in each of the three layers. Default 12.
          --consumers <n>      Test-style projects that reference the package. Default 8.
          --references <n>     Project references per consumer. Default 6.
          --weight <n>         Classes generated per library, which sets how long it compiles. Default 120.
          --runs <n>           Clean builds per scenario and package. Default 3.
          --nodes <n>          MSBuild nodes. Default: the processor count.
          --keep               Leave the generated solution in the temp directory.
        """;

    public string? BaselinePackage { get; init; }
    public IReadOnlyList<Scenario> Scenarios { get; init; } = Scenario.All;
    public int Libraries { get; init; } = 12;
    public int Consumers { get; init; } = 8;
    public int References { get; init; } = 6;
    public int Weight { get; init; } = 120;
    public int Runs { get; init; } = 3;
    public int Nodes { get; init; } = Environment.ProcessorCount;
    public bool Keep { get; init; }

    public static bool TryParse(string[] args, out Options options, out string error)
    {
        options = new();
        error = "";
        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (name == "--keep")
            {
                options = options with { Keep = true };
                continue;
            }

            if (index + 1 >= args.Length)
            {
                error = $"{name} needs a value.";
                return false;
            }

            var value = args[++index];
            if (!TryApply(name, value, ref options, out error))
            {
                return false;
            }
        }

        if (options.References > options.Libraries)
        {
            error = "--references cannot exceed --libraries: a consumer references first-layer libraries, each once.";
            return false;
        }

        return true;
    }

    static bool TryApply(string name, string value, ref Options options, out string error)
    {
        error = "";
        switch (name)
        {
            case "--baseline":
                if (!File.Exists(value))
                {
                    error = $"--baseline: no file at '{value}'.";
                    return false;
                }

                options = options with { BaselinePackage = Path.GetFullPath(value) };
                return true;

            case "--scenarios":
                var scenarios = new List<Scenario>();
                foreach (var scenarioName in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var scenario = Scenario.All.FirstOrDefault(_ => _.Name == scenarioName);
                    if (scenario == null)
                    {
                        error = $"--scenarios: '{scenarioName}' is not one of {string.Join(", ", Scenario.All.Select(_ => _.Name))}.";
                        return false;
                    }

                    scenarios.Add(scenario);
                }

                if (scenarios.Count == 0)
                {
                    error = "--scenarios needs at least one scenario.";
                    return false;
                }

                options = options with { Scenarios = scenarios };
                return true;

            case "--libraries" or "--consumers" or "--references" or "--weight" or "--runs" or "--nodes":
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                    count < 1)
                {
                    error = $"{name} needs a whole number of at least 1, not '{value}'.";
                    return false;
                }

                options = name switch
                {
                    "--libraries" => options with { Libraries = count },
                    "--consumers" => options with { Consumers = count },
                    "--references" => options with { References = count },
                    "--weight" => options with { Weight = count },
                    "--runs" => options with { Runs = count },
                    _ => options with { Nodes = count }
                };
                return true;

            default:
                error = $"Unknown option '{name}'.";
                return false;
        }
    }
}
