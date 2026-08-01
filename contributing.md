# Contributing


## Project layout

- `src/SponsorCheck` — the multi-targeted (netstandard2.0;net472) MSBuild task assembly plus the package's bundler `.targets` and embedded verifier template; produces the `SponsorCheck` nupkg
- `src/SponsorCheck.Tests` — TUnit + Verify unit tests for pure helpers and tasks
- `src/SponsorCheck.Web` — the Blazor WASM [setup wizard](https://simoncropp.github.io/SponsorCheck/), deployed to GitHub Pages by `.github/workflows/deploy-blazor.yml`
- `src/SponsorCheck.Web.Tests` — generator snapshot tests, anti-rot checks against the shipped targets/docs, bUnit component tests, and Playwright end-to-end tests over the published WASM output
- `IntegrationTests/IntegrationTests` — end-to-end tests that pack ThePackage with the just-built SponsorCheck and build consumer fixtures (C#, F#, VB) against it


## Build & test

```pwsh
dotnet build src --configuration Release
dotnet run  --project src/SponsorCheck.Tests --configuration Release --no-build
dotnet run  --project src/SponsorCheck.Web.Tests --configuration Release --no-build
dotnet build IntegrationTests --configuration Release
dotnet run  --project IntegrationTests/IntegrationTests --configuration Release --no-build
```

The web tests install Playwright's Chromium on first run (no separate install step needed locally).

Filter to a single test (TUnit uses `--treenode-filter`, not `--filter`):

```pwsh
dotnet run --project src/SponsorCheck.Tests --configuration Release --no-build -- --treenode-filter '/*/*/GitHubSponsorsPlatformTests/LiveLookup'
```

Integration tests depend on the `SponsorCheck` nupkg that building `src` produces at `nugets/`, so rebuild `src` after changing the bundler or verifier before running them.


## Docs are generated

`readme.md` and the pages under `docs/` are processed by [MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets) during the test build. Code blocks wrapped in `snippet:` / `endSnippet` comment markers (and `include:` / `endInclude` blocks) are generated from the referenced source files — edit the source file (e.g. an integration test fixture csproj), not the expanded block, or the change is overwritten on the next build.
