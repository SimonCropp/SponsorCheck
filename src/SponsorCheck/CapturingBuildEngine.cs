// Collects what the verifier logged instead of letting it reach the build.
//
// The alternative was threading a diagnostic sink through DecisionApplier and every message
// renderer below it. A TaskLoggingHelper over this engine gets the same result without touching
// any of that code: the logging call sites stay exactly as they are and the event they raise is
// captured here, which is also what keeps the immediate path (DeferDiagnostic="false") and the
// deferred path rendering byte-identical text.
//
// Only the last event is kept. Every DecisionApplier branch emits one diagnostic and returns, so
// there is never a second one to lose.
public sealed class CapturingBuildEngine :
    IBuildEngine
{
    public DeferredDiagnostic? Diagnostic { get; private set; }

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "";

    public void LogErrorEvent(BuildErrorEventArgs e) =>
        Diagnostic = new(e.Code ?? "", Severity.Error, e.Message ?? "");

    public void LogWarningEvent(BuildWarningEventArgs e) =>
        Diagnostic = new(e.Code ?? "", Severity.Warning, e.Message ?? "");

    public void LogMessageEvent(BuildMessageEventArgs e) =>
        Diagnostic = new(e.Code ?? "", Severity.Message, e.Message ?? "");

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) =>
        throw new NotSupportedException();
}
