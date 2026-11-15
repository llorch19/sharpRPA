using System;
using System.Collections.Generic;
using System.Threading;
using sharpRPA.Core.Recording;

namespace sharpRPA.Core.Execution
{
    public interface IScriptExecutionContext
    {
        List<Script.ScriptVariable> Variables { get; }
        Dictionary<string, object> AppInstances { get; }
        AutomationCommands.ErrorHandlingCommand ErrorHandling { get; set; }
        CancellationToken CancellationToken { get; }
        bool IsCancellationPending { get; }
        IRecordingSink Recorder { get; }

        void ReportProgress(int? lineNumber, string message);
        void ReportProgress(string message);
        bool ShowMessage(string message, string title, int closeAfter = 0);
        void ExecuteCommand(Script.ScriptAction command);
        void WaitIfPaused();
        void RegisterCleanup(Action cleanup);
    }
}
