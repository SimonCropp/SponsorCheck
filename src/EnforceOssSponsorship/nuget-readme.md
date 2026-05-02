# EnforceOssSponsorship

Enforce the [Open Source Maintenance Fee](https://opensourcemaintenancefee.org/) at build time via NuGet — gentle nudging plus honesty rather than runtime DRM.

Add as a development dependency to your library project. At pack time it fetches your sponsor list from the configured platforms (GitHub Sponsors, Open Collective, Polar), hashes the accounts, and bundles a build-time verifier into your produced NuGet package. Consumers of your package then declare one of three license modes per package: explicit ignore (with a build warning), platform sponsorship match, or time-bounded private license.

## OSS author setup

```xml
<PackageReference Include="EnforceOssSponsorship"
                  Version="1.0.0"
                  PrivateAssets="all"
                  IncludeAssets="build;buildTransitive;contentFiles;analyzers"
                  GitHubSponsorsAccount="acmecorp"
                  OpenCollectiveAccount="acme-collective"
                  PolarAccount="acme" />
```

At least one `<Platform>Account` must be set. Credentials per platform come from MSBuild properties / env vars: `GITHUB_TOKEN`, `OPENCOLLECTIVE_API_KEY`, `POLAR_API_KEY` (the last is required).

## Consumer license modes (per package, mutually exclusive)

```xml
<!-- Sponsor match (any-platform: passes if any matches the bundled list) -->
<PackageReference Include="ThePackage" Version="1.0" GitHubSponsorAccount="alice" />

<!-- Explicit time-bounded license -->
<PackageReference Include="ThePackage" Version="1.0" SponsorshipLicensedUntil="2026-12" />

<!-- Escape hatch (passes with warning EOSS003) -->
<PackageReference Include="ThePackage" Version="1.0" SponsorshipIgnored="true" />
```

Verification runs in Release config only.
