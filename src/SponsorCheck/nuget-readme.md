# SponsorCheck

Build-time sponsorship verification for NuGet packages — nudge consumers of an OSS library to sponsor its author, in the spirit of the [Open Source Maintenance Fee](https://opensourcemaintenancefee.org/). Gentle nudging plus honesty rather than runtime DRM.

Add as a development dependency to the library project. At pack time it fetches the author's sponsor list from the configured platforms (GitHub Sponsors, Open Collective, Polar), hashes the accounts, and bundles a build-time verifier into the produced NuGet package. Consumers of the package then declare one of three license modes per package: explicit ignore (with a build warning), platform sponsorship match, or time-bounded private license.

## OSS author setup

```xml
<PackageReference Include="SponsorCheck"
                  Version="1.0.0"
                  PrivateAssets="all"
                  GitHubSponsorsAccount="acmecorp"
                  OpenCollectiveAccount="acme-collective"
                  PolarAccount="acme" />
```

At least one `<Platform>Account` must be set. Credentials per platform come from MSBuild properties (or env vars of the same name, which MSBuild auto-imports): `GitHubToken` (required), `OpenCollectiveToken`, `PolarToken` (required). Locally, prefer `dotnet user-secrets set SponsorCheck:<Platform>Token`.

## Consumer license modes (per package, mutually exclusive)

```xml
<!-- Sponsor match (any-platform: passes if any matches the bundled list) -->
<PackageReference Include="ThePackage" Version="1.0" GitHubSponsorAccount="alice" />

<!-- Explicit time-bounded license -->
<PackageReference Include="ThePackage" Version="1.0" SponsorshipLicensedUntil="2026-12" />

<!-- Escape hatch (passes with warning SC005) -->
<PackageReference Include="ThePackage" Version="1.0" SponsorshipLicenseIgnored="true" />
```

Verification runs in Release config only.
