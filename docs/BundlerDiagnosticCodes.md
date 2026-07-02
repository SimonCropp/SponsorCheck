# Bundler diagnostic codes

Codes in the `SC1xx` range are emitted by the bundler at the OSS author's pack time.

Every emitted message is prefixed with the code's short **Name** (e.g. `Platform fetch failed. Polar HTTP 500: ...`) and suffixed with ` See: https://github.com/SimonCropp/SponsorCheck/blob/main/docs/BundlerDiagnosticCodes.md#<code>`. The Syntax/Example entries below show the inner format string only — the name and link wrap is added at log time.


### SC100

- **Name:** Platform fetch failed
- **Level**: Error
- **Meaning:** Bundler-side platform error (HTTP failure, GraphQL error, an override file naming an unknown platform, etc.) — message is the underlying `MaintenanceFeeException`.
- **Syntax:** `{exception.Message}`
- **Example:** `Polar HTTP 500: {"detail":"server error"}`


### SC101

- **Name:** No platform account configured
- **Level**: Error
- **Meaning:** OSS author has no `<Platform>Account` metadata on SponsorCheck.
- **Syntax:** `SponsorCheck: at least one platform account metadata must be set on the PackageReference or PackageVersion (e.g. GitHubSponsorsAccount="acmecorp").`
- **Example:** `SponsorCheck: at least one platform account metadata must be set on the PackageReference or PackageVersion (e.g. GitHubSponsorsAccount="acmecorp").`


### SC102

- **Name:** Missing platform credential
- **Level**: Error
- **Meaning:** A platform that requires a credential is missing one (GitHub Sponsors, Polar). Setup advice flips between user-secrets-first (local) and env-var-only (CI) based on `BuildServerDetector`.
- **Syntax:** `{platformLabel}: API token required. {advice}` where `advice` is `Run \`dotnet user-secrets set SponsorCheck:{Platform}Token <pat>\` (recommended for local dev), or set the <{Platform}Token> MSBuild property, or set the '{Platform}Token' env var.` locally, or `Set the '{Platform}Token' env var (CI providers should expose their encrypted secret under this name; MSBuild auto-imports it as the <{Platform}Token> property).` on CI.
- **Example:** `GitHub Sponsors: API token required. Run \`dotnet user-secrets set SponsorCheck:GitHubToken <pat>\` (recommended for local dev), or set the <GitHubToken> MSBuild property, or set the 'GitHubToken' env var.`


### SC103

- **Name:** User-secrets read failed
- **Level**: Warning
- **Meaning:** User-secrets file present but couldn't be read at pack time.
- **Syntax:** `SponsorCheck: could not read user-secrets at '{path}': {exception.Message}`
- **Example:** `SponsorCheck: could not read user-secrets at 'C:\Users\me\AppData\Roaming\Microsoft\UserSecrets\abc-123\secrets.json': Unexpected character encountered while parsing value.`


### SC104

- **Name:** Invalid severity override
- **Level**: Error
- **Meaning:** A `<Code>SeverityOverride` metadatum on the SponsorCheck reference has an unrecognized value. Overrideable metadata: `NoLicenseSpecifiedSeverityOverride` (SC001), `LicenseIgnoredSeverityOverride` (SC005), `InvalidAccountSeverityOverride` (SC007), `LicenseExpiredSeverityOverride` (SC009). Allowed values are `error`, `warning`, `message`.
- **Syntax:** `{metadataName}='{value}' is not a recognized severity. Allowed: error, warning, message.`
- **Example:** `NoLicenseSpecifiedSeverityOverride='critical' is not a recognized severity. Allowed: error, warning, message.`


### SC105

- **Name:** Invalid SponsorOwner
- **Level**: Error
- **Meaning:** `SponsorOwner` is baked into the consumer-side property names as a prefix (e.g. `<acme_GitHubSponsorAccount>`), so it must be a safe MSBuild property name prefix: starts with a letter, then letters, digits, or underscores. Hyphens, dots, spaces, and other characters are rejected.
- **Syntax:** `SponsorCheck: SponsorOwner='{ownerId}' is not a valid MSBuild property prefix. SponsorOwner is baked into the consumer-side property names (e.g. <{ownerId}_GitHubSponsorAccount>) so it must start with a letter and contain only letters, digits, and underscores.`
- **Example:** `SponsorCheck: SponsorOwner='acme-corp' is not a valid MSBuild property prefix. SponsorOwner is baked into the consumer-side property names (e.g. <acme-corp_GitHubSponsorAccount>) so it must start with a letter and contain only letters, digits, and underscores.`


### SC106

- **Name:** Invalid exemption definition
- **Level**: Error
- **Meaning:** A `<SponsorExemption>` item declared next to the SponsorCheck reference is invalid. Each item must have a non-empty `Include=` (the exemption name) and a non-empty `Message=` metadatum (the criteria text consumers will see when claiming the exemption). Duplicate names (case-insensitive) are also rejected.
- **Syntax:** `SponsorExemption '{name}': {reason}` (where `{reason}` is one of `Message metadata is empty`, `duplicate definition`, or `an item has an empty Name (Include attribute)`).
- **Example:** `SponsorExemption 'Consulting': Message metadata is empty.`
