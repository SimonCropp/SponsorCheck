# Verifier diagnostic codes

Codes in the `SC0xx` range are emitted by the verifier in consumer projects. Pairs are interleaved by mode: **odd-numbered codes** fire when the consumer is **not** using Central Package Management (metadata lives on `<PackageReference>` in the consumer csproj); **even-numbered codes** fire when the consumer **is** using CPM (metadata lives on `<PackageVersion>` in `Directory.Packages.props`). Sibling = `code ± 1` — SC001/SC002, SC003/SC004, SC011/SC012, and so on. The trailing SC017–SC020 codes are unpaired (audit message, install-integrity check, and the two placement errors).

**Owner mode (SC021–SC028).** When the OSS author opts a package into *owner mode* (`SponsorOwner="<id>"` at pack time), the consumer configures sponsorship **once**, via global MSBuild **properties** (in `Directory.Build.props` or the consuming project), rather than per-package metadata. Because there is a single property source — no `<PackageReference>`/`<PackageVersion>` split — each scenario needs only one code, so SC021–SC028 are **unpaired** owner-mode counterparts of the per-package family: SC021↔SC001/SC002 (no config), SC022↔SC003/SC004 (conflicting modes), SC023↔SC005/SC006 (ignored), SC024↔SC007/SC008 (invalid account), SC025↔SC009/SC010 (expired), SC026↔SC011/SC012 (bad license date), SC027↔SC013/SC014 (bad SponsorshipStart), SC028↔SC015/SC016 (future SponsorshipStart). The placement-agnostic SC017 (attestation) and SC018 (missing hash file) are reused as-is; SC019/SC020 do not apply (no items to misplace).

Each paired scenario shares one author-side override metadatum: `NoLicenseSpecifiedSeverityOverride` applies to SC001, SC002, **and** the owner-mode SC021, and similarly for the rest. The split exists so each code can be documented, linked, and triaged independently — the underlying scenario is the same.

Every emitted message is prefixed with the code's short **Name** (e.g. `No license specified. Package 'MyOssLib'...`) and suffixed with ` See: https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#<code>`. The Syntax/Example entries below show the inner format string only — the name and link wrap is added at log time.

The default severities below can be overridden by the OSS author at pack time, and the message body can be replaced with custom text, via paired metadata on `<PackageReference Include="SponsorCheck">`: `<Stem>SeverityOverride` and `<Stem>MessageOverride` for each of `NoLicenseSpecified` (SC001/SC002/SC021), `LicenseIgnored` (SC005/SC006/SC023), `InvalidAccount` (SC007/SC008/SC024), `LicenseExpired` (SC009/SC010/SC025). A single override value applies across the per-package, CPM, and owner-mode siblings. Other codes are consumer-side configuration bugs that the consumer must fix and so cannot be tuned. Severity values: `error`, `warning`, `message`. Message values: any string (the code's short Name and the docs link wrap still apply).

<!-- include: verifier-flow. path: /docs/verifier-flow.include.md -->
```mermaid
flowchart TD
    Start([Consumer build]) --> Which{Which mode?}

    Which -->|Ignored| SC005[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc005'>SC005 Warning<br/>In breach of license</a>]

    Which -->|Supplied sponsor account| HasStart{Sponsorship<br/>Start set?}
    HasStart -->|Yes| Future{Start in<br/>future?}
    Future -->|Yes| SC015[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc015'>SC015 Error<br/>Date in future</a>]
    Future -->|No| AfterPack{Start &gt;<br/>PackDate?}
    AfterPack -->|Yes| PassAttest([<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc017'>Build passes<br/>SC017 audit message</a>])
    AfterPack -->|No| Match
    HasStart -->|No| Match
    Match{Supplied account<br/>exists in hash list?}
    Match -->|Yes| PassSponsor([Build passes])
    Match -->|No| SC007[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc007'>SC007 Error<br/>Account is not licensed for usage</a>]

    Which -->|Licensed Until| ParseYM{Valid<br/>yyyy-MM?}
    ParseYM -->|No| SC011[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc011'>SC011 Error<br/>Invalid date format</a>]
    ParseYM -->|Yes| Expired{End of month<br/>in the past?}
    Expired -->|Yes| SC009[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc009'>SC009 Error<br/>License expired</a>]
    Expired -->|No| PassLicense([Build passes])
```

> The decision logic above is identical across all three placements; only the emitted code differs. Terminal codes shown are the non-CPM (`<PackageReference>`) variants. A CPM consumer (`<PackageVersion>` in `Directory.Packages.props`) emits the `+1` sibling of each ([SC005](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc005)→[SC006](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc006), [SC007](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc007)→[SC008](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc008), …). An [owner-mode](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc021) consumer (sponsorship set via a global MSBuild property) emits the SC021–SC028 equivalent (Ignored→[SC023](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc023), no match→[SC024](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc024), expired→[SC025](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc025), invalid date→[SC026](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc026), future start→[SC028](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc028); [SC017](https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc017) is shared).
<!-- endInclude -->

### SC001

- **Name:** No license specified
- **Level**: Error
- **Meaning:** No license mode set on the `<PackageReference>` for ThePackage. CPM equivalent: [SC002](#sc002).
- **Syntax:**

  ```
  Package '{PackageId}' requires license metadata on the <PackageReference> for '{PackageId}'.

  Add ONE of the following attributes to the existing <PackageReference> for '{PackageId}' in:
    {csprojPath}

  Option — Sponsor on {PlatformName} ({sponsorUrl}):
    <PackageReference Include="{PackageId}" Version="{version}" {PlatformMetadataName}="<your-{platform}-account>" />

  Option — Time-bounded license (replace yyyy-MM with the last covered month):
    <PackageReference Include="{PackageId}" Version="{version}" SponsorshipLicensedUntil="yyyy-MM" />

  Option — Mark as ignored (you accept that the build is in breach of the package license):
    <PackageReference Include="{PackageId}" Version="{version}" SponsorshipLicenseIgnored="true" />

  Sponsor at:
    {sponsorUrls}
  ```

  One "Sponsor on..." option is rendered per platform the author has enabled. SponsorshipLicenseIgnored is listed last so the breach-of-license escape hatch sits after the legitimate options. When only one platform is configured the "Sponsor at:" block collapses to a single inline line: `Sponsor at {sponsorUrl}`.
- **Example:**

  ```
  Package 'MyOssLib' requires license metadata on the <PackageReference> for 'MyOssLib'.

  Add ONE of the following attributes to the existing <PackageReference> for 'MyOssLib' in:
    /work/MyApp/MyApp.csproj

  Option — Sponsor on GitHub Sponsors (https://github.com/sponsors/acmecorp):
    <PackageReference Include="MyOssLib" Version="1.2.3" GitHubSponsorAccount="<your-github-account>" />

  Option — Time-bounded license (replace yyyy-MM with the last covered month):
    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipLicensedUntil="yyyy-MM" />

  Option — Mark as ignored (you accept that the build is in breach of the package license):
    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipLicenseIgnored="true" />

  Sponsor at https://github.com/sponsors/acmecorp
  ```


### SC002

- **Name:** No license specified
- **Level**: Error
- **Meaning:** CPM sibling of [SC001](#sc001): no license mode set on the `<PackageVersion>` for ThePackage in `Directory.Packages.props`. Body shape matches SC001 with `<PackageVersion>` in place of `<PackageReference>` and the props file path in place of the csproj path.
- **Example opener:** `Package 'MyOssLib' requires license metadata on the <PackageVersion> for 'MyOssLib' in Directory.Packages.props.`


### SC003

- **Name:** Conflicting license modes
- **Level**: Error
- **Meaning:** Multiple license modes set on the same `<PackageReference>` (mutually exclusive). CPM equivalent: [SC004](#sc004).
- **Syntax:**

  ```
  Package '{PackageId}': mutually exclusive license modes are set on the <PackageReference> ({modes}). Pick one.

  Edit the <PackageReference> for '{PackageId}' in:
    {csprojPath}

  Keep exactly one of: GitHubSponsorAccount, OpenCollectiveSponsorAccount, PolarSponsorAccount, SponsorshipLicensedUntil, or SponsorshipLicenseIgnored.
  ```
- **Example:**

  ```
  Package 'MyOssLib': mutually exclusive license modes are set on the <PackageReference> (Sponsor, SponsorshipLicenseIgnored). Pick one.

  Edit the <PackageReference> for 'MyOssLib' in:
    /work/MyApp/MyApp.csproj

  Keep exactly one of: GitHubSponsorAccount, OpenCollectiveSponsorAccount, PolarSponsorAccount, SponsorshipLicensedUntil, or SponsorshipLicenseIgnored.
  ```


### SC004

- **Name:** Conflicting license modes
- **Level**: Error
- **Meaning:** CPM sibling of [SC003](#sc003): mutually exclusive license modes set on the same `<PackageVersion>` in `Directory.Packages.props`.
- **Example opener:** `Package 'MyOssLib': mutually exclusive license modes are set on the <PackageVersion> in Directory.Packages.props (Sponsor, SponsorshipLicenseIgnored). Pick one.`


### SC005

- **Name:** License ignored
- **Level**: Warning
- **Meaning:** `SponsorshipLicenseIgnored="true"` on the `<PackageReference>` — consumer has opted out. CPM equivalent: [SC006](#sc006).
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicenseIgnored="true" on the <PackageReference>. Build is allowed but is in breach of the package license.

  <SC001-style remediation block — see SC001 for the full format>
  ```
- **Example:** Same body shape as SC001, prefixed with `Package 'MyOssLib': SponsorshipLicenseIgnored="true" on the <PackageReference>. Build is allowed but is in breach of the package license.`


### SC006

- **Name:** License ignored
- **Level**: Warning
- **Meaning:** CPM sibling of [SC005](#sc005): `SponsorshipLicenseIgnored="true"` on the `<PackageVersion>` in `Directory.Packages.props`.
- **Example opener:** `Package 'MyOssLib': SponsorshipLicenseIgnored="true" on the <PackageVersion> in Directory.Packages.props. Build is allowed but is in breach of the package license.`


### SC007

- **Name:** Invalid account
- **Level**: Error
- **Meaning:** None of the sponsor accounts declared on the `<PackageReference>` match the bundled hash list. CPM equivalent: [SC008](#sc008).
- **Syntax:**

  ```
  Package '{PackageId}': no sponsor account declared on the <PackageReference> matches the bundled list.

  Tried: {attempts}

  Sponsor at:
    {sponsorUrl1}
    {sponsorUrl2}
    ...

  If sponsorship started after this package was released, attest to the start date in:

    {csprojPath}

  Example format:

    <PackageReference Include="{PackageId}" Version="{version}" {PlatformMetadataName}="{accountValue}" SponsorshipStart="yyyy-MM-dd" />
  ```

  The "Sponsor at:" block is omitted when the author did not bundle any platform accounts. When only one platform is configured it collapses to a single inline line: `Sponsor at {sponsorUrl}`. The "If sponsorship started after this package was released..." block is omitted when `SponsorshipStart` is already set on the consumer side.
- **Example:**

  ```
  Package 'MyOssLib': no sponsor account declared on the <PackageReference> matches the bundled list.

  Tried: GitHubSponsors=mallory

  Sponsor at:
    https://github.com/sponsors/acmecorp
    https://opencollective.com/acme-org
    https://polar.sh/acme

  If sponsorship started after this package was released, attest to the start date in:

    /work/MyApp/MyApp.csproj

  Example format:

    <PackageReference Include="MyOssLib" Version="1.2.3" GitHubSponsorAccount="mallory" SponsorshipStart="yyyy-MM-dd" />
  ```


### SC008

- **Name:** Invalid account
- **Level**: Error
- **Meaning:** CPM sibling of [SC007](#sc007): no sponsor account declared on the `<PackageVersion>` in `Directory.Packages.props` matches the bundled hash list.
- **Example opener:** `Package 'MyOssLib': no sponsor account declared on the <PackageVersion> in Directory.Packages.props matches the bundled list.`


### SC009

- **Name:** License expired
- **Level**: Error
- **Meaning:** `SponsorshipLicensedUntil` on the `<PackageReference>` has expired. CPM equivalent: [SC010](#sc010).
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicensedUntil='{value}' on the <PackageReference> has expired (end of month {endOfMonth:yyyy-MM-dd} UTC).

  Renew the license in:

    {csprojPath}

  Example format:

    <PackageReference Include="{PackageId}" Version="{version}" SponsorshipLicensedUntil="yyyy-MM" />

  Sponsor at {sponsorUrl}     ← single-platform inline form
  ```

  Or as an alternative to renewal, the consumer can switch to sponsorship — the "Sponsor at" block lists the author's configured sponsor URLs (inline for one platform, indented `Sponsor at:\n  url1\n  url2` block for multiple).
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipLicensedUntil='2000-01' on the <PackageReference> has expired (end of month 2000-01-31 UTC).

  Renew the license in:

    /work/MyApp/MyApp.csproj

  Example format:

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipLicensedUntil="yyyy-MM" />

  Sponsor at https://github.com/sponsors/acmecorp
  ```


### SC010

- **Name:** License expired
- **Level**: Error
- **Meaning:** CPM sibling of [SC009](#sc009): `SponsorshipLicensedUntil` on the `<PackageVersion>` in `Directory.Packages.props` has expired.
- **Example opener:** `Package 'MyOssLib': SponsorshipLicensedUntil='2000-01' on the <PackageVersion> in Directory.Packages.props has expired (end of month 2000-01-31 UTC).`


### SC019

- **Name:** Metadata set on both PackageReference and PackageVersion
- **Level**: Error
- **Meaning:** Defensive backstop: metadata set on both PackageReference and PackageVersion. SC020 normally fires first now (because the wrong-side metadatum is itself a placement violation) — SC019 only surfaces if SC020's check is bypassed.
- **Syntax:** `{metadataName}: set on both PackageReference ('{r}') and PackageVersion ('{v}'). Set on only one.`
- **Example:** `GitHubSponsorAccount: set on both PackageReference ('alice') and PackageVersion ('bob'). Set on only one.`


### SC011

- **Name:** Invalid license date format
- **Level**: Error
- **Meaning:** `SponsorshipLicensedUntil` on a `<PackageReference>` is not in `yyyy-MM` format. The CPM equivalent is [SC012](#sc012).
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicensedUntil='{value}' on the <PackageReference> is not in 'yyyy-MM' format.

  Fix the SponsorshipLicensedUntil attribute in:

    {csprojPath}

  Example format:

    <PackageReference Include="{PackageId}" Version="{version}" SponsorshipLicensedUntil="yyyy-MM" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipLicensedUntil='not-a-date' on the <PackageReference> is not in 'yyyy-MM' format.

  Fix the SponsorshipLicensedUntil attribute in:

    /work/MyApp/MyApp.csproj

  Example format:

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipLicensedUntil="yyyy-MM" />
  ```


### SC012

- **Name:** Invalid license date format
- **Level**: Error
- **Meaning:** `SponsorshipLicensedUntil` on a `<PackageVersion>` (Central Package Management) is not in `yyyy-MM` format. The non-CPM equivalent is [SC011](#sc011).
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicensedUntil='{value}' on the <PackageVersion> in Directory.Packages.props is not in 'yyyy-MM' format.

  Fix the SponsorshipLicensedUntil attribute in:

    {directoryPackagesPropsPath}

  Example format:

    <PackageVersion Include="{PackageId}" Version="{version}" SponsorshipLicensedUntil="yyyy-MM" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipLicensedUntil='not-a-date' on the <PackageVersion> in Directory.Packages.props is not in 'yyyy-MM' format.

  Fix the SponsorshipLicensedUntil attribute in:

    /work/MyApp/Directory.Packages.props

  Example format:

    <PackageVersion Include="MyOssLib" Version="1.2.3" SponsorshipLicensedUntil="yyyy-MM" />
  ```


### SC017

- **Name:** Sponsorship attestation trusted
- **Level**: Info
- **Meaning:** `SponsorshipStart` is after pack date — verifier trusts the attestation (audit trail message).
- **Syntax:** `Package '{PackageId}': trusting unverified sponsor declaration ({attempts}): SponsorshipStart={startDate:yyyy-MM-dd} is later than package release {packDate:yyyy-MM-dd}, so the bundled sponsor list cannot contain this account.`
- **Example:** `Package 'MyOssLib': trusting unverified sponsor declaration (GitHubSponsors=carol): SponsorshipStart=2026-04-30 is later than package release 2026-04-15, so the bundled sponsor list cannot contain this account.`


### SC018

- **Name:** Bundled sponsor hash file missing
- **Level**: Error
- **Meaning:** Bundled sponsor hash file is missing from the package (corrupt install).
- **Syntax:** `Package '{PackageId}': bundled sponsor hash file not found at '{path}'.`
- **Example:** `Package 'MyOssLib': bundled sponsor hash file not found at 'C:\Users\me\.nuget\packages\myosslib\1.0.0\build\SponsorCheck.SponsorHashes.txt'.`


### SC013

- **Name:** Invalid SponsorshipStart format
- **Level**: Error
- **Meaning:** `SponsorshipStart` on the `<PackageReference>` is not in `yyyy-MM-dd` format. CPM equivalent: [SC014](#sc014).
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipStart='{value}' on the <PackageReference> is not in 'yyyy-MM-dd' format.

  Fix the SponsorshipStart attribute in:

    {csprojPath}

  Example format:

    <PackageReference Include="{PackageId}" Version="{version}" SponsorshipStart="yyyy-MM-dd" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipStart='yesterday' on the <PackageReference> is not in 'yyyy-MM-dd' format.

  Fix the SponsorshipStart attribute in:

    /work/MyApp/MyApp.csproj

  Example format:

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipStart="yyyy-MM-dd" />
  ```


### SC014

- **Name:** Invalid SponsorshipStart format
- **Level**: Error
- **Meaning:** CPM sibling of [SC013](#sc013): `SponsorshipStart` on the `<PackageVersion>` in `Directory.Packages.props` is not in `yyyy-MM-dd` format.
- **Example opener:** `Package 'MyOssLib': SponsorshipStart='yesterday' on the <PackageVersion> in Directory.Packages.props is not in 'yyyy-MM-dd' format.`


### SC015

- **Name:** SponsorshipStart in the future
- **Level**: Error
- **Meaning:** `SponsorshipStart` on the `<PackageReference>` is in the future. CPM equivalent: [SC016](#sc016).
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipStart='{value}' on the <PackageReference> is in the future.

  Fix the SponsorshipStart attribute in:

    {csprojPath}

  Example format:

    <PackageReference Include="{PackageId}" Version="{version}" SponsorshipStart="yyyy-MM-dd" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipStart='2099-01-01' on the <PackageReference> is in the future.

  Fix the SponsorshipStart attribute in:

    /work/MyApp/MyApp.csproj

  Example format:

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipStart="yyyy-MM-dd" />
  ```


### SC016

- **Name:** SponsorshipStart in the future
- **Level**: Error
- **Meaning:** CPM sibling of [SC015](#sc015): `SponsorshipStart` on the `<PackageVersion>` in `Directory.Packages.props` is in the future.
- **Example opener:** `Package 'MyOssLib': SponsorshipStart='2099-01-01' on the <PackageVersion> in Directory.Packages.props is in the future.`


### SC020

- **Name:** Sponsor metadata in the wrong location
- **Level**: Error
- **Meaning:** Under Central Package Management (`ManagePackageVersionsCentrally=true`) the SponsorCheck metadata must live on `<PackageVersion>` in `Directory.Packages.props`, not on `<PackageReference>` in the consumer's csproj. The mirror also holds: without CPM the metadata must live on `<PackageReference>`, not on `<PackageVersion>`.
- **Syntax (multiple misplaced attributes):**

  ```
  Package '{PackageId}' uses Central Package Management, so SponsorCheck metadata must live on <PackageVersion> in Directory.Packages.props — not on <PackageReference>.

  Move the following attributes off the <PackageReference> for '{PackageId}'
    in: {csprojPath}
    - {misplacedMetadata1}
    - {misplacedMetadata2}

  ...and onto the <PackageVersion> for '{PackageId}' in:
    {directoryPackagesPropsPath}
  ```

  The first sentence inverts (`is not using` / `<PackageReference>` ↔ `<PackageVersion>` ↔ `Directory.Packages.props` ↔ csproj) when CPM is off.
- **Syntax (single misplaced attribute):**

  ```
  Package '{PackageId}' uses Central Package Management, so SponsorCheck metadata must live on <PackageVersion> in Directory.Packages.props — not on <PackageReference>.

  Move the {misplacedMetadata} attribute from the <PackageReference> for '{PackageId}' to the <PackageVersion> for '{PackageId}' in:
    {directoryPackagesPropsPath}
  ```
- **Example (single attribute):**

  ```
  Package 'MyOssLib' uses Central Package Management, so SponsorCheck metadata must live on <PackageVersion> in Directory.Packages.props — not on <PackageReference>.

  Move the SponsorshipLicenseIgnored attribute from the <PackageReference> for 'MyOssLib' to the <PackageVersion> for 'MyOssLib' in:
    /work/MyApp/Directory.Packages.props
  ```


### SC021

- **Name:** No license specified
- **Level**: Error
- **Meaning:** Owner-mode equivalent of [SC001](#sc001)/[SC002](#sc002): the package is published in owner mode (`SponsorOwner`), so sponsorship is configured via a global MSBuild property, but none was set. Author override metadatum: `NoLicenseSpecifiedSeverityOverride` / `NoLicenseSpecifiedMessageOverride`.
- **Syntax:**

  ```
  Package '{PackageId}' requires a SponsorCheck license property. '{PackageId}' is published in owner mode, so sponsorship is configured once via an MSBuild property (in Directory.Build.props or the consuming project).

  Set ONE of the following properties (in a <PropertyGroup> in Directory.Build.props or the consuming project):

  Option — Sponsor on {PlatformName} ({sponsorUrl}):
    <{PlatformMetadataName}><your-{platform}-account></{PlatformMetadataName}>

  Option — Time-bounded license (replace yyyy-MM with the last covered month):
    <SponsorshipLicensedUntil>yyyy-MM</SponsorshipLicensedUntil>

  Option — Mark as ignored (you accept that the build is in breach of the package license):
    <SponsorshipLicenseIgnored>true</SponsorshipLicenseIgnored>

  Sponsor at:
    {sponsorUrls}
  ```

  One "Sponsor on..." option is rendered per platform the author has enabled. The property names match the per-package metadata names, used here as MSBuild properties. The "Sponsor at:" block collapses to a single inline line when only one platform is configured.
- **Example:**

  ```
  Package 'MyOssLib' requires a SponsorCheck license property. 'MyOssLib' is published in owner mode, so sponsorship is configured once via an MSBuild property (in Directory.Build.props or the consuming project).

  Set ONE of the following properties (in a <PropertyGroup> in Directory.Build.props or the consuming project):

  Option — Sponsor on GitHub Sponsors (https://github.com/sponsors/acmecorp):
    <GitHubSponsorAccount><your-github-account></GitHubSponsorAccount>

  Option — Time-bounded license (replace yyyy-MM with the last covered month):
    <SponsorshipLicensedUntil>yyyy-MM</SponsorshipLicensedUntil>

  Option — Mark as ignored (you accept that the build is in breach of the package license):
    <SponsorshipLicenseIgnored>true</SponsorshipLicenseIgnored>

  Sponsor at https://github.com/sponsors/acmecorp
  ```


### SC022

- **Name:** Conflicting license modes
- **Level**: Error
- **Meaning:** Owner-mode equivalent of [SC003](#sc003)/[SC004](#sc004): mutually exclusive license properties are set (e.g. both a sponsor account property and `SponsorshipLicenseIgnored`).
- **Syntax:**

  ```
  Package '{PackageId}': mutually exclusive license properties are set ({modes}). Pick one.

  Edit the SponsorCheck properties for '{PackageId}' in Directory.Build.props or the consuming project.

  Keep exactly one of: GitHubSponsorAccount, OpenCollectiveSponsorAccount, PolarSponsorAccount, SponsorshipLicensedUntil, or SponsorshipLicenseIgnored.
  ```
- **Example opener:** `Package 'MyOssLib': mutually exclusive license properties are set (Sponsor, SponsorshipLicenseIgnored). Pick one.`


### SC023

- **Name:** License ignored
- **Level**: Warning
- **Meaning:** Owner-mode equivalent of [SC005](#sc005)/[SC006](#sc006): the `SponsorshipLicenseIgnored=true` property is set — the consumer has opted out. Author override metadatum: `LicenseIgnoredSeverityOverride` / `LicenseIgnoredMessageOverride`.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicenseIgnored="true" property is set. Build is allowed but is in breach of the package license.

  <SC021-style remediation block — see SC021 for the full format>
  ```
- **Example opener:** `Package 'MyOssLib': SponsorshipLicenseIgnored="true" property is set. Build is allowed but is in breach of the package license.`


### SC024

- **Name:** Invalid account
- **Level**: Error
- **Meaning:** Owner-mode equivalent of [SC007](#sc007)/[SC008](#sc008): no sponsor account property matches the bundled hash list. Author override metadatum: `InvalidAccountSeverityOverride` / `InvalidAccountMessageOverride`.
- **Syntax:**

  ```
  Package '{PackageId}': no sponsor account property matches the bundled list.

  Tried: {attempts}

  Sponsor at:
    {sponsorUrl1}
    {sponsorUrl2}

  If sponsorship started after this package was released, attest to the start date by setting the SponsorshipStart property in Directory.Build.props or the consuming project.

  Example format:

    <{PlatformMetadataName}>{accountValue}</{PlatformMetadataName}>
    <SponsorshipStart>yyyy-MM-dd</SponsorshipStart>
  ```

  The "If sponsorship started after this package was released..." block is omitted when `SponsorshipStart` is already set.
- **Example:**

  ```
  Package 'MyOssLib': no sponsor account property matches the bundled list.

  Tried: GitHubSponsors=mallory

  Sponsor at https://github.com/sponsors/acmecorp

  If sponsorship started after this package was released, attest to the start date by setting the SponsorshipStart property in Directory.Build.props or the consuming project.

  Example format:

    <GitHubSponsorAccount>mallory</GitHubSponsorAccount>
    <SponsorshipStart>yyyy-MM-dd</SponsorshipStart>
  ```


### SC025

- **Name:** License expired
- **Level**: Error
- **Meaning:** Owner-mode equivalent of [SC009](#sc009)/[SC010](#sc010): the `SponsorshipLicensedUntil` property has expired. Author override metadatum: `LicenseExpiredSeverityOverride` / `LicenseExpiredMessageOverride`.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicensedUntil='{value}' property has expired (end of month {endOfMonth:yyyy-MM-dd} UTC).

  Renew the license by updating the SponsorshipLicensedUntil property in Directory.Build.props or the consuming project.

  Example format:

    <SponsorshipLicensedUntil>yyyy-MM</SponsorshipLicensedUntil>

  Sponsor at {sponsorUrl}
  ```
- **Example opener:** `Package 'MyOssLib': SponsorshipLicensedUntil='2000-01' property has expired (end of month 2000-01-31 UTC).`


### SC026

- **Name:** Invalid license date format
- **Level**: Error
- **Meaning:** Owner-mode equivalent of [SC011](#sc011)/[SC012](#sc012): the `SponsorshipLicensedUntil` property is not in `yyyy-MM` format.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicensedUntil='{value}' property is not in 'yyyy-MM' format.

  Fix the SponsorshipLicensedUntil property in Directory.Build.props or the consuming project.

  Example format:

    <SponsorshipLicensedUntil>yyyy-MM</SponsorshipLicensedUntil>
  ```
- **Example opener:** `Package 'MyOssLib': SponsorshipLicensedUntil='not-a-date' property is not in 'yyyy-MM' format.`


### SC027

- **Name:** Invalid SponsorshipStart format
- **Level**: Error
- **Meaning:** Owner-mode equivalent of [SC013](#sc013)/[SC014](#sc014): the `SponsorshipStart` property is not in `yyyy-MM-dd` format.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipStart='{value}' property is not in 'yyyy-MM-dd' format.

  Fix the SponsorshipStart property in Directory.Build.props or the consuming project.

  Example format:

    <SponsorshipStart>yyyy-MM-dd</SponsorshipStart>
  ```
- **Example opener:** `Package 'MyOssLib': SponsorshipStart='yesterday' property is not in 'yyyy-MM-dd' format.`


### SC028

- **Name:** SponsorshipStart in the future
- **Level**: Error
- **Meaning:** Owner-mode equivalent of [SC015](#sc015)/[SC016](#sc016): the `SponsorshipStart` property is in the future.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipStart='{value}' property is in the future.

  Fix the SponsorshipStart property in Directory.Build.props or the consuming project.

  Example format:

    <SponsorshipStart>yyyy-MM-dd</SponsorshipStart>
  ```
- **Example opener:** `Package 'MyOssLib': SponsorshipStart='2099-01-01' property is in the future.`
