public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifyDiffPlex.Initialize();
        VerifierSettings.IgnoreMembers(
            "HelpKeyword",
            "SenderName",
            "ContinueOnError",
            "ProjectFileOfTaskNode",
            "File",
            "Subcategory",
            "Timestamp");
        VerifierSettings.IgnoreMember<BuildEventArgs>(_ => _.ThreadId);
        // Message importance is High on a build server and Low elsewhere, so recording it would make
        // every snapshot of a message-severity diagnostic disagree between CI and a developer
        // machine. These snapshots exist to pin message text; the importance branch is pinned
        // directly, on both of its inputs, by SponsorCheckLogTests.
        VerifierSettings.IgnoreMember<BuildMessageEventArgs>(_ => _.Importance);
        VerifierSettings.Inline(maxLines: 10, applyMaxLinesToExisting: true);
    }
}
