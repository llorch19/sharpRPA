using System;
using System.Collections.Generic;
using System.Threading;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Execution;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Script;

namespace sharpRPA.Tests
{
    /// <summary>
    /// Test-only execution context. Proves commands no longer require the WinForms engine.
    /// </summary>
    internal sealed class FakeScriptContext : IScriptExecutionContext
    {
        public List<ScriptVariable> Variables { get; } = new List<ScriptVariable>();

        public Dictionary<string, object> AppInstances { get; } = new Dictionary<string, object>();

        public ErrorHandlingCommand ErrorHandling { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public bool IsCancellationPending
        {
            get { return CancellationToken.IsCancellationRequested; }
        }

        public IRecordingSink Recorder { get; set; } = NullRecordingSink.Instance;

        public List<string> Progress { get; } = new List<string>();

        public List<ShownMessage> Messages { get; } = new List<ShownMessage>();

        public List<ScriptAction> ExecutedChildren { get; } = new List<ScriptAction>();

        public int PauseCount { get; private set; }

        public bool NextMessageResult { get; set; } = true;

        public void ReportProgress(int? lineNumber, string message)
        {
            Progress.Add((lineNumber.HasValue ? lineNumber.Value + ": " : string.Empty) + message);
        }

        public void ReportProgress(string message)
        {
            ReportProgress(null, message);
        }

        public bool ShowMessage(string message, string title, int closeAfter = 0)
        {
            Messages.Add(new ShownMessage { Message = message, Title = title, CloseAfter = closeAfter });
            return NextMessageResult;
        }

        public void ExecuteCommand(ScriptAction command)
        {
            ExecutedChildren.Add(command);
        }

        public void WaitIfPaused()
        {
            PauseCount++;
        }

        public void RegisterCleanup(Action cleanup)
        {
            Cleanups.Add(cleanup);
        }

        public List<Action> Cleanups { get; } = new List<Action>();

        public sealed class ShownMessage
        {
            public string Message { get; set; }
            public string Title { get; set; }
            public int CloseAfter { get; set; }
        }
    }
}
