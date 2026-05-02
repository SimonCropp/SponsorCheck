# SponsorCheck

Enforce the [Open Source Maintenance Fee](https://opensourcemaintenancefee.org/) at build time via NuGet. Gentle nudging plus honesty rather than runtime DRM.

OSS authors install `SponsorCheck` as a development dependency in their library project. At pack time, a Bundler MSBuild task fetches the author's sponsor list from one or more configured **sponsorship platforms** (GitHub Sponsors, Open Collective, Polar), hashes each account, and bundles a build-time verifier into the produced NuGet package — *without* adding any runtime dependency to that package.

When consumers reference the produced package, the bundled verifier runs in their Release builds and requires one of three mutually exclusive license-mode metadata declarations per package: a sponsor account that matches the bundled list, a time-bounded private license, or an explicit "ignored" with a build warning.

## OSS author setup

Add `SponsorCheck` as a `PrivateAssets="all"` development dependency on your library project, with one `<Platform>Account` metadatum per platform you accept sponsorship through.

<!-- snippet: ThePackage.csproj -->
<a id='snippet-ThePackage.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>ThePackage</PackageId>
    <Version>1.0.0</Version>
    <Authors>Acme Corp</Authors>
    <Description>Sample library used by SponsorCheck integration tests.</Description>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <NoWarn>$(NoWarn);NU5128</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SponsorCheck" Version="0.1.0"
                      PrivateAssets="all"
                      GitHubSponsorsAccount="acmecorp"
                      OpenCollectiveAccount="acme-org"
                      PolarAccount="acme" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/_Shared/ThePackage/ThePackage.csproj#L1-L18' title='Snippet source file'>snippet source</a> | <a href='#snippet-ThePackage.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

At least one `<Platform>Account` must be set. Credentials per platform:

| Platform | MSBuild property | User-secrets key | Required? |
|---|---|---|---|
| `GitHubSponsors` | `<GitHubToken>` | `SponsorCheck:GitHubToken` | Required — [classic PAT](https://github.com/settings/tokens/new) with `read:user` (when sponsored as a user) and/or `read:org` (when sponsored as an organization), or a [fine-grained PAT](https://github.com/settings/personal-access-tokens/new) with **Sponsorships: Read-only**. The token must be owned by the sponsored account (or an admin of the sponsored org) — otherwise private sponsors are silently filtered out and the bundled hash list will be incomplete |
| `OpenCollective` | `<OpenCollectiveToken>` | `SponsorCheck:OpenCollectiveToken` | Optional — public collectives are queryable anonymously, but anonymous calls hit rate limits on collectives with many backers. Create a [Personal Token](https://opencollective.com/applications) (no scopes required — the token is used for rate-limit headroom, not access) |
| `Polar` | `<PolarToken>` | `SponsorCheck:PolarToken` | Required — [organization access token](https://docs.polar.sh/integrate/authentication/personal-access-token) with scopes `subscriptions:read`, `customers:read`, `organizations:read`. The customer scope matters: without it Polar can return null `github_username` / `email` on embedded customer objects, causing the bundler to fall back to opaque `user_id`s that won't match consumer-declared `<PolarSponsorAccount>` values |

> **Token expiry.** GitHub PATs and Polar API keys both expire. If your CI builds suddenly fail with HTTP 401 from a platform, your token has likely expired — rotate it and update the secret. Pick "no expiration" on the GitHub PAT form if you want set-and-forget; otherwise put the rotation date in your calendar.

### Storing credentials locally

Use [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets) — no extra wiring. The bundler reads `SponsorCheck:<Platform>Token` keys from the secrets file at the conventional path (`%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` on Windows; `~/.microsoft/usersecrets/<id>/secrets.json` on Unix).

Run from the directory containing your library's `.csproj`, or pass `--project <path>` explicitly — `dotnet user-secrets` resolves the project from the current directory and errors if it finds zero or multiple project files.

```pwsh
# writes <UserSecretsId> into the csproj in cwd
dotnet user-secrets init
dotnet user-secrets set "SponsorCheck:GitHubToken" "ghp_xxx"
dotnet user-secrets set "SponsorCheck:OpenCollectiveToken" "zzz"
dotnet user-secrets set "SponsorCheck:PolarToken" "polar_yyy"
```

### Multiple packable projects in one repo

If your repo produces multiple NuGet packages, configure once and let MSBuild's normal cascading mechanisms apply:

```xml
<!-- Directory.Packages.props — sponsor accounts in one place -->
<PackageVersion Include="SponsorCheck" Version="0.1.0"
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
<PackageReference Include="SponsorCheck" PrivateAssets="all" />
```

Each project still bundles independently at its own pack time (one platform fetch per packable project).

For CI, set the corresponding MSBuild properties via env vars (e.g. an AppVeyor/GitHub Actions secret named `GitHubToken` lands as `$(GitHubToken)` automatically).

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

Pick exactly one mode per `<PackageReference>` (or set the metadata on the matching `<PackageVersion>` under CPM). The verifier reads metadata from both and merges (agree → use; disagree → error SC006).

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

#### Recent sponsors: SponsorshipStart

The bundled hash list is frozen at the package's pack date. If you started sponsoring *after* the package was released, your account can't possibly be in the list. Add `SponsorshipStart="yyyy-MM-dd"` to attest to when you started:

```xml
<PackageReference Include="ThePackage" Version="1.0"
                  GitHubSponsorAccount="carol"
                  SponsorshipStart="2026-04-30" />
```

If `SponsorshipStart` is **after** the package's pack date, the verifier trusts the declaration and emits a high-priority build message naming the unverified sponsor (audit trail in the consumer's own build log). If `SponsorshipStart` is on or before the pack date, the hash check is enforced as normal — claiming to be a sponsor at release time means the account should already be in the bundled list.

`SponsorshipStart` in the future fails with SC014. Once the OSS author ships a new version of ThePackage that includes the new sponsor in its hash list, `SponsorshipStart` can be dropped.

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

Build passes but emits `SC003` warning every Release build, telling the consumer they are not honoring the maintenance fee.

## Diagnostic codes

| Code | Severity | Meaning |
|---|---|---|
| SC001 | Error | No license mode set on the PackageReference / PackageVersion |
| SC002 | Error | Multiple license modes set (mutually exclusive) |
| SC003 | Warning | `SponsorshipIgnored="true"` — consumer has opted out |
| SC004 | Error | None of the supplied platform accounts match the bundled hash list |
| SC005 | Error | `SponsorshipLicensedUntil` has expired |
| SC006 | Error | Conflicting metadata between PackageReference and PackageVersion |
| SC007 | Error | `SponsorshipLicensedUntil` not in `yyyy-MM` format |
| SC010 | Error | Bundled sponsor hash file is missing from the package (corrupt install) |
| SC013 | Error | `SponsorshipStart` not in `yyyy-MM-dd` format |
| SC014 | Error | `SponsorshipStart` is in the future |
| SC102 | Error | OSS author has no `<Platform>Account` metadata on SponsorCheck |

## How it works

The bundler runs at the OSS author's pack time (Release config, `IsPackable=true`). It:

1. Reads `<Platform>Account` metadata from the SponsorCheck `PackageReference` / `PackageVersion`.
2. For each enabled platform, calls the platform's API (or reads `SponsorListOverride` if set) to get the list of sponsor accounts.
3. Hashes each as `lowercase_hex(SHA256(utf8("{platform-id}:{lowercase(account)}")))`. Platform-id prefix prevents cross-platform spoofing.
4. Writes the sorted, deduped hashes to `build/SponsorCheck.SponsorHashes.txt` and a verifier `.targets` file to `build/<ThePackageId>.targets` inside the produced nupkg, plus the verifier task DLL under `tasks/`.

The verifier runs in consumer projects (Release config) and:

1. Locates the consumer's `PackageReference` and `PackageVersion` for ThePackage by id.
2. Merges metadata across both. Reads license-mode declarations (`SponsorshipIgnored`, `SponsorshipLicensedUntil`, `<Platform>SponsorAccount`).
3. Applies the appropriate decision: ignored (warn), sponsor (check hash list), license (check expiry), or fail with the relevant SC code.


## Hashing — what it protects

The hash list is **light obfuscation**, not real privacy. Anyone with a wordlist of GitHub usernames can reverse-engineer the published hashes by recomputing `SHA256("{platform-id}:{lowercase(login)}")` for each candidate. Sponsorship is publicly visible on the platforms anyway when sponsors choose so. The hash format is enough to prevent casual scraping and to keep plaintext logins out of every consumer's bin folder.


## Project layout

- `src/SponsorCheck` — the multi-targeted (netstandard2.0;net472) MSBuild task assembly plus the package's bundler `.targets` and embedded verifier template; produces the `SponsorCheck` nupkg
- `src/SponsorCheck.Tests` — TUnit + Verify unit tests for pure helpers and tasks
- `IntegrationTests/IntegrationTests` — end-to-end tests that pack ThePackage with the just-built SponsorCheck and build consumer fixtures (C#, F#, VB) against it


## Build & test

```pwsh
dotnet build src --configuration Release
dotnet run  --project src/SponsorCheck.Tests --configuration Release --no-build
dotnet build IntegrationTests --configuration Release
dotnet run  --project IntegrationTests/IntegrationTests --configuration Release --no-build
```


## Icon

https://thenounproject.com/icon/optical-illusion-344030/
