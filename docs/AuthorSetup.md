# OSS author setup

Add `SponsorCheck` as a `PrivateAssets="all"` development dependency on the library project, with one `<Platform>Account` metadatum per supported platform. The produced package acquires no runtime dependency on SponsorCheck — everything ships embedded (see [What gets bundled](#what-gets-bundled)).

<!-- snippet: ThePackage.csproj -->
<a id='snippet-ThePackage.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SponsorCheck" Version="$(SponsorCheckVersion)"
                      PrivateAssets="all"
                      GitHubSponsorsAccount="acmecorp"
                      OpenCollectiveAccount="acme-org"
                      PolarAccount="acme" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/_Shared/ThePackage/ThePackage.csproj#L1-L12' title='Snippet source file'>snippet source</a> | <a href='#snippet-ThePackage.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Platforms

At least one `<Platform>Account` must be set. Credentials per platform:


### [GitHub Sponsors](https://github.com/open-source/sponsors)

 * MSBuild property: `<GitHubToken>`
 * Env var: `GitHubToken` (auto-imported into the MSBuild property of the same name)
 * User-secrets key: `SponsorCheck:GitHubToken`

Required - [classic PAT](https://github.com/settings/tokens/new) with `read:user`, plus `read:org` if sponsored as an organization. `read:user` is always required because the bundler reads per-sponsorship metadata (`isOneTimePayment`, `createdAt`, `isActive`) from `sponsorshipsAsMaintainer`, and GitHub gates those fields on `read:user` even when the maintainer is an organization. Fine-grained PATs don't expose a Sponsorships permission, so a classic PAT is the only option. The token must be owned by the sponsored account (or an admin of the sponsored org) — otherwise private sponsors are silently filtered out and the bundled hash list will be incomplete.

Some organizations disable classic-PAT access in their security settings. When sponsored as such an org, a classic PAT will fail with a `FORBIDDEN` error from GitHub at pack time and the bundler emits an actionable message. The org admin needs to re-enable classic-PAT access for the sponsored org.


#### One-time sponsors

One-time GitHub sponsors are bundled for **one month from the payment date**. After that window the entry drops out of the next pack — the same behaviour a monthly sponsor gets after one billing cycle of non-renewal.

For this to map cleanly to "an effective month of sponsor status", set GitHub Sponsors' **"Set minimum amount"** (under [Sponsor profile → Tiers](https://github.com/sponsors)) to the **same value as the lowest monthly tier**. That way any one-time sponsorship is priced at least as much as a single month at the entry tier, so the one-month bundle window is paid for. If the minimum is lower than the monthly tier, one-time sponsors get the full month of sponsor status for less than the recurring sponsors pay — usually not the intent.

Recurring sponsors are bundled while their sponsorship is active and dropped as soon as it lapses, independent of this window.


### [OpenCollective](https://opencollective.com)

 * MSBuild property: `<OpenCollectiveToken>`
 * Env var: `OpenCollectiveToken` (auto-imported into the MSBuild property of the same name)
 * User-secrets key: `SponsorCheck:OpenCollectiveToken`

Optional - public collectives are queryable anonymously, but anonymous calls hit rate limits on collectives with many backers. Create a [Personal Token](https://opencollective.com/applications) (no scopes required — the token is used for rate-limit headroom, not access).


### [Polar](https://polar.sh)

 * MSBuild property: `<PolarToken>`
 * Env var: `PolarToken` (auto-imported into the MSBuild property of the same name)
 * User-secrets key: `SponsorCheck:PolarToken`

Required - [organization access token](https://docs.polar.sh/integrate/authentication/personal-access-token) with scopes `subscriptions:read`, `customers:read`, `organizations:read`. The customer scope matters: without it Polar can return null `github_username` / `email` on embedded customer objects, causing the bundler to fall back to opaque `user_id`s that won't match consumer-declared `<PolarSponsorAccount>` values.


## Token expiry

GitHub PATs and Polar API keys both expire. If a CI build suddenly fails with HTTP 401 from a platform, the token has likely expired — rotate it and update the secret. Pick "no expiration" on the GitHub PAT form for set-and-forget; otherwise add the rotation date to a calendar.


## Storing credentials

Precedence: explicit MSBuild property → env var (auto-imported by MSBuild) → user-secrets.


### Local dev — user-secrets

Recommended for local builds. [`dotnet user-secrets`](https://learn.microsoft.com/aspnet/core/security/app-secrets) stores tokens at `%APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json` (Windows) or `~/.microsoft/usersecrets/<id>/secrets.json` (Unix) — outside the repo, so there's no risk of accidentally committing the value. The bundler reads `SponsorCheck:<Platform>Token` keys.

Run from the directory containing the library's `.csproj`, or pass `--project <path>` explicitly — `dotnet user-secrets` resolves the project from the current directory and errors if it finds zero or multiple project files.

```pwsh
# writes <UserSecretsId> into the csproj in cwd
dotnet user-secrets init
dotnet user-secrets set "SponsorCheck:GitHubToken" "ghp_xxx"
dotnet user-secrets set "SponsorCheck:OpenCollectiveToken" "zzz"
dotnet user-secrets set "SponsorCheck:PolarToken" "polar_yyy"
```


### CI — encrypted env vars

Recommended for CI builds, where there's no per-developer profile to hold a user-secrets file. Encrypt the token in the CI provider's secret store (AppVeyor "secure variable", GitHub Actions secret, Azure DevOps secret variable, etc.) and surface it as an env var named `GitHubToken`, `OpenCollectiveToken`, or `PolarToken`. MSBuild auto-imports env vars as properties, so no extra wiring is needed — the bundler picks them up via the same `<GitHubToken>` / `<OpenCollectiveToken>` / `<PolarToken>` resolution path. The env var name must match the MSBuild property name modulo case (`GitHubToken`, `githubtoken`, and `GITHUBTOKEN` all resolve via case-insensitive property lookup), but punctuation matters — conventional CI names like `GITHUB_TOKEN` won't auto-flow.


### Pull request builds

Most CI providers withhold encrypted secrets from pull-request builds (especially PRs from forks), so the credential above isn't available — a pack there would otherwise fail with [SC102](BundlerDiagnosticCodes.md#sc102). A PR build also never publishes the package it produces, so the sponsorship verifier that bundling would embed is throwaway anyway.

So on a detected pull-request build the bundler is **skipped**: the package still packs — without the verifier — and no credential is required. A high-importance build message records that it happened. Detection covers the common providers via their PR-only signals — AppVeyor (`APPVEYOR_PULL_REQUEST_NUMBER`), GitHub Actions (`GITHUB_EVENT_NAME=pull_request`), Azure DevOps (`SYSTEM_PULLREQUEST_PULLREQUESTID`), GitLab, Bitbucket, Jenkins, Travis, CircleCI, Buildkite — and is deliberately conservative, so a real release build is never mistaken for a PR.

To validate the verifier on PR builds that *do* have the credential, opt back in:

```xml
<PropertyGroup>
  <SponsorCheckBundleInPullRequest>true</SponsorCheckBundleInPullRequest>
</PropertyGroup>
```


## Multiple packable projects in one repo

For repos that produce multiple NuGet packages, configure once and let MSBuild's normal cascading mechanisms apply:

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

Each csproj declares the bare reference:

```xml
<PackageReference Include="SponsorCheck" PrivateAssets="all" />
```

Each project still bundles independently at its own pack time (one platform fetch per packable project).


## Owner mode

When a family of packages is covered by sponsoring a single account, add `SponsorOwner` to the SponsorCheck reference to opt the produced package into **owner mode**. Consumers then configure sponsorship once via a global MSBuild property (see [Owner mode](ConsumerUsage.md#owner-mode) in the consumer guide) instead of per-package metadata — the natural shape for several libraries published under one GitHub org.

<!-- snippet: ThePackageOwnerMode.csproj -->
<a id='snippet-ThePackageOwnerMode.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <!-- SponsorOwner opts this package into owner mode: consumers configure sponsorship once via
         global MSBuild properties rather than per-package metadata. -->
    <PackageReference Include="SponsorCheck" Version="$(SponsorCheckVersion)"
                      PrivateAssets="all"
                      GitHubSponsorsAccount="acmecorp"
                      OpenCollectiveAccount="acme-org"
                      PolarAccount="acme"
                      SponsorOwner="acme" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/_Shared/ThePackageOwnerMode/ThePackageOwnerMode.csproj#L1-L15' title='Snippet source file'>snippet source</a> | <a href='#snippet-ThePackageOwnerMode.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The `<Platform>Account` metadata is still required — the bundler fetches and bundles the sponsor list exactly as in per-package mode. `SponsorOwner` only changes the *consumer-side* verifier that gets bundled: it reads global MSBuild properties rather than `<PackageReference>` metadata, and de-duplicates so multiple owner-mode packages from the same owner verify once per build. The owner id is an opaque label — give every package in the family the same value. Owner-mode consumers see the [SC021–SC028](VerifierDiagnosticCodes.md#sc021) diagnostics; the severity/message overrides below apply to them too.


## Checking transitive references

By default the bundled verifier ships in the package's `build/` folder, which NuGet imports only for **direct** `<PackageReference>`s. A project that pulls the package in transitively — through another package that depends on it — is never checked. Setting `CheckTransitiveReferences="true"` on the SponsorCheck reference packs the verifier (and its sidecars) under `buildTransitive/` instead, so NuGet imports it for direct **and** transitive references:

```xml
<PackageReference Include="SponsorCheck" Version="$(SponsorCheckVersion)"
                  PrivateAssets="all"
                  GitHubSponsorsAccount="acmecorp"
                  CheckTransitiveReferences="true" />
```

A transitively-referenced consumer has no `<PackageReference>` of its own to carry a sponsor account, so an unconfigured one fails with the same [SC001](VerifierDiagnosticCodes.md#sc001) family as a direct consumer — the resolution is to add a direct reference declaring a license mode. Leaving the metadatum unset (or `false`) keeps the default: direct references only. The choice is the author's, baked into the produced nupkg at pack time.

Project references within the same solution are handled differently. If a project pulls the package in through a `<ProjectReference>` to a sibling project that has the direct `<PackageReference>`, the verifier in the downstream project skips — the direct consumer's verifier already produces the authoritative result, and emitting the diagnostic again in every dependent project would be noise. The check walks two levels of `<ProjectReference>` from each consumer, which covers the typical `App → Lib → Package` and `App → Web → Lib → Package` shapes. NuGet-transitive consumers (`Consumer → MiddlePackage → Package`) are *not* affected by this — they still verify under `CheckTransitiveReferences`, because the author's intent there is to enforce sponsorship across the package graph.


## Packages that ship their own MSBuild targets

NuGet auto-imports exactly one file named `<PackageId>.targets` into a consumer, and SponsorCheck claims that slot for the bundled verifier. A package that *also* ships its own `<PackageId>.targets` — for example to inject a source generator as an analyzer, register a build task, or set default properties — has both files wanting the same slot. SponsorCheck handles this automatically: at pack time it detects the author's own `<PackageId>.targets`, moves it aside to a `<PackageId>.SponsorCheckInner.targets` sidecar, and has the generated verifier `<Import>` that sidecar. The verifier owns the auto-import slot and the author's build logic still loads in consumers — no `NU5118` collision, no manual wiring:

```xml
<ItemGroup>
  <!-- The author's own build logic — packed to the <PackageId>.targets slot as usual. -->
  <None Include="build\MyOssLib.targets" Pack="true" PackagePath="build\MyOssLib.targets" />
  <None Include="build\MyOssLib.targets" Pack="true" PackagePath="buildTransitive\MyOssLib.targets" />

  <PackageReference Include="SponsorCheck" Version="$(SponsorCheckVersion)"
                    PrivateAssets="all"
                    GitHubSponsorsAccount="acmecorp"
                    CheckTransitiveReferences="true" />
</ItemGroup>
```

Only the copy in the folder SponsorCheck packs into is relocated (`build/` by default, or `buildTransitive/` under [`CheckTransitiveReferences`](#checking-transitive-references)). When the file is shipped to both folders — so it loads for direct *and* transitive references — the other copy is left in place; using identical MSBuild target names across the two keeps a direct consumer that imports both idempotent. This applies to `<PackageId>.targets` only; a shipped `<PackageId>.props` is untouched, since SponsorCheck never claims the props slot.


## Tuning verifier severity and message text

By default the verifier emits [`SC001`](VerifierDiagnosticCodes.md#sc001) (no license mode set), [`SC007`](VerifierDiagnosticCodes.md#sc007) (sponsor account not in list), and [`SC009`](VerifierDiagnosticCodes.md#sc009) (license expired) as **errors** that fail the consumer build, and [`SC005`](VerifierDiagnosticCodes.md#sc005) (license ignored) as a **warning**. An author who wants a softer nudge — or stricter enforcement, or a custom-worded message — can override the severity and/or the message text at pack time:

```xml
<PackageReference Include="SponsorCheck" Version="$(SponsorCheckVersion)"
                  PrivateAssets="all"
                  GitHubSponsorsAccount="acmecorp"
                  NoLicenseSpecifiedSeverityOverride="warning"
                  NoLicenseSpecifiedMessageOverride="Please sponsor MyOssLib before shipping."
                  LicenseIgnoredSeverityOverride="error" />
```

Available metadata (severity + message pair per overrideable code). Each override applies to **all** siblings of the same condition — one knob covers the non-CPM, CPM, and owner-mode codes:

| Codes | Severity metadata | Message metadata | Default severity |
| --- | --- | --- | --- |
| [SC001](VerifierDiagnosticCodes.md#sc001) / [SC002](VerifierDiagnosticCodes.md#sc002) / [SC021](VerifierDiagnosticCodes.md#sc021) | `NoLicenseSpecifiedSeverityOverride` | `NoLicenseSpecifiedMessageOverride` | error |
| [SC005](VerifierDiagnosticCodes.md#sc005) / [SC006](VerifierDiagnosticCodes.md#sc006) / [SC023](VerifierDiagnosticCodes.md#sc023) | `LicenseIgnoredSeverityOverride` | `LicenseIgnoredMessageOverride` | warning |
| [SC007](VerifierDiagnosticCodes.md#sc007) / [SC008](VerifierDiagnosticCodes.md#sc008) / [SC024](VerifierDiagnosticCodes.md#sc024) | `InvalidAccountSeverityOverride` | `InvalidAccountMessageOverride` | error |
| [SC009](VerifierDiagnosticCodes.md#sc009) / [SC010](VerifierDiagnosticCodes.md#sc010) / [SC025](VerifierDiagnosticCodes.md#sc025) | `LicenseExpiredSeverityOverride` | `LicenseExpiredMessageOverride` | error |

Severity values: `error`, `warning`, `message`. Message values: any string (the code's short Name still prefixes and the docs link still suffixes). Other codes are consumer-side configuration bugs and aren't overrideable. Unrecognized severity values fail the pack with [SC104](BundlerDiagnosticCodes.md#sc104). The chosen severities and messages are baked into the produced nupkg — consumers can't tamper with them.


## Defining exemptions

Many publishers have legitimate scenarios where a consumer doesn't need to sponsor — consulting clients, pre-existing customers, small businesses below a revenue threshold, etc. Treating those consumers as in breach of the package license (via the [SC005](VerifierDiagnosticCodes.md#sc005)-style warning that follows the build through CI) misrepresents the relationship.

`<SponsorExemption>` items declared next to the SponsorCheck reference let publishers define **named exemptions**, each with the criteria text that describes who qualifies. Consumers claim one by name (see [Publisher-defined exemptions](ConsumerUsage.md#publisher-defined-exemptions) in the consumer guide); the build passes with a warning whose body is the publisher's verbatim criteria text — so the consumer's audit trail documents the specific carve-out being claimed instead of a generic breach.

<!-- snippet: ThePackageWithExemptions.csproj -->
<a id='snippet-ThePackageWithExemptions.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SponsorCheck" Version="$(SponsorCheckVersion)"
                      PrivateAssets="all"
                      GitHubSponsorsAccount="acmecorp"
                      OpenCollectiveAccount="acme-org"
                      PolarAccount="acme" />
    <SponsorExemption Include="Consulting"
                      Message="Organizations that have engaged any of the core maintainers in consulting work could be exempt from the Maintenance Fee for 6 months from the final date of that work." />
    <SponsorExemption Include="SmallRevenue"
                      Message="Consumers under US$10,000 annual gross revenue are exempt." />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/_Shared/ThePackageWithExemptions/ThePackageWithExemptions.csproj#L1-L16' title='Snippet source file'>snippet source</a> | <a href='#snippet-ThePackageWithExemptions.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`Include=` is the exemption name consumers will claim; `Message=` is the criteria text that becomes the warning body. The bundler validates each item at pack time — empty name, empty message, or duplicate names (case-insensitive) fail with [SC106](BundlerDiagnosticCodes.md#sc106). The exemption set is baked into the produced nupkg under `build/SponsorCheck.Exemptions.json`, so consumers can't add new names or change the criteria text.

Exemption warnings ([SC029](VerifierDiagnosticCodes.md#sc029) / [SC030](VerifierDiagnosticCodes.md#sc030) / [SC031](VerifierDiagnosticCodes.md#sc031)) are **not** overrideable via `*MessageOverride` — the publisher's `Message` *is* the override. To change the warning text, edit the `Message` and repack.


## Custom sponsor landing URL

By default, the verifier surfaces each enabled platform's public sponsor page (e.g. `https://github.com/sponsors/acmecorp`, `https://opencollective.com/acme-org`, `https://polar.sh/acme`) wherever a sponsor URL appears in an `SC0xx` message — the per-platform `Option — Sponsor on ...` lines in [SC001](VerifierDiagnosticCodes.md#sc001)/[SC002](VerifierDiagnosticCodes.md#sc002)/[SC005](VerifierDiagnosticCodes.md#sc005)/[SC006](VerifierDiagnosticCodes.md#sc006) and the `Sponsor at ...` block in [SC007](VerifierDiagnosticCodes.md#sc007)/[SC008](VerifierDiagnosticCodes.md#sc008)/[SC009](VerifierDiagnosticCodes.md#sc009)/[SC010](VerifierDiagnosticCodes.md#sc010).

Authors who prefer to drive consumers to a single page they control — e.g. an author-owned "How to sponsor" landing page, an internal CRM, or a Stripe/Lemon Squeezy checkout — can set `SponsorLandingUrl` on the SponsorCheck reference. When set, every URL the verifier prints points at that page instead of the platform-native ones, and the multi-line `Sponsor at:` block collapses to a single `Sponsor at <landing-url>` line:

<!-- snippet: ThePackageLandingUrl.csproj -->
<a id='snippet-ThePackageLandingUrl.csproj'></a>
```csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SponsorCheck" Version="$(SponsorCheckVersion)"
                      PrivateAssets="all"
                      GitHubSponsorsAccount="acmecorp"
                      OpenCollectiveAccount="acme-org"
                      PolarAccount="acme"
                      SponsorLandingUrl="https://acme.example.com/sponsor" />
  </ItemGroup>
</Project>
```
<sup><a href='/IntegrationTests/Fixtures/_Shared/ThePackageLandingUrl/ThePackageLandingUrl.csproj#L1-L13' title='Snippet source file'>snippet source</a> | <a href='#snippet-ThePackageLandingUrl.csproj' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The platform-specific `<Platform>Account` metadata is still required — the bundler uses it to fetch the actual sponsor list from each platform, and the consumer still declares a per-platform `<Platform>SponsorAccount` to match against the bundled hashes. Only the **rendered URLs** in diagnostic messages change; the hash-match logic, license-mode parsing, and platform fetch are unaffected. Like the severity/message overrides, the landing URL is baked into the produced nupkg at pack time — consumers can't tamper with it.


## Sponsor list override (testing & offline builds)

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


## What gets bundled

```mermaid
flowchart TD
    Metadata["SponsorCheck reference metadata<br/>platform accounts, SponsorOwner, CheckTransitiveReferences,<br/>severity/message overrides, exemptions"] --> Bundler[BundleSponsorListTask]
    Credentials["platform credentials<br/>MSBuild property / env var / user-secrets"] --> Bundler
    Platforms["platform APIs<br/>GitHub Sponsors / Open Collective / Polar<br/>or SponsorListOverride JSON"] -->|sponsor accounts| Bundler
    Bundler -->|"accounts hashed —<br/>first 12 hex chars of SHA-256"| Folder
    Bundler --> TaskDll
    subgraph Nupkg["produced nupkg"]
        subgraph Folder["build/ — or buildTransitive/ when CheckTransitiveReferences=true"]
            Hashes["SponsorCheck.SponsorHashes.txt"]
            PackDate["SponsorCheck.PackDate.txt"]
            Accounts["SponsorCheck.AuthorAccounts.txt"]
            Targets["&lt;PackageId&gt;.targets — the generated verifier"]
        end
        TaskDll["tasks/ — SponsorCheck.dll (netstandard2.0 + net472)<br/>unaffected by the folder choice"]
    end
```

The bundler runs at the OSS author's pack time (Release config, `IsPackable=true`). It:

1. Reads `<Platform>Account` metadata from the SponsorCheck `PackageReference` / `PackageVersion`.
1. For each enabled platform, calls the platform's API (or reads `SponsorListOverride` if set) to get the list of sponsor accounts.
1. Hashes each as the first 12 hex chars (48 bits) of `SHA256(utf8("{platform-id}:{lowercase(account)}"))`. Platform-id prefix prevents cross-platform spoofing.
1. Writes four files into the produced nupkg's `build/` folder: the sorted, deduped hashes (`SponsorCheck.SponsorHashes.txt`), the UTC pack date that powers the `SponsorshipStart` bypass (`SponsorCheck.PackDate.txt`), the enabled platform accounts used to render sponsor URLs in diagnostics (`SponsorCheck.AuthorAccounts.txt`), and the per-consumer verifier targets file (`<ThePackageId>.targets`). The verifier task DLL is packed under `tasks/`. When `SponsorOwner` is set, the generated targets are the owner-mode variant — they read global MSBuild properties instead of per-package metadata, with the owner id baked in. When `CheckTransitiveReferences` is set, those `build/` files ship under `buildTransitive/` instead, so NuGet imports the verifier for transitive consumers too (see [Checking transitive references](#checking-transitive-references)).


## Hashing — what it protects

The hash is **light obfuscation, not real privacy.** Anyone with a wordlist of candidate usernames can reverse-engineer the published hashes by recomputing `SHA256("{platform-id}:{lowercase(login)}")` for each candidate and truncating to 12 hex chars. The hash isn't a security boundary either — `SponsorshipLicenseIgnored="true"` is the documented bypass, so anyone wanting to free-ride doesn't need to forge a match.

What hashing actually buys:

1. **Private GitHub sponsors don't ship as plaintext.** GitHub Sponsors lets sponsors opt to be private. The bundler still includes them (the token-owner can see them via the API), and the resulting file lands in every consumer's `~/.nuget/packages/<id>/<ver>/build/` after restore. Hashing means a private sponsor's username is not grep-able across every consumer's disk — an attacker has to specifically guess it and recompute the hash to confirm. Plaintext would effectively dox every private sponsor to every consumer.
1. **Friction against casual scraping.** A flat list of usernames in a published nupkg is a free dataset for anyone running `nuget restore` on public CI. Hashing doesn't stop a determined deanonymizer but does stop incidental harvesting.

If a sponsor needs guarantees stronger than "annoying to reverse" — e.g. they're sponsoring under a pseudonym they're trying to keep separate from their GitHub identity — the OSS author should ask them up front and either skip the bundling entirely or accept the risk of targeted username guesses. The hash is a speed bump, not a wall.

Hash length is truncated to 48 bits (12 hex chars) because the only correctness requirement is "accidental collisions are implausible" — a non-sponsor's hash falsely matching the bundled list is ≈ 1 in tens of billions even at 100k sponsors. Preimage resistance is unnecessary given `SponsorshipLicenseIgnored`.


## Diagnostic codes

[Bundler diagnostic codes (SC1xx)](BundlerDiagnosticCodes.md) — every code the bundler can emit at pack time.
