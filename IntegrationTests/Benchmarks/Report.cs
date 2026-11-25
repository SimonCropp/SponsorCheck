namespace SponsorCheck.Benchmarks;

public sealed record Result(Scenario Scenario, string Label, Measurement Measurement);

public static class Report
{
    public static void Write(TextWriter writer, Options options, IReadOnlyList<Result> results)
    {
        var libraries = options.Libraries * 3;
        writer.WriteLine();
        writer.WriteLine($"{libraries + options.Consumers} projects: {libraries} libraries in three layers, {options.Consumers} consumers with {options.References} project references each. {options.Runs} clean builds on {options.Nodes} nodes; median (min - max).");
        writer.WriteLine();
        writer.WriteLine("| Scenario | Package | Blocked in the verifier | Per consumer | Build wall-clock |");
        writer.WriteLine("|---|---|---|---|---|");
        foreach (var group in results.GroupBy(_ => (_.Scenario, _.Label)))
        {
            var measurements = group.Select(_ => _.Measurement).ToList();
            var blocked = measurements.Select(_ => _.Blocked.TotalSeconds).ToList();
            var perConsumer = measurements.Select(_ => _.Blocked.TotalSeconds / _.Projects).ToList();
            var wall = measurements.Select(_ => _.Wall.TotalSeconds).ToList();
            writer.WriteLine($"| {group.Key.Scenario.Name}: {group.Key.Scenario.Description} | {group.Key.Label} | {Spread(blocked)} | {Seconds(Median(perConsumer))} | {Spread(wall)} |");
        }

        writer.WriteLine();
        writer.WriteLine("Blocked is the time inside the verifier's target summed over the consumers. They wait in parallel and hand their node back while they do, so it is not time added to the build.");
    }

    static string Spread(List<double> values) =>
        $"{Seconds(Median(values))} ({Seconds(values.Min())} - {Seconds(values.Max())})";

    static string Seconds(double value)
    {
        var format = value < 10 ? "0.00" : "0.0";
        return value.ToString(format, CultureInfo.InvariantCulture) + " s";
    }

    static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return sorted[middle];
        }

        return (sorted[middle - 1] + sorted[middle]) / 2;
    }
}
