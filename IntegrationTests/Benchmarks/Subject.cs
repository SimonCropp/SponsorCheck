namespace SponsorCheck.Benchmarks;

// One SponsorCheck package under measurement: its own feed, the benchmark's author package packed
// against it, and its own copy of the generated solution restored from that feed. Nothing is
// shared between subjects, so two builds of the same version number can be compared.
public sealed class Subject
{
    const string authorFixture = SyntheticSolution.PackageId;

    readonly string solutionDirectory;
    readonly string packagesDirectory;
    readonly IReadOnlyList<string> projects;

    Subject(string label, string solutionDirectory, string packagesDirectory, IReadOnlyList<string> projects)
    {
        Label = label;
        this.solutionDirectory = solutionDirectory;
        this.packagesDirectory = packagesDirectory;
        this.projects = projects;
    }

    public string Label { get; }

    public static async Task<Subject> Prepare(string label, string sponsorCheckPackage, string workDirectory, Options options)
    {
        var root = Path.Combine(workDirectory, label);
        var feed = Path.Combine(root, "feed");
        var packages = Path.Combine(root, "packages");
        Directory.CreateDirectory(feed);
        Directory.CreateDirectory(packages);
        File.Copy(sponsorCheckPackage, Path.Combine(feed, Path.GetFileName(sponsorCheckPackage)));

        await PackAuthorPackage(root, feed, packages, VersionOf(sponsorCheckPackage));

        var solution = Path.Combine(root, "solution");
        Directory.CreateDirectory(solution);
        var projects = SyntheticSolution.Write(solution, feed, options);
        WriteScenario(solution, options.Scenarios[0]);
        await Dotnet.RunOrThrow($"Restoring the {label} solution", solution, packages, "restore", SyntheticSolution.FileName, "--nologo", "--verbosity", "minimal");
        return new(label, solution, packages, projects);
    }

    // The same pack the integration suite's ThePackageBuilder does: an override list instead of
    // live platform calls, a backdated pack date, and bundling forced on so a pull-request build
    // environment cannot skip it.
    static async Task PackAuthorPackage(string root, string feed, string packages, string sponsorCheckVersion)
    {
        var source = Path.Combine(Repo.Root, "IntegrationTests", "Fixtures", "_Shared", authorFixture);
        var target = Path.Combine(root, "author");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        File.WriteAllText(
            Path.Combine(target, "nuget.config"),
            $"""
             <?xml version="1.0" encoding="utf-8"?>
             <configuration>
               <packageSources>
                 <clear />
                 <add key="local" value="{feed}" />
                 <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
               </packageSources>
             </configuration>
             """);

        await Dotnet.RunOrThrow(
            $"Packing {authorFixture} against SponsorCheck {sponsorCheckVersion}",
            target,
            packages,
            "pack",
            $"{authorFixture}.csproj",
            "--configuration",
            "Release",
            "--nologo",
            "--verbosity",
            "minimal",
            $"-p:SponsorListOverride={Repo.SponsorListOverride}",
            $"-p:PackageOutputPath={feed}",
            $"-p:SponsorCheckVersion={sponsorCheckVersion}",
            "-p:SponsorCheck_PackDateOverride=2024-01-01",
            "-p:SponsorCheckBundleInPullRequest=true");
    }

    public async Task<Measurement> Build(Scenario scenario, Options options)
    {
        WriteScenario(solutionDirectory, scenario);
        Clean();
        var result = await Dotnet.RunOrThrow(
            $"Building the {Label} solution ({scenario.Name})",
            solutionDirectory,
            packagesDirectory,
            "build",
            SyntheticSolution.FileName,
            "--configuration",
            "Release",
            "--no-restore",
            "--nologo",
            "--verbosity",
            "minimal",
            $"-m:{options.Nodes}",
            "-clp:PerformanceSummary");
        return Measurement.Parse(result);
    }

    static void WriteScenario(string solutionDirectory, Scenario scenario) =>
        File.WriteAllText(
            Path.Combine(solutionDirectory, "Directory.Build.props"),
            $"""
             <Project>
               <PropertyGroup>
                 {scenario.Property}
               </PropertyGroup>
             </Project>

             """);

    // Every build is a full one: the compile load is what keeps the nodes busy. The restore output
    // directly under obj stays.
    void Clean()
    {
        foreach (var project in projects)
        {
            DeleteIfPresent(Path.Combine(solutionDirectory, project, "bin"));
            DeleteIfPresent(Path.Combine(solutionDirectory, project, "obj", "Release"));
        }
    }

    static void DeleteIfPresent(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // "SponsorCheck.0.27.0.nupkg" -> "0.27.0". The author fixture references exactly this version.
    static string VersionOf(string sponsorCheckPackage)
    {
        const string prefix = "SponsorCheck.";
        var name = Path.GetFileNameWithoutExtension(sponsorCheckPackage);
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new BenchmarkException($"'{sponsorCheckPackage}' is not a SponsorCheck.<version>.nupkg.");
        }

        return name[prefix.Length..];
    }
}
