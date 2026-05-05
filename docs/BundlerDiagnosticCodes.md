# Bundler diagnostic codes

Codes in the `SC1xx` range are emitted by the bundler at the OSS author's pack time.


### SC100

- **Level**: Error
- **Meaning:** Bundler-side platform error (HTTP failure, GraphQL error, etc.) — message is the underlying `MaintenanceFeeException`.
- **Syntax:** `{exception.Message}`
- **Example:** `Polar HTTP 500: {"detail":"server error"}`


### SC101

- **Level**: Error
- **Meaning:** OSS author has no `<Platform>Account` metadata on SponsorCheck.
- **Syntax:** `SponsorCheck: at least one platform account metadata must be set on the PackageReference or PackageVersion (e.g. GitHubSponsorsAccount="acmecorp").`
- **Example:** `SponsorCheck: at least one platform account metadata must be set on the PackageReference or PackageVersion (e.g. GitHubSponsorsAccount="acmecorp").`


### SC102

- **Level**: Error
- **Meaning:** A platform that requires a credential is missing one (GitHub Sponsors, Polar). Setup advice flips between user-secrets-first (local) and env-var-only (CI) based on `BuildServerDetector`.
- **Syntax:** `{platformLabel}: API token required. {advice}` where `advice` is `Run \`dotnet user-secrets set SponsorCheck:{Platform}Token <pat>\` (recommended for local dev), or set the <{Platform}Token> MSBuild property, or set the '{Platform}Token' env var.` locally, or `Set the '{Platform}Token' env var (CI providers should expose their encrypted secret under this name; MSBuild auto-imports it as the <{Platform}Token> property).` on CI.
- **Example:** `GitHub Sponsors: API token required. Run \`dotnet user-secrets set SponsorCheck:GitHubToken <pat>\` (recommended for local dev), or set the <GitHubToken> MSBuild property, or set the 'GitHubToken' env var.`


### SC103

- **Level**: Warning
- **Meaning:** User-secrets file present but couldn't be read at pack time.
- **Syntax:** `SponsorCheck: could not read user-secrets at '{path}': {exception.Message}`
- **Example:** `SponsorCheck: could not read user-secrets at 'C:\Users\me\AppData\Roaming\Microsoft\UserSecrets\abc-123\secrets.json': Unexpected character encountered while parsing value.`
