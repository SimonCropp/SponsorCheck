namespace SponsorCheck.Benchmarks;

public sealed record DotnetResult(int ExitCode, string Output, TimeSpan Elapsed);

public static class Dotnet
{
    // Every invocation gets its own packages directory, so a SponsorCheck already in the global
    // cache under the same version can never stand in for the one being measured.
    //
    // Node reuse is off for all of them. A build then starts from fresh nodes, so none inherits
    // another's loaded assemblies or evaluated projects; and nothing outlives its command, where a
    // reusable node would keep the SponsorCheck task assembly loaded from that packages directory
    // and so keep the benchmark from deleting its own work directory.
    public static async Task<DotnetResult> Run(string workingDirectory, string packagesDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        startInfo.Environment["DOTNET_NOLOGO"] = "true";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";
        startInfo.Environment["NUGET_PACKAGES"] = packagesDirectory;
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        stopwatch.Stop();
        return new(process.ExitCode, await stdout + await stderr, stopwatch.Elapsed);
    }

    public static async Task<DotnetResult> RunOrThrow(string what, string workingDirectory, string packagesDirectory, params string[] arguments)
    {
        var result = await Run(workingDirectory, packagesDirectory, arguments);
        if (result.ExitCode != 0)
        {
            throw new BenchmarkException($"{what} failed with exit code {result.ExitCode}:\n{result.Output}");
        }

        return result;
    }
}

public sealed class BenchmarkException(string message) :
    Exception(message);
