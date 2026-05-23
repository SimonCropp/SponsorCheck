// How the consumer declares SponsorCheck configuration, which drives both diagnostic placement and
// message wording:
//   NonCpm — metadata on <PackageReference> in the consumer csproj (odd SC0xx codes).
//   Cpm    — metadata on <PackageVersion> in Directory.Packages.props (even SC0xx codes).
//   Owner  — global MSBuild properties (owner mode); single source, SC021-SC028.
public enum ConsumerMode
{
    NonCpm,
    Cpm,
    Owner
}
