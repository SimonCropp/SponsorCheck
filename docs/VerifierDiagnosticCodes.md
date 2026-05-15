# Verifier diagnostic codes

Codes in the `SC0xx` range are emitted by the verifier in consumer projects.

Every emitted message is prefixed with the code's short **Name** (e.g. `No license specified. Package 'MyOssLib'...`) and suffixed with ` See: https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#<code>`. The Syntax/Example entries below show the inner format string only — the name and link wrap is added at log time.

The default severities below can be overridden by the OSS author at pack time, and the message body can be replaced with custom text, via paired metadata on `<PackageReference Include="SponsorCheck">`: `<Code>SeverityOverride` and `<Code>MessageOverride` for each of `NoLicenseSpecified` (SC001), `LicenseIgnored` (SC003), `InvalidAccount` (SC004), `LicenseExpired` (SC005). Other codes are consumer-side configuration bugs that the consumer must fix and so cannot be tuned. Severity values: `error`, `warning`, `message`. Message values: any string (the code's short Name and the docs link wrap still apply).

<!-- include: verifier-flow. path: /docs/verifier-flow.include.md -->
```mermaid
flowchart TD
    Start([Consumer build]) --> Which{Which mode?}

    Which -->|Ignored| SC003[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc003'>SC003 Warning<br/>In breach of license</a>]

    Which -->|Supplied sponsor account| HasStart{Sponsorship<br/>Start set?}
    HasStart -->|Yes| Future{Start in<br/>future?}
    Future -->|Yes| SC011[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc011'>SC011 Error<br/>Date in future</a>]
    Future -->|No| AfterPack{Start &gt;<br/>PackDate?}
    AfterPack -->|Yes| PassAttest([<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc008'>Build passes<br/>SC008 audit message</a>])
    AfterPack -->|No| Match
    HasStart -->|No| Match
    Match{Supplied account<br/>exists in hash list?}
    Match -->|Yes| PassSponsor([Build passes])
    Match -->|No| SC004[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc004'>SC004 Error<br/>Account is not licensed for usage</a>]

    Which -->|Licensed Until| ParseYM{Valid<br/>yyyy-MM?}
    ParseYM -->|No| SC007[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc007'>SC007 Error<br/>Invalid date format</a>]
    ParseYM -->|Yes| Expired{End of month<br/>in the past?}
    Expired -->|Yes| SC005[<a href='https://github.com/SimonCropp/SponsorCheck/blob/main/docs/VerifierDiagnosticCodes.md#sc005'>SC005 Error<br/>License expired</a>]
    Expired -->|No| PassLicense([Build passes])
```
<!-- endInclude -->

### SC001

- **Name:** No license specified
- **Level**: Error
- **Meaning:** No license mode set on the PackageReference / PackageVersion.
- **Syntax:**

  ```
  Package '{PackageId}' is built with SponsorCheck and requires license metadata applied to the {Element}.

  Add ONE of the following attributes to the existing <{Element}> for '{PackageId}' in:
    {targetFile}

  Option — Mark as ignored (you accept that the build is in breach of the package license):
    <{Element} Include="{PackageId}" Version="{version}" SponsorshipLicenseIgnored="true" />

  Option — Sponsor on {PlatformName} ({sponsorUrl}):
    <{Element} Include="{PackageId}" Version="{version}" {PlatformMetadataName}="<your-{platform}-account>" />

  Option — Time-bounded license (replace yyyy-MM with the last covered month):
    <{Element} Include="{PackageId}" Version="{version}" SponsorshipLicensedUntil="yyyy-MM" />

  Sponsor at:
    {sponsorUrls}
  ```

  `{Element}` is `PackageReference` (no CPM) or `PackageVersion` (CPM). `{targetFile}` is the consumer csproj or `Directory.Packages.props`. One "Sponsor on..." option is rendered per platform the author has enabled.
- **Example:**

  ```
  Package 'MyOssLib' is built with SponsorCheck and requires license metadata applied to the PackageVersion.

  Add ONE of the following attributes to the existing <PackageVersion> for 'MyOssLib' in:
    /work/MyApp/Directory.Packages.props

  Option — Mark as ignored (you accept that the build is in breach of the package license):
    <PackageVersion Include="MyOssLib" Version="1.2.3" SponsorshipLicenseIgnored="true" />

  Option — Sponsor on GitHub Sponsors (https://github.com/sponsors/acmecorp):
    <PackageVersion Include="MyOssLib" Version="1.2.3" GitHubSponsorAccount="<your-github-account>" />

  Option — Time-bounded license (replace yyyy-MM with the last covered month):
    <PackageVersion Include="MyOssLib" Version="1.2.3" SponsorshipLicensedUntil="yyyy-MM" />

  Sponsor at:
    https://github.com/sponsors/acmecorp
  ```


### SC002

- **Name:** Conflicting license modes
- **Level**: Error
- **Meaning:** Multiple license modes set (mutually exclusive).
- **Syntax:**

  ```
  Package '{PackageId}': mutually exclusive license modes are set ({modes}). Pick one.

  Edit the <{Element}> for '{PackageId}' in:
    {targetFile}

  Keep exactly one of: SponsorshipLicenseIgnored, a <Platform>SponsorAccount, or SponsorshipLicensedUntil.
  ```
- **Example:**

  ```
  Package 'MyOssLib': mutually exclusive license modes are set (SponsorshipLicenseIgnored, Sponsor). Pick one.

  Edit the <PackageReference> for 'MyOssLib' in:
    /work/MyApp/MyApp.csproj

  Keep exactly one of: SponsorshipLicenseIgnored, a <Platform>SponsorAccount, or SponsorshipLicensedUntil.
  ```


### SC003

- **Name:** License ignored
- **Level**: Warning
- **Meaning:** `SponsorshipLicenseIgnored="true"` — consumer has opted out.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicenseIgnored="true". Build is allowed but is in breach of the package license.

  <SC001-style remediation block — see SC001 for the full format>
  ```
- **Example:** Same body shape as SC001 (rendered with the consumer's actual `<PackageReference>` / `<PackageVersion>`, target file, and one "Sponsor on..." line per enabled platform), prefixed with `Package 'MyOssLib': SponsorshipLicenseIgnored="true". Build is allowed but is in breach of the package license.`


### SC004

- **Name:** Invalid account
- **Level**: Error
- **Meaning:** None of the supplied platform accounts match the bundled hash list.
- **Syntax:**

  ```
  Package '{PackageId}': no supplied sponsor account matches the bundled list.

  Tried: {attempts}

  If sponsorship started after this package was released, attest to the start date in:
    {targetFile}

    <{Element} Include="{PackageId}" Version="{version}" {PlatformMetadataName}="{accountValue}" SponsorshipStart="yyyy-MM-dd" />
  ```

  The "If sponsorship started after this package was released..." block is omitted when `SponsorshipStart` is already set on the consumer side.
- **Example:**

  ```
  Package 'MyOssLib': no supplied sponsor account matches the bundled list.

  Tried: GitHubSponsors=mallory

  If sponsorship started after this package was released, attest to the start date in:
    /work/MyApp/MyApp.csproj

    <PackageReference Include="MyOssLib" Version="1.2.3" GitHubSponsorAccount="mallory" SponsorshipStart="yyyy-MM-dd" />
  ```


### SC005

- **Name:** License expired
- **Level**: Error
- **Meaning:** `SponsorshipLicensedUntil` has expired.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicensedUntil='{value}' has expired (end of month {endOfMonth:yyyy-MM-dd} UTC).

  Renew the license in:
    {targetFile}

    <{Element} Include="{PackageId}" Version="{version}" SponsorshipLicensedUntil="yyyy-MM" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipLicensedUntil='2000-01' has expired (end of month 2000-01-31 UTC).

  Renew the license in:
    /work/MyApp/MyApp.csproj

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipLicensedUntil="yyyy-MM" />
  ```


### SC006

- **Name:** Metadata set on both PackageReference and PackageVersion
- **Level**: Error
- **Meaning:** Defensive backstop: metadata set on both PackageReference and PackageVersion. SC012 normally fires first now (because the wrong-side metadatum is itself a placement violation) — SC006 only surfaces if SC012's check is bypassed.
- **Syntax:** `{metadataName}: set on both PackageReference ('{r}') and PackageVersion ('{v}'). Set on only one.`
- **Example:** `GitHubSponsorAccount: set on both PackageReference ('alice') and PackageVersion ('bob'). Set on only one.`


### SC007

- **Name:** Invalid license date format
- **Level**: Error
- **Meaning:** `SponsorshipLicensedUntil` not in `yyyy-MM` format.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipLicensedUntil='{value}' is not in 'yyyy-MM' format.

  Fix the SponsorshipLicensedUntil attribute in:
    {targetFile}

    <{Element} Include="{PackageId}" Version="{version}" SponsorshipLicensedUntil="yyyy-MM" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipLicensedUntil='not-a-date' is not in 'yyyy-MM' format.

  Fix the SponsorshipLicensedUntil attribute in:
    /work/MyApp/MyApp.csproj

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipLicensedUntil="yyyy-MM" />
  ```


### SC008

- **Name:** Sponsorship attestation trusted
- **Level**: Info
- **Meaning:** `SponsorshipStart` is after pack date — verifier trusts the attestation (audit trail message).
- **Syntax:** `Package '{PackageId}': trusting unverified sponsor declaration ({attempts}): SponsorshipStart={startDate:yyyy-MM-dd} is later than package release {packDate:yyyy-MM-dd}, so the bundled sponsor list cannot contain this account.`
- **Example:** `Package 'MyOssLib': trusting unverified sponsor declaration (GitHubSponsors=carol): SponsorshipStart=2026-04-30 is later than package release 2026-04-15, so the bundled sponsor list cannot contain this account.`


### SC009

- **Name:** Bundled sponsor hash file missing
- **Level**: Error
- **Meaning:** Bundled sponsor hash file is missing from the package (corrupt install).
- **Syntax:** `Package '{PackageId}': bundled sponsor hash file not found at '{path}'.`
- **Example:** `Package 'MyOssLib': bundled sponsor hash file not found at 'C:\Users\me\.nuget\packages\myosslib\1.0.0\build\SponsorCheck.SponsorHashes.txt'.`


### SC010

- **Name:** Invalid SponsorshipStart format
- **Level**: Error
- **Meaning:** `SponsorshipStart` not in `yyyy-MM-dd` format.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipStart='{value}' is not in 'yyyy-MM-dd' format.

  Fix the SponsorshipStart attribute in:
    {targetFile}

    <{Element} Include="{PackageId}" Version="{version}" SponsorshipStart="yyyy-MM-dd" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipStart='yesterday' is not in 'yyyy-MM-dd' format.

  Fix the SponsorshipStart attribute in:
    /work/MyApp/MyApp.csproj

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipStart="yyyy-MM-dd" />
  ```


### SC011

- **Name:** SponsorshipStart in the future
- **Level**: Error
- **Meaning:** `SponsorshipStart` is in the future.
- **Syntax:**

  ```
  Package '{PackageId}': SponsorshipStart='{value}' is in the future.

  Fix the SponsorshipStart attribute in:
    {targetFile}

    <{Element} Include="{PackageId}" Version="{version}" SponsorshipStart="yyyy-MM-dd" />
  ```
- **Example:**

  ```
  Package 'MyOssLib': SponsorshipStart='2099-01-01' is in the future.

  Fix the SponsorshipStart attribute in:
    /work/MyApp/MyApp.csproj

    <PackageReference Include="MyOssLib" Version="1.2.3" SponsorshipStart="yyyy-MM-dd" />
  ```


### SC012

- **Name:** Sponsor metadata in the wrong location
- **Level**: Error
- **Meaning:** Under Central Package Management (`ManagePackageVersionsCentrally=true`) the SponsorCheck metadata must live on `<PackageVersion>` in `Directory.Packages.props`, not on `<PackageReference>` in the consumer's csproj. The mirror also holds: without CPM the metadata must live on `<PackageReference>`, not on `<PackageVersion>`.
- **Syntax:**

  ```
  Package '{PackageId}' uses Central Package Management, so SponsorCheck metadata must live on <PackageVersion> in Directory.Packages.props — not on <PackageReference>.

  Move the following attribute(s) off the <PackageReference> for '{PackageId}'
    in: {csprojPath}
    - {misplacedMetadata1}
    - {misplacedMetadata2}

  ...and onto the <PackageVersion> for '{PackageId}' in:
    {directoryPackagesPropsPath}
  ```

  The first sentence inverts (`is not using` / `<PackageReference>` ↔ `<PackageVersion>` ↔ `Directory.Packages.props` ↔ csproj) when CPM is off.
- **Example:**

  ```
  Package 'MyOssLib' uses Central Package Management, so SponsorCheck metadata must live on <PackageVersion> in Directory.Packages.props — not on <PackageReference>.

  Move the following attribute(s) off the <PackageReference> for 'MyOssLib'
    in: /work/MyApp/MyApp.csproj
    - SponsorshipLicenseIgnored

  ...and onto the <PackageVersion> for 'MyOssLib' in:
    /work/MyApp/Directory.Packages.props
  ```
