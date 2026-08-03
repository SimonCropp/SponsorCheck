namespace SponsorCheck.IntegrationTests;

public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public string Combined => Stdout + Stderr;
}

public static class DotnetCliRunner
{
    public static async Task<CliResult> Run(
        string command,
        string projectPath,
        string configuration,
        IReadOnlyDictionary<string, string>? properties = null,
        string? workingDirectory = null,
        string? packagesDir = null,
        CancellationToken cancellation = default)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(Path.GetFullPath(projectPath))!
        };
        psi.Environment["DOTNET_NOLOGO"] = "true";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";
        if (packagesDir != null)
        {
            psi.Environment["NUGET_PACKAGES"] = packagesDir;
        }

        psi.ArgumentList.Add(command);
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("--configuration");
        psi.ArgumentList.Add(configuration);
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add("--verbosity");
        psi.ArgumentList.Add("minimal");
        if (properties != null)
        {
            foreach (var kv in properties)
            {
                psi.ArgumentList.Add($"-p:{kv.Key}={kv.Value}");
            }
        }

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        return new CliResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    /// Runs a single target via `dotnet msbuild` — no restore, no build, no configuration.
    /// For evaluation-only checks against a hand-written project, where the pack/build switches
    /// <see cref="Run"/> always passes (--configuration, --verbosity minimal) aren't valid.
    public static async Task<CliResult> RunMsBuild(
        string projectPath,
        string target,
        CancellationToken cancellation = default)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(projectPath))!
        };
        psi.Environment["DOTNET_NOLOGO"] = "true";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true";
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add($"-target:{target}");
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-verbosity:minimal");

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        return new CliResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }
}
