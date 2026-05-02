# EnforceOssSponsorship

Enforce the [Open Source Maintenance Fee](https://opensourcemaintenancefee.org/) at build time via NuGet. Gentle nudging plus honesty rather than runtime DRM.

OSS authors install `EnforceOssSponsorship` as a development dependency in their library project. At pack time, a Bundler MSBuild task fetches the author's sponsor list from one or more configured **sponsorship platforms** (GitHub Sponsors, Open Collective, Polar), hashes each account, and bundles a build-time verifier into the produced NuGet package — *without* adding any runtime dependency to that package.

When consumers reference the produced package, the bundled verifier runs in their Release builds and requires one of three mutually exclusive license-mode metadata declarations per package: a sponsor account that matches the bundled list, a time-bounded private license, or an explicit "ignored" with a build warning.

## OSS author setup

Add `EnforceOssSponsorship` as a `PrivateAssets="all"` development dependency on the library project, with one `<Platform>Account` metadatum per platform you accept sponsorship through.

<!-- snippet: ThePackage.csproj -->
<a id='snippet-ThePackage.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>ThePackage</PackageId>
    <Version>1.0.0</Version>
    <Authors>Acme Corp</Authors>
    <Description>Sample library used by EnforceOssSponsorship integration tests.</Description>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <NoWarn>$(NoWarn);NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="EnforceOssSponsorship" Version="0.1.0"
                      PrivateAssets="all"
                      IncludeAssets="build;buildTransitive;contentFiles;analyzers"
                      GitHubSponsorsAccount="acmecorp"
                      OpenCollectiveAccount="acme-org"
                      PolarAccount="acme" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/_Shared/ThePackage/ThePackage.csproj#L1-L19' title='Snippet source file'>snippet source</a> | <a href='#snippet-ThePackage.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At least one `<Platform>Account` must be set. Credentials per platform:

| Platform | MSBuild property | User-secrets key | Required? |
|---|---|---|---|
| `GitHubSponsors` | `<GitHubSponsorsToken>` | `EnforceOssSponsorship:GitHubSponsorsToken` | Required (any GitHub PAT, no scopes — GitHub's GraphQL API requires authentication even for public data) |
| `OpenCollective` | `<OpenCollectiveToken>` | `EnforceOssSponsorship:OpenCollectiveToken` | Optional (public collectives queryable anonymously) |
| `Polar` | `<PolarToken>` | `EnforceOssSponsorship:PolarToken` | Required |

### Storing credentials locally

Use [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets) — no extra wiring. The bundler reads `EnforceOssSponsorship:<Platform>Token` keys from the secrets file at the conventional path (`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` on Windows; `~/.microsoft/usersecrets/<id>/secrets.json` on Unix).

Run from the directory containing your library's `.csproj`, or pass `--project <path>` explicitly — `dotnet user-secrets` resolves the project from the current directory and errors if it finds zero or multiple project files.

```pwsh
dotnet user-secrets init                                                # writes <UserSecretsId> into the csproj in cwd
dotnet user-secrets set "EnforceOssSponsorship:GitHubSponsorsToken" "ghp_xxx"
dotnet user-secrets set "EnforceOssSponsorship:PolarToken" "polar_yyy"
```

### Multiple packable projects in one repo

If your repo produces multiple NuGet packages, configure once and let MSBuild's normal cascading mechanisms apply:

```xml
<!-- Directory.Packages.props — sponsor accounts in one place -->
<PackageVersion Include="EnforceOssSponsorship" Version="0.1.0"
                GitHubSponsorsAccount="acmecorp"
                OpenCollectiveAccount="acme-org"
                PolarAccount="acme" />
```

```xml
<!-- Directory.Build.props — one UserSecretsId shared by every project so they all read the same secrets.json -->
<PropertyGroup>
  <UserSecretsId>acmecorp-monorepo-secrets</UserSecretsId>
</PropertyGroup>
```

Each csproj just declares the bare reference:

```xml
<PackageReference Include="EnforceOssSponsorship"
                  PrivateAssets="all"
                  IncludeAssets="build;buildTransitive;contentFiles;analyzers" />
```

Each project still bundles independently at its own pack time (one platform fetch per packable project).

For CI, set the corresponding MSBuild properties via env vars (e.g. an AppVeyor/GitHub Actions secret named `GitHubSponsorsToken` lands as `$(GitHubSponsorsToken)` automatically).

Precedence: explicit MSBuild property → env var (auto-imported by MSBuild) → user-secrets.

For testing or offline builds, set `<SponsorListOverride>` to a JSON file path:

<!-- snippet: sponsors-override.json -->
<a id='snippet-sponsors-override.json'></a>
```json
[
  { "platform": "GitHubSponsors", "account": "alice" },
  { "platform": "GitHubSponsors", "account": "bob" },
  { "platform": "OpenCollective", "account": "acme-org" },
  { "platform": "Polar",          "account": "acme" }
]
```
<sup><a href='/IntegrationTests/IntegrationTests/Fixtures/sponsors-override.json#L1-L6' title='Snippet source file'>snippet source</a> | <a href='#snippet-sponsors-override.json' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Consumer license modes (per package, mutually exclusive)

Pick exactly one mode per `<PackageReference>` (or set the metadata on the matching `<PackageVersion>` under CPM). The verifier reads metadata from both and merges (agree → use; disagree → error EOSS006).

### Sponsor account match (any platform)

<!-- snippet: Consumer.ValidGitHubSponsor.csproj -->
<a id='snippet-Consumer.ValidGitHubSponsor.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ThePackage" Version="1.0.0"
                      GitHubSponsorAccount="alice" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/Consumer.ValidGitHubSponsor/Consumer.ValidGitHubSponsor.csproj#L1-L11' title='Snippet source file'>snippet source</a> | <a href='#snippet-Consumer.ValidGitHubSponsor.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

If the package author accepts multiple platforms and you sponsor on one of them, supply the matching `<Platform>SponsorAccount` metadata. You may supply more than one — the verifier passes if **any** account matches the bundled list.

### Time-bounded private license

<!-- snippet: Consumer.FutureLicense.csproj -->
<a id='snippet-Consumer.FutureLicense.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ThePackage" Version="1.0.0"
                      SponsorshipLicensedUntil="2099-12" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/Consumer.FutureLicense/Consumer.FutureLicense.csproj#L1-L11' title='Snippet source file'>snippet source</a> | <a href='#snippet-Consumer.FutureLicense.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

For private B2B licensing arrangements outside of the platforms. Format is `yyyy-MM`; the license is valid through the end of that month UTC.

### Explicit ignore (escape hatch)

<!-- snippet: Consumer.IgnoredLicense.csproj -->
<a id='snippet-Consumer.IgnoredLicense.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="ThePackage" Version="1.0.0"
                      SponsorshipIgnored="true" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/Consumer.IgnoredLicense/Consumer.IgnoredLicense.csproj#L1-L11' title='Snippet source file'>snippet source</a> | <a href='#snippet-Consumer.IgnoredLicense.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Build passes but emits `EOSS003` warning every Release build, telling the consumer they are not honoring the maintenance fee.

## Diagnostic codes

| Code | Severity | Meaning |
|---|---|---|
| EOSS001 | Error | No license mode set on the PackageReference / PackageVersion |
| EOSS002 | Error | Multiple license modes set (mutually exclusive) |
| EOSS003 | Warning | `SponsorshipIgnored="true"` — consumer has opted out |
| EOSS004 | Error | None of the supplied platform accounts match the bundled hash list |
| EOSS005 | Error | `SponsorshipLicensedUntil` has expired |
| EOSS006 | Error | Conflicting metadata between PackageReference and PackageVersion |
| EOSS007 | Error | `SponsorshipLicensedUntil` not in `yyyy-MM` format |
| EOSS010 | Error | Bundled sponsor hash file is missing from the package (corrupt install) |
| EOSS101 | Error | OSS author missing `PrivateAssets="all"` on the EnforceOssSponsorship reference |
| EOSS102 | Error | OSS author has no `<Platform>Account` metadata on EnforceOssSponsorship |

## How it works

The bundler runs at the OSS author's pack time (Release config, `IsPackable=true`). It:

1. Reads `<Platform>Account` metadata from the EnforceOssSponsorship `PackageReference` / `PackageVersion`.
2. For each enabled platform, calls the platform's API (or reads `SponsorListOverride` if set) to get the list of sponsor accounts.
3. Hashes each as `lowercase_hex(SHA256(utf8("{platform-id}:{lowercase(account)}")))`. Platform-id prefix prevents cross-platform spoofing.
4. Writes the sorted, deduped hashes to `build/EnforceOssSponsorship.SponsorHashes.txt` and a verifier `.targets` file to `build/<ThePackageId>.targets` inside the produced nupkg, plus the verifier task DLL under `tasks/`.

The verifier runs in consumer projects (Release config) and:

1. Locates the consumer's `PackageReference` and `PackageVersion` for ThePackage by id.
2. Merges metadata across both. Reads license-mode declarations (`SponsorshipIgnored`, `SponsorshipLicensedUntil`, `<Platform>SponsorAccount`).
3. Applies the appropriate decision: ignored (warn), sponsor (check hash list), license (check expiry), or fail with the relevant EOSS code.

## Hashing — what it protects

The hash list is **light obfuscation**, not real privacy. Anyone with a wordlist of GitHub usernames can reverse-engineer the published hashes by recomputing `SHA256("{platform-id}:{lowercase(login)}")` for each candidate. Sponsorship is publicly visible on the platforms anyway when sponsors choose so. The hash format is enough to prevent casual scraping and to keep plaintext logins out of every consumer's bin folder.

## Project layout

- `src/EnforceOssSponsorship` — the meta nupkg (only ships files and the bundler `.targets`)
- `src/EnforceOssSponsorship.Tasks` — single multi-targeted (netstandard2.0;net472) MSBuild task assembly with both the Bundler and the Verifier
- `src/EnforceOssSponsorship.Tests` — TUnit + Verify unit tests for pure helpers and tasks
- `IntegrationTests/IntegrationTests` — end-to-end tests that pack ThePackage with the just-built EnforceOssSponsorship and build consumer fixtures (C#, F#, VB) against it

## Build & test

```pwsh
dotnet build src --configuration Release
dotnet run  --project src/EnforceOssSponsorship.Tests --configuration Release --no-build
dotnet build IntegrationTests --configuration Release
dotnet run  --project IntegrationTests/IntegrationTests --configuration Release --no-build
```

## License

MIT — see [license.txt](license.txt).
