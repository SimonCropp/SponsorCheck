# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & test

```pwsh
dotnet build src --configuration Release
dotnet run  --project src/SponsorCheck.Tests --configuration Release --no-build
dotnet build IntegrationTests --configuration Release
dotnet run  --project IntegrationTests/IntegrationTests --configuration Release --no-build
```

Filter to a single test (TUnit uses `--treenode-filter`, not `--filter`):

```pwsh
dotnet run --project src/SponsorCheck.Tests --configuration Release --no-build -- --treenode-filter '/*/*/GitHubSponsorsPlatformTests/LiveLookup'
```

Integration tests build `src` first by depending on `nugets/SponsorCheck.<version>.nupkg`, so always rebuild `src` after changing the bundler/verifier before running integration tests.

`SponsorCheck.csproj` has `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`, so `dotnet build src -c Release` produces the nupkg at `nugets/`. Don't add a separate `dotnet pack` step.

## Setup wizard (SponsorCheck.Web)

`src/SponsorCheck.Web` is a Blazor WASM wizard (deployed to GitHub Pages at https://simoncropp.github.io/SponsorCheck/ by `.github/workflows/deploy-blazor.yml`; tests gate the deploy) that generates consumer/author configuration. The consumer flow can inspect a published nupkg client-side (`Services/NupkgParser` + `Services/PackageLookup`, api.nuget.org flat container via the [RemoteZip](https://github.com/Papyrine/RemoteZip) package — HTTP range requests fetch only the zip central directory plus the sidecar/targets files in one coalesced batch read, so package size doesn't matter; the 30 MB `MaxNupkgBytes` cap only bounds the full-download fallback when a server ignores Range) to pre-answer owner mode, platforms, and exemptions — the sidecar file names/formats it parses are pinned by `RepoContractTests.SidecarFileNamesMatchVerifierTemplates`. `src/SponsorCheck.Web.Tests` contains anti-rot tests (`RepoContractTests`) that compare the wizard's hardcoded names against `build/SponsorCheck.targets`, `EmbeddedTemplates/ConsumerVerifier*.targets`, `OverrideableCodes.cs`, and the diagnostic-code docs — adding or renaming metadata, properties, or SC codes fails those tests until the wizard models (`Models/Platform.cs`, `Models/Overrides.cs`, `Models/ScCode.cs`, the generators) are updated to match. Run them with `dotnet run --project src/SponsorCheck.Web.Tests --configuration Release --no-build`.

The wizard serves its own subset woff2 faces from `wwwroot/fonts/` instead of naming system families, because Chromium shapes with HarfBuzz on every platform — a bundled font measures identically everywhere, a system font does not, and the differing advance widths re-wrap prose and change full-page screenshot heights. That is what lets the Windows-authored `ScreenSnapshotTests` PNG baselines hold on Linux CI. The `unicode-range` descriptors in `wwwroot/css/app.css` declare coverage and `RepoContractTests.ShippedFontsCoverRenderedText` fails if the wizard renders a character outside them (it would fall back to a system font), so adding such a glyph means rebuilding the woff2 and widening the range — see `contributing.md`. Don't replace the font stacks with system families, and don't drop the `document.fonts.ready` wait in `ScreenSnapshotTests.VerifyScreen`; the faces are `font-display: block`, so an early capture is blank.

The html half of each screen snapshot is pretty printed through Verify.AngleSharp (`HtmlPrettyPrint.All` in `ModuleInitializer`), which also strips Blazor's `<!--!-->` render markers — as comment nodes, and as literal text inside `<title>`, where they are parsed as raw text rather than a comment. `<pre>` content is left byte-for-byte alone by the formatter (Verify.AngleSharp 5.1.0+), so the generated snippet inside a code box stays faithful; don't reformat those blocks by hand, they are regenerated on every run.

Bundling the faces pins which outlines are used, not how they are measured, so three further `app.css` rules are load-bearing and were each needed before Linux matched Windows: `text-rendering: geometricPrecision` on `html, body` (rasterizers round advance widths onto their hinting grid differently, re-wrapping prose), an explicit `font-family` on `code, pre, kbd, samp` (the UA stylesheet's rule beats inheritance, so inline code silently uses the platform monospace), and `line-height: 1` on `code, kbd, samp` (a line box is the union of the strut and each inline box, placed from per-font ascent/descent that round differently per platform). They look cosmetic; deleting any one reintroduces per-OS PNG drift. `wwwroot/fonts/readme.md` records the reasoning.

## The two-stage MSBuild architecture

This is the central design. Hold it in mind when reading any file in `SponsorCheck/`.

**Stage 1 — Bundler (OSS-author pack time).** `BundleSponsorListTask` runs from `src/SponsorCheck/build/SponsorCheck.targets`, which is auto-imported into any project that PackageReferences `SponsorCheck`. It:

1. Reads `<Platform>Account` metadata from the author's `PackageReference Include="SponsorCheck"` (or matching `PackageVersion` under CPM).
2. Calls each enabled platform's API (or reads `<SponsorListOverride>` JSON if set) to fetch the account list.
3. Writes four files into the author's intermediate output, all packed into the produced nupkg:
   - `build/SponsorCheck.SponsorHashes.txt` — sorted, deduped `SHA256("{platform-id}:{lowercase(account)}")` hashes.
   - `build/SponsorCheck.PackDate.txt` — UTC date the package was packed (or `SponsorCheck_PackDateOverride` if set, used by integration tests).
   - `build/SponsorCheck.AuthorAccounts.txt` — `platformId={account}` lines for each enabled `<Platform>Account` on the author's SponsorCheck reference. Used by the verifier to construct platform sponsor URLs in the SC001/SC005 messages.
   - `build/<ThePackageId>.targets` — the verifier targets file, generated by template substitution (see below).
4. Also packs the multi-targeted `tasks/{netstandard2.0,net472}/SponsorCheck.dll` (plus its closure of System.* deps from `bin/{Configuration}/{tfm}/`).

The `build/` prefix on those four files is the default. When the author sets `CheckTransitiveReferences="true"` on the SponsorCheck reference, `SponsorCheck.targets` swaps the pack folder to `buildTransitive/` (via the `_SponsorCheck_BuildFolder` property). NuGet imports `build/<id>.targets` only for direct references but `buildTransitive/<id>.targets` for direct *and* transitive ones, so the folder choice is the direct-vs-transitive verification toggle — no verifier code change is involved. The `tasks/` DLLs stay put (referenced as `..\tasks\`, which resolves identically from either folder).

### Coexisting with a package that ships its own `<PackageId>.targets`

NuGet auto-imports exactly one file named `<PackageId>.targets`, and the verifier claims it. A package that *also* ships its own `<PackageId>.targets` (its own MSBuild build logic) would otherwise collide at pack — `NU5118` under `TreatWarningsAsErrors`, or a silently dropped verifier otherwise. `SponsorCheck.targets` handles this: before adding its own `<None>` items it scans `@(None)` for an author item landing in the `$(_SponsorCheck_BuildFolder)\$(PackageId).targets` slot — matching both a `PackagePath` that names the file and one that names only the folder (`buildTransitive\`, the more common authoring style, where the item's own file name puts it in the slot). If found, it relocates that item (via `Remove ... MatchOnMetadata="Identity;PackagePath"` + re-`Include`) to a `<PackageId>.SponsorCheckInner.targets` sidecar, and passes `InnerTargetsImportFileName` to the bundler. The bundler then replaces the `__SC_INNER_IMPORT__` placeholder (in both verifier templates) with an `<Import>` of that sidecar — so the verifier owns the auto-import slot while the author's own logic still loads in consumers. Matching on `Identity` as well as `PackagePath` is load-bearing once folder-form paths are matched: siblings packed to the same folder (a `<PackageId>.props`) share the `PackagePath` value and would otherwise be removed too.

`MatchOnMetadata="PackagePath"` alone was also why an author shipping to both `build/` and `buildTransitive/` used to keep the non-pack-folder copy — which meant direct consumers imported the author's `build/<id>.targets` and got no verifier at all. Now both copies are relocated and the verifier plus a full set of data sidecars is packed into *both* folders (`_SponsorCheck_SecondFolder` / `_SponsorCheck_PackSecondFolder`; the data `<None>`s batch over `@(_SponsorCheck_PackFolder)`). Each folder is self-contained because the verifier resolves its data files relative to `$(MSBuildThisFileDirectory)`, and the two verifier copies carry identical target names, so a consumer importing both is idempotent. This only happens when the author claimed the slot in the second folder — with no collision the verifier still lives in exactly one folder, which is what keeps `CheckTransitiveReferences` the direct-vs-transitive toggle. Regression coverage: `AuthorPackTests.OwnTargets_RelocatedToSidecar_NoCollision` / `_FolderFormPackagePath` (pack structure), `ConsumerBuildTests.DirectReference_ToPackageShippingItsOwnTargets_IsVerified` (the direct consumer actually verifies), and `BundleSponsorListTaskTests.InnerTargetsImport_*` (placeholder substitution).

**Stage 2 — Verifier (consumer build time).** When a consumer project PackageReferences ThePackage, NuGet auto-imports `build/<ThePackageId>.targets` (or `buildTransitive/<ThePackageId>.targets`) from the nupkg. That file `UsingTask`s the version-scoped verifier task (`VerifySponsorshipTask_<version>`, see below) from the bundled `tasks/` DLL and runs it `BeforeTargets="BeforeBuild"`. The verifier reads consumer-side license metadata (`SponsorshipLicenseIgnored`, `SponsorshipLicensedUntil`, `<Platform>SponsorAccount`, `SponsorshipStart`) and either passes, warns, or fails with an `SC0xx` code.

ThePackage acquires **no runtime dependency** on `SponsorCheck`. Everything ships embedded.

## Why template substitution (not `$(...)` expansion)

`ConsumerVerifier.targets` (in `SponsorCheck/EmbeddedTemplates/`) uses `__SC_PACKAGE_ID__` and `__SC_PACKAGE_ID_RAW__` as placeholders. The bundler substitutes them at pack time:

- `__SC_PACKAGE_ID__` → the package id with non-alphanumeric chars replaced by `_` (used in MSBuild target/item names — MSBuild rejects dots/dashes there).
- `__SC_PACKAGE_ID_RAW__` → the literal package id (used inside element values).
- `__SC_TASK_NAME__` → the version-scoped verifier task type (`VersionedTaskName.Verify`), used in both the `UsingTask TaskName` and the task element.

MSBuild target `Name` attributes do **not** support `$(Property)` expansion. That's the constraint forcing template substitution rather than dynamic property reads. Per-package unique target names (`_SponsorCheck_Verify_<sanitized-id>`) prevent collisions when a consumer references multiple SponsorCheck-using packages.

## Why the verifier task name carries the SponsorCheck version

MSBuild's task registry is keyed by **task name alone** — not assembly path, identity, or version — and the first `UsingTask` to claim a name serves every invocation of it in that project. Since every SponsorCheck-bundling package ships its own copy of `SponsorCheck.dll`, a bare shared name meant one copy ran *all* the verifiers in a consumer project. The moment two such packages bundled SponsorCheck versions with different task APIs, the build died with `MSB4064: The "<X>" parameter is not supported by the "VerifySponsorshipTask" task` — pointing at the *newer* package's targets while naming the *older* package's DLL path. (Giving each release a distinct `AssemblyVersion` does not help: registration is resolved before assembly identity is consulted.)

So `SponsorCheck.csproj`'s `_SponsorCheck_GenerateVersionedTaskName` target emits `VerifySponsorshipTask_<version>` — a subclass of `VerifySponsorshipTask` (which is therefore *not* sealed) — plus a `VersionedTaskName.Verify` constant initialized with `nameof`, so the bundler's `__SC_TASK_NAME__` substitution can't drift from the type actually compiled. Each release lands in its own registry slot, so version-skewed packages coexist. `$(Version)` feeds the name, with `.`/`-`/`+` sanitized to `_` so prerelease versions stay valid identifiers.

Consequences worth knowing: `VerifySponsorshipTask` must stay unsealed and stay the direct base of exactly one generated type, and packages published before this change still register the bare name — harmlessly, since it no longer collides with anything newer.

Regression coverage: `AuthorPackTests.BundledTargetsScopeTaskNameToSponsorCheckVersion` (the packed verifier binds a scoped name and no bare one) and `BundleSponsorListTaskTests.TemplateSubstitution_BindsVersionScopedTaskName` (the named type exists, derives from the task, and reaches the rendered targets).

## Multi-targeting in `SponsorCheck`

`SponsorCheck.csproj` is a single multi-targeted project (`netstandard2.0;net472`) that holds both the task code and the package metadata. `IncludeBuildOutput=false` keeps the build output out of `lib/`; a custom `_SponsorCheck_PackTaskDlls` target copies each TFM's DLL closure into `tasks/{tfm}/`. Both TFMs must build because the consumer's MSBuild may be either Core (.NET) or Framework (Visual Studio / MSBuild.exe). `SponsorCheck.targets` and the generated verifier targets pick the right TFM via `'$(MSBuildRuntimeType)' == 'Core'` conditionals. Task classes live in the global namespace, so `UsingTask` references them by unqualified name (`BundleSponsorListTask` author-side, `VerifySponsorshipTask_<version>` consumer-side); the shipping assembly is `SponsorCheck.dll`. `BundleSponsorListTask` keeps a bare name because a project can only reference one SponsorCheck version, so it can't collide the way the verifier could.

The Tests project references the netstandard2.0 build via `SetTargetFramework="TargetFramework=netstandard2.0"`.

## Token resolution precedence

In `BundleSponsorListTask.TokenFor`:

1. Explicit task property (`GitHubToken`, `OpenCollectiveToken`, `PolarToken`) — comes from MSBuild properties of the same name. MSBuild auto-imports env vars as properties of the same name (case-insensitive but underscore-sensitive), so a `GitHubToken` env var lands as `$(GitHubToken)` automatically. Conventional CI names like `GITHUB_TOKEN` / `POLAR_API_KEY` do **not** auto-flow — the env var must be named to match the property exactly.
2. User-secrets at key `SponsorCheck:<Platform>Token`. The UserSecretsId is read from `$(UserSecretsId)` in the **author's** csproj, not this repo.

`UserSecretsReader` reads the conventional path (`%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` on Windows; `~/.microsoft/usersecrets/<id>/secrets.json` on Unix) and flattens nested JSON into colon-keyed paths.

## SponsorshipStart bypass

A consumer who began sponsoring after a package's pack date can't possibly be in the bundled hash list. `SponsorshipStart="yyyy-MM-dd"` lets them attest to a start date. If `SponsorshipStart > PackDate`, the verifier skips the hash check and emits a high-priority build message naming the unverified sponsor (audit trail in the consumer's own log). Future `SponsorshipStart` fails with `SC015`.

This is why `OutputPackDatePath` exists in the bundler and `PackDatePath` is plumbed all the way to `DecisionApplier.Apply`.

## Integration test isolation

`ThePackageBuilder` (one-time setup) packs `IntegrationTests/Fixtures/_Shared/ThePackage` once per suite with `SponsorListOverride=Fixtures/sponsors-override.json` and `SponsorCheck_PackDateOverride=2024-01-01` into a per-suite local feed under `Path.GetTempPath()/sponsorcheck-it-feed/<guid>`. Consumer fixtures get a generated `nuget.config` pointing at that feed.

`ConsumerBuildTests.BuildFixture` writes empty `Directory.Build.props` / `Directory.Build.targets` into each fixture's temp dir so the parent `IntegrationTests/` config (CPM, etc.) doesn't leak in. It also uses an isolated `--packages` dir under the work dir to avoid the global NuGet cache caching a stale `SponsorCheck.0.1.0` between runs.

The 2024-01-01 backdate is what enables `Consumer.RecentSponsor` to test the `SponsorshipStart > PackDate` bypass against today's clock.

## Diagnostic code conventions

- `SC0xx` — consumer-side (verifier). 001-007 are license-mode errors; 008 is the `SponsorshipStart` trust-attestation info message; 009 is corrupt install; 010-011 are `SponsorshipStart` errors.
- `SC1xx` — author-side (bundler). 100 = catch-all for `MaintenanceFeeException` (HTTP/GraphQL errors); 101 = no platform metadata; 102 = required platform credential missing (typed via `MissingCredentialException`); 103 = user-secrets read warning.

Any time a code is added, removed, or its message text changes, update the per-code section in `docs/VerifierDiagnosticCodes.md` (SC0xx) or `docs/BundlerDiagnosticCodes.md` (SC1xx) alongside `VerifySponsorshipTask`/`BundleSponsorListTask`. The Syntax line must mirror the actual interpolated format string and the Example must be a plausible rendering — these double as the public reference and as a reviewer cross-check.

## Project-reference coverage (skipping redundant transitive verification)

A package with `CheckTransitiveReferences="true"` ships its verifier under `buildTransitive/`, so NuGet imports it into every transitive consumer in the closure. In a multi-project solution where the direct `<PackageReference>` lives in one project (call it Lib) and other projects only reach the package through `<ProjectReference Include="Lib\Lib.csproj" />`, the verifier *also* fires in those downstream projects — emitting duplicate SC0xx errors for what is logically one verification.

Both `ConsumerVerifier.targets` and `ConsumerVerifierOwner.targets` define two companion targets per package — `_SponsorCheck_CoversDeep_<sanitized-id>` and `_SponsorCheck_CoversShallow_<sanitized-id>` — alongside the verifier target. Before the verifier runs the actual `VerifySponsorshipTask`, it `<MSBuild>`-calls the deep responder on every `@(ProjectReference)`. Each responder checks the called project's `@(PackageReference)` for ThePackageId; the deep one also walks that project's own `@(ProjectReference)` via the shallow responder. That gives depth-2 visibility (self, direct ref, ref-of-ref) — the common solution layout. If any project reports coverage, the verifier task is skipped via Condition. The `_SponsorCheck_OwnerVerified_<owner>` flag still gets set so other owner-mode packages in the same project don't re-walk.

The coverage path only matters when `CheckTransitiveReferences="true"` — without it, NuGet doesn't import the verifier into transitive consumers in the first place, so there's nothing to skip. `SkipNonexistentTargets="true"` makes the `<MSBuild>` call cheap when a `<ProjectReference>` target doesn't have the responder defined (e.g. it doesn't transitively reference ThePackage at all).

Important nuance the user explicitly scoped: **`CheckTransitiveReferences` semantics for NuGet transitive references are unchanged** — a consumer that pulls ThePackage in via a transitive *package* dependency still gets the verifier and still has to supply metadata. The skip-on-coverage logic only fires when the coverage is reachable via a `<ProjectReference>` (i.e. same solution).

The responders use `$(_SponsorCheck_ThePackageId)` (a property set from the `>__SC_PACKAGE_ID_RAW__<` element substitution) inside attribute values, because the bundler's `__SC_PACKAGE_ID_RAW__` substitution at `BundleSponsorListTask.cs:142` is regex-anchored to `>...<` element content and won't touch attribute strings. If you ever need the raw id in an attribute, route it through the property — don't expand the substitution to attribute syntax.

Regression coverage: `ConsumerBuildTests.OwnerMode_ProjectReferenceCoverage_SkipsRedundantVerification` plus the fixture `Consumer.OwnerCoveredByProjectReference` (top-level with no PackageReference + no property; Lib subdir with the PackageReference + property). The author fixture `_Shared/ThePackageOwnerModeTransitive` is owner mode + `CheckTransitiveReferences="true"` — needed to exercise this path.

## MSBuild task batching trap

`SponsorCheck.targets` and `ConsumerVerifier.targets` flatten item-metadata into scalar properties (`@(Items->'%(M)')`) before passing to the bundler/verifier task. Do not revert this to direct `%(ItemGroup.Metadata)` task parameters. With CPM both `@(PackageReference)` and `@(PackageVersion)` carry SponsorCheck items, and direct metadata accessors cause MSBuild to invoke the task once per ItemGroup batch. The PackageReference batch typically has no metadata under CPM (it lives on PackageVersion), so that batch fires SC101/SC001 even though the other one would succeed. Regression coverage: `AuthorPackTests.CpmMultiTargeted_MetadataOnPackageVersion_BundlesSuccessfully` (bundler) and `ConsumerBuildTests.CpmConsumer_LicenseMetadataOnPackageVersion_PassesWithoutBatchingError` (verifier).

## Configuration gating

The bundler target is gated on `'$(Configuration)' == 'Release'` (it only runs alongside packing). The consumer verifier runs in every configuration — Debug builds enforce sponsorship too. Don't reintroduce a `Configuration == 'Release'` gate on the verifier.

## Code style

- File-scoped namespaces, `LangVersion=preview`, nullable enabled, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true` (see `src/Directory.Build.props`).
- Tests use TUnit + Verify. `StubBuildEngine` and `TaskLoggingHelperFor` (in `VerifySponsorshipTaskTests.cs`) are the standard test plumbing for invoking tasks directly.
- Live tests that need credentials use `LiveTokenResolver.ResolveOrSkip(envVar, secretKey, label, extra?)` — env var → user-secrets → `Skip.Test`. Skip messages flip between user-secrets-first (local) and env-var-only (CI) advice based on `BuildServerDetector.Detected`. The bundler's missing-credential errors (`SC102`) flip the same way via `TokenSetupAdvice.MissingTokenMessage`.
- `src/SponsorCheck/BuildServerDetector.cs` is a verbatim duplicate of `VerifyTests/DiffEngine/src/DiffEngine/BuildServerDetector.cs`. If the upstream changes meaningfully, re-sync this copy rather than editing in place.

## readme and docs pages are generated

`readme.md` and the pages under `docs/` use [MarkdownSnippets](https://github.com/SimonCropp/MarkdownSnippets) (via `MarkdownSnippets.MsBuild` referenced from `src/SponsorCheck.Tests`). Snippet anchors look like `<!-- snippet: name --> ... <!-- endSnippet -->` (includes: `<!-- include: name -->`) and pull from real fixture files. Edits inside snippet/include blocks get clobbered on the next test build — edit the source file (e.g. an integration fixture csproj, or `docs/*.include.md`), not the expanded block.

Doc structure: `readme.md` is a deliberately short landing page split by audience — consumers land in `docs/ConsumerUsage.md`, OSS authors in `docs/AuthorSetup.md`. Detailed feature docs belong in those two guides (or `docs/WhyBuildTimeVerification.md` for rationale), not in the readme. Contributor-facing content (project layout, build/test instructions) lives in `contributing.md`.
