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
        bool onBuildServer = true,
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
        PinBuildServerDetection(psi, onBuildServer);
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
        PinBuildServerDetection(psi, onBuildServer: true);
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

    // Every variable BuildServerDetector reads. Inheriting them would make the child build's answer
    // depend on where the suite runs.
    static string[] buildServerVariables =
    [
        "JENKINS_URL",
        "GITHUB_ACTION",
        "TEAMCITY_VERSION",
        "BuildRunner",
        "GITLAB_CI",
        "GO_SERVER_URL",
        "TRAVIS_BUILD_ID",
        "DOTNET_RUNNING_IN_CONTAINER",
        "APPVEYOR",
        "WSL_DISTRO_NAME",
        "TF_BUILD"
    ];

    // SponsorCheck logs its message-severity diagnostics (SC017, SC029/SC030/SC031, SC059) at high
    // importance on a build server and low importance elsewhere, and these builds are read at
    // minimal verbosity, which carries only the high ones. So the child build is told outright what
    // it is running on, rather than inheriting it: otherwise every assertion about one of those
    // lines would pass on CI and fail on a developer machine, or the reverse.
    //
    // JENKINS_URL is the flag of choice because neither the SDK, NuGet nor MSBuild reads it — unlike
    // TF_BUILD or GITHUB_ACTION, which steer terminal-logger and telemetry behaviour of their own.
    static void PinBuildServerDetection(ProcessStartInfo psi, bool onBuildServer)
    {
        foreach (var variable in buildServerVariables)
        {
            psi.Environment.Remove(variable);
        }

        if (onBuildServer)
        {
            psi.Environment["JENKINS_URL"] = "https://build-server.invalid/";
        }
    }
}
