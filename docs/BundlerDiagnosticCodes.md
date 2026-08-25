# Bundler diagnostic codes

Codes in the `SC1xx` range are emitted by the bundler at the OSS author's pack time.

Every emitted message is prefixed with the code's short **Name** (e.g. `Platform fetch failed. Polar HTTP 500: ...`) and suffixed with ` See: https://github.com/SimonCropp/SponsorCheck/blob/main/docs/BundlerDiagnosticCodes.md#<code>`. The Syntax/Example entries below show the inner format string only — the name and link wrap is added at log time.


### SC100

- **Name:** Platform fetch failed
- **Level**: Error
- **Meaning:** Bundler-side platform error (HTTP failure, GraphQL error, an override file naming an unknown platform, etc.) — message is the underlying `MaintenanceFeeException`. Two platform failures are *not* reported here, because both have a definite cause the catch-all would hide: a credential the platform actively rejected is [SC107](#sc107), and an exhausted rate limit is [SC108](#sc108).
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
- **Pull-request builds:** on a detected pull-request CI build the bundler is *skipped* rather than failing with SC102 — the credential is normally unavailable on PRs and the PR package is throwaway (packs without the verifier). Force bundling on PRs with `<SponsorCheckBundleInPullRequest>true</SponsorCheckBundleInPullRequest>`. See [OSS author setup → Pull request builds](AuthorSetup.md#pull-request-builds).


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
- **Meaning:** A `<SponsorExemption>` item declared next to the SponsorCheck reference is invalid. Each item must have a non-empty `Include=` (the exemption name) and a non-empty `Message=` metadatum (the criteria text consumers will see when claiming the exemption). Duplicate names (case-insensitive) are also rejected. The optional `MaxTermMonths=` metadatum, when present, must be a positive whole number — no sign, no decimal point, no exponent. It is rejected at pack time rather than ignored because the value is baked into every consumer's build, so a typo would otherwise silently ship an exemption with no end date required.
- **Syntax:** `SponsorExemption '{name}': {reason}` (where `{reason}` is one of `Message metadata is empty`, `duplicate definition`, `MaxTermMonths='{value}' is not a positive whole number of months`, or `an item has an empty Name (Include attribute)`).
- **Example:** `SponsorExemption 'Consulting': Message metadata is empty.`
- **Example:** `SponsorExemption 'Consulting': MaxTermMonths='six' is not a positive whole number of months.`


### SC107

- **Name:** Platform credential rejected
- **Level**: Error
- **Meaning:** A credential *was* configured, but the platform's API answered HTTP 401 — it does not recognize that credential. Split out of the SC100 catch-all because it is never transient: re-running the build cannot fix it, and the stored value has to be replaced. Distinct from SC102, which means no credential was configured at all.
- **Syntax:** `{platformLabel}: the configured token was rejected (HTTP 401{detail}). Token supplied: {shape}. {diagnosis} {contrast}` followed by `The rejected credential was read from {source}.`, or — when more than one candidate was configured — `SponsorCheck tried {n} configured credentials in order and none were accepted: {sources}. The detail above is from the last attempt.`
- **Example:** `GitHub Sponsors: the configured token was rejected (HTTP 401 Bad credentials). Token supplied: ghp_…, 40 chars. That is a classic PAT, which is the correct type, so GitHub no longer recognizes this particular value — it has been deleted or regenerated. Issue a replacement at https://github.com/settings/tokens with read:user (plus read:org when sponsored as an organization). A 401 means GitHub does not recognize the credential itself — a missing scope returns INSUFFICIENT_SCOPES and an organization that blocks classic PATs returns FORBIDDEN, so this is not a scope, SSO, or org-policy failure. The rejected credential was read from the <GitHubToken> MSBuild property (which MSBuild also auto-imports from a 'GitHubToken' env var).`

**Token shape.** The rejected value itself is never logged — only its published vendor prefix, its character count, and whether it was stored with surrounding whitespace, so the message stays safe in a public CI log. That is enough to separate the three failures that actually occur: a correct-type token gone dead (`ghp_…`), a token of the wrong type entirely (`github_pat_…` fine-grained, or `ghs_…`, which is what `secrets.GITHUB_TOKEN` expands to on GitHub Actions — neither can read Sponsorships), and a truncated or newline-padded paste.

**Credential source.** The trailing clause names *where the value was read from*, not only which token is wrong. The same `GitHubToken` name arrives from an env var on CI and from user-secrets locally, so the source is what identifies the box to go and edit.

**Open Collective.** Its token is optional — it only raises the rate limit on collectives with many backers — so removing the stored value entirely is a valid fix there, and the message says so.


### SC108

- **Name:** Platform rate limit exhausted
- **Level**: Error
- **Meaning:** The platform refused the call because its rate limit is spent. Split out of the SC100 catch-all as the inverse of [SC107](#sc107): nothing is misconfigured, and an identical build succeeds once the window rolls over — so the message carries a reset time rather than a fix. Detected as HTTP 429 on any platform; as HTTP 403 carrying `x-ratelimit-remaining: 0` (GitHub's primary limit) or `Retry-After` (GitHub's secondary limit); and as a GraphQL error of type `RATE_LIMITED`, which GitHub returns with HTTP 200 rather than an error status.
- **Syntax:** `{platformLabel}: the API rate limit is exhausted (HTTP {status}). {reset} {note}` — where `{reset}` is one of `The limit resets at {yyyy-MM-dd HH:mm:ss} UTC, in {n} minutes.`, `The platform asked for a retry after {n} seconds.`, or `The platform reported no reset time.`
- **Example:** `GitHub Sponsors: the API rate limit is exhausted (HTTP 403). The limit resets at 2026-08-04 15:12:00 UTC, in 23 minutes. The limit is charged against the token making the call, not the repository, so a CI matrix that packs several projects at once draws them all from one budget. Nothing is misconfigured — re-running the build after the reset is the fix.`

**A 403 is ambiguous.** GitHub uses it for both an exhausted primary limit and a genuine permission failure, so only a 403 that also carries `x-ratelimit-remaining: 0` or `Retry-After` is treated as a rate limit. Everything else stays on the SC100 path, including the org-blocks-classic-PATs case, which arrives as a GraphQL `FORBIDDEN` error and has its own message.

**Reset time.** The absolute `x-ratelimit-reset` (epoch seconds) is preferred over the relative `Retry-After`. A reset timestamp already in the past renders as "which has already elapsed" rather than as a negative delay — clock skew between a build agent and the platform is common enough to be worth not rendering as a defect.

**Open Collective.** Anonymous calls are normal there (the token is optional), and the anonymous ceiling is the one a collective with many backers actually reaches, since paging the member list costs several requests. When the rate-limited call carried no token, the message recommends creating one instead of recommending a retry.


### SC109

- **Name:** Invalid PrivateSponsorMaxTermMonths
- **Level**: Error
- **Meaning:** The `PrivateSponsorMaxTermMonths` metadatum on the SponsorCheck reference is not a positive whole number of months. It caps how far ahead a consumer may set `SponsorshipPrivateUntil` when attesting to a private or incognito sponsorship — see [Private and incognito sponsors](AuthorSetup.md#private-and-incognito-sponsors). Leave it unset for the default of 12 months. Parsed with `NumberStyles.None`, the same rule as `MaxTermMonths` on a `<SponsorExemption>`, so `+6`, `6.0` and hex are rejected rather than quietly reinterpreted. Failing the pack is deliberate: the value is baked into the generated verifier, so a silently ignored one would ship a cap the author did not choose.
- **Syntax:** `SponsorCheck: PrivateSponsorMaxTermMonths='{value}' is not a positive whole number of months.`
- **Example:** `SponsorCheck: PrivateSponsorMaxTermMonths='six' is not a positive whole number of months.`
