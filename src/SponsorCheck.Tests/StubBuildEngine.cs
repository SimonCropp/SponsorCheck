public sealed class StubBuildEngine : IBuildEngine
{
    public List<BuildErrorEventArgs> Errors { get; } = [];
    public List<BuildWarningEventArgs> Warnings { get; } = [];
    public List<BuildMessageEventArgs> Messages { get; } = [];

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "";

    public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
    public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e);
    public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e);
    public void LogCustomEvent(CustomBuildEventArgs e) { }

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) =>
        throw new NotSupportedException();
}
