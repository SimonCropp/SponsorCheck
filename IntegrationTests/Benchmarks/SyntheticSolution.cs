namespace SponsorCheck.Benchmarks;

// Writes the solution the benchmark builds.
//
// Three layers of libraries, each library referencing three from the layer below, and a set of
// test-style consumers that reference first-layer libraries plus the SponsorCheck-bundling
// package. The shape is the one that made the verifier expensive in real solutions: a consumer
// becomes buildable while later layers are still compiling, so every node is busy when its
// verifier runs. Anything the verifier then asks of another project has to wait for that
// project's node to come free, which is roughly one compile per question.
//
// That is also why the libraries carry real code. A solution of empty projects builds with idle
// nodes, and an <MSBuild> call into an idle node costs almost nothing, so it would measure none of
// what a large build pays.
public static class SyntheticSolution
{
    public const string FileName = "bench.slnx";
    public const string PackageId = "ThePackageOwnerModeWithExemptions";

    const int layerCount = 3;
    const int methodsPerClass = 40;

    public static IReadOnlyList<string> Write(string root, string feed, Options options)
    {
        var projects = new List<string>();
        var layers = new List<List<string>>();
        for (var layer = 0; layer < layerCount; layer++)
        {
            var names = new List<string>();
            for (var index = 0; index < options.Libraries; index++)
            {
                var name = $"L{layer + 1}_{index:00}";
                var references = new List<string>();
                if (layer > 0)
                {
                    var below = layers[layer - 1];
                    for (var offset = 0; offset < Math.Min(3, below.Count); offset++)
                    {
                        references.Add(below[(index + offset) % below.Count]);
                    }
                }

                WriteProject(root, name, references, referencesPackage: false, LibrarySource(name, options.Weight));
                names.Add(name);
            }

            layers.Add(names);
            projects.AddRange(names);
        }

        var firstLayer = layers[0];
        for (var index = 0; index < options.Consumers; index++)
        {
            var name = $"T_{index:00}";
            var references = new List<string>();
            for (var offset = 0; offset < options.References; offset++)
            {
                references.Add(firstLayer[(index * 2 + offset) % firstLayer.Count]);
            }

            WriteProject(root, name, references, referencesPackage: true, $"namespace {name};\n\npublic class Tests;\n");
            projects.Add(name);
        }

        var solution = new StringBuilder("<Solution>\n");
        foreach (var project in projects)
        {
            solution.Append($"  <Project Path=\"{project}/{project}.csproj\" />\n");
        }

        solution.Append("</Solution>\n");
        File.WriteAllText(Path.Combine(root, FileName), solution.ToString());

        File.WriteAllText(
            Path.Combine(root, "nuget.config"),
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
        // Empty, so nothing above the temp directory leaks in. Directory.Build.props is written per
        // scenario: it is where the sponsorship property goes.
        File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), "<Project/>\n");
        return projects;
    }

    static void WriteProject(string root, string name, List<string> references, bool referencesPackage, string source)
    {
        var directory = Path.Combine(root, name);
        Directory.CreateDirectory(directory);
        var project = new StringBuilder(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>

            """);
        foreach (var reference in references)
        {
            project.Append($"    <ProjectReference Include=\"..\\{reference}\\{reference}.csproj\" />\n");
        }

        if (referencesPackage)
        {
            project.Append($"    <PackageReference Include=\"{PackageId}\" Version=\"1.0.0\" />\n");
        }

        project.Append("  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(directory, $"{name}.csproj"), project.ToString());
        File.WriteAllText(Path.Combine(directory, "Code.cs"), source);
    }

    // Lambdas, generic inference and string interpolation: enough binding work per method that a
    // library takes a second or two to compile without needing megabytes of source.
    static string LibrarySource(string name, int classes)
    {
        var source = new StringBuilder($"namespace {name};\n");
        for (var classIndex = 0; classIndex < classes; classIndex++)
        {
            source.Append($"public class C{classIndex}\n{{\n");
            for (var method = 0; method < methodsPerClass; method++)
            {
                source.Append(
                    $$"""
                          public int M{{method}}(int a, int b)
                          {
                              var list = new System.Collections.Generic.List<int> { a, b, {{method + 3}} };
                              var query = System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(list, _ => _ > a), _ => _ * b + {{classIndex + 1}});
                              return System.Linq.Enumerable.Sum(query) + $"{a}:{b}".Length;
                          }

                      """);
            }

            source.Append("}\n");
        }

        return source.ToString();
    }
}
