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

Each wizard screen is snapshotted as a PNG plus the page html. The html is pretty printed via [Verify.AngleSharp](https://github.com/VerifyTests/Verify.AngleSharp), and Blazor's `<!--!-->` render markers are stripped in the same pass, so a snapshot diff points at the element that actually changed. Content inside `<pre>` is preserved verbatim, keeping the generated snippet in each code box faithful.

Filter to a single test (TUnit uses `--treenode-filter`, not `--filter`):

```pwsh
dotnet run --project src/SponsorCheck.Tests --configuration Release --no-build -- --treenode-filter '/*/*/GitHubSponsorsPlatformTests/LiveLookup'
```

Integration tests depend on the `SponsorCheck` nupkg that building `src` produces at `nugets/`, so rebuild `src` after changing the bundler or verifier before running them.


## The wizard bundles its own fonts

`src/SponsorCheck.Web/wwwroot/fonts/` holds subset woff2 faces that the wizard serves itself, rather than naming system families like `Segoe UI` or `Consolas`. Chromium shapes text with HarfBuzz on every platform, so a font served with the app measures identically everywhere; a system font does not, and the differing advance widths re-wrap prose and change the height of a full-page screenshot. That is what makes the `ScreenSnapshotTests` PNG baselines — authored on Windows — hold on the Linux CI images.

Bundling the faces is only half of it: it pins which outlines are used, not how they are measured. Three further rules in `app.css` — `text-rendering: geometricPrecision` on `html, body`, an explicit `font-family` on `code, pre, kbd, samp` (the UA stylesheet's rule beats inheritance), and `line-height: 1` on `code, kbd, samp` — were each needed before the Linux render matched, and `wwwroot/fonts/readme.md` explains why. They read as cosmetic tweaks; removing any one reintroduces per-OS drift.

Two things guard the arrangement:

- `RepoContractTests.ShippedFontsCoverRenderedText` fails if the wizard renders a character the bundled subsets do not cover, since that character would fall back to a system font and reintroduce the drift. The `unicode-range` descriptors in `wwwroot/css/app.css` are the source of truth for coverage.
- `ScreenSnapshotTests.VerifyScreen` awaits `document.fonts.ready` before capturing, because the faces are declared `font-display: block`.

Adding a glyph therefore means rebuilding the affected woff2 *and* widening the matching `unicode-range`. The faces are built with [fonttools](https://github.com/fonttools/fonttools) from the upstream sources listed in `wwwroot/fonts/readme.md`:

```pwsh
python -m venv fontvenv
./fontvenv/Scripts/python.exe -m pip install fonttools brotli
```

Then, for each face, pin any non-weight variation axis, keep the `name` table (it carries the licence records), subset to the wanted codepoints, and save with `font.flavor = "woff2"`. Note that `instantiateVariableFont` can leave `gvar` without entries for non-varying glyphs, which the subsetter then trips over — give every glyph an explicit entry before subsetting.


## Docs are generated

`readme.md` and the pages under `docs/` are processed by [MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets) during the test build. Code blocks wrapped in `snippet:` / `endSnippet` comment markers (and `include:` / `endInclude` blocks) are generated from the referenced source files — edit the source file (e.g. an integration test fixture csproj), not the expanded block, or the change is overwritten on the next build.
