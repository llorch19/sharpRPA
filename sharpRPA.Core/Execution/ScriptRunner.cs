using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Script;

namespace sharpRPA.Core.Execution
{
    public sealed class ScriptRunner : IScriptExecutionContext
    {
        private readonly List<Action> cleanupActions = new List<Action>();

        public List<ScriptVariable> Variables { get; set; } = new List<ScriptVariable>();

        public Dictionary<string, object> AppInstances { get; } = new Dictionary<string, object>();

        public AutomationCommands.ErrorHandlingCommand ErrorHandling { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public bool IsCancellationPending
        {
            get { return CancellationToken.IsCancellationRequested; }
        }

        public IRecordingSink Recorder { get; set; } = NullRecordingSink.Instance;

        public event Action<int?, string> ProgressChanged;

        public Func<string, string, int, bool> MessageHandler { get; set; }

        public Action PauseHandler { get; set; }

        public void Run(Core.Script.Script script)
        {
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }

            Variables = script.Variables ?? new List<ScriptVariable>();
            AppInstances.Clear();
            ErrorHandling = null;

            foreach (var executionCommand in script.Commands)
            {
                CancellationToken.ThrowIfCancellationRequested();
                ExecuteCommand(executionCommand);
            }
        }

        public void ExecuteCommand(ScriptAction command)
        {
            if ((command == null) || (command.ScriptCommand == null))
            {
                return;
            }

            CancellationToken.ThrowIfCancellationRequested();
            WaitIfPaused();
            CancellationToken.ThrowIfCancellationRequested();

            var parentCommand = command.ScriptCommand;

            if ((parentCommand is AutomationCommands.CommentCommand) || parentCommand.IsCommented)
            {
                ReportProgress(parentCommand.LineNumber, "Skipping Line " + parentCommand.LineNumber + ": " + parentCommand.GetDisplayValue());
                return;
            }

            ReportProgress(parentCommand.LineNumber, "Running Line " + parentCommand.LineNumber + ": " + parentCommand.GetDisplayValue());

            try
            {
                if ((parentCommand is AutomationCommands.BeginLoopCommand) || (parentCommand is AutomationCommands.BeginIfCommand))
                {
                    parentCommand.RunCommand(this, command);
                }
                else
                {
                    parentCommand.RunCommand(this);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if ((ErrorHandling != null) && (ErrorHandling.v_ErrorHandlingAction == "Continue Processing"))
                {
                    ReportProgress(parentCommand.LineNumber, "Error Occured at Line " + parentCommand.LineNumber + ":" + ex);
                    ReportProgress(parentCommand.LineNumber, "Continuing Per Error Handling");
                    return;
                }

                throw;
            }
        }

        public void ReportProgress(int? lineNumber, string message)
        {
            var handler = ProgressChanged;
            if (handler != null)
            {
                handler(lineNumber, message);
            }
        }

        public void ReportProgress(string message)
        {
            ReportProgress(null, "Command Report: " + message);
        }

        public bool ShowMessage(string message, string title, int closeAfter = 0)
        {
            var handler = MessageHandler;
            return handler == null || handler(message, title, closeAfter);
        }

        public void WaitIfPaused()
        {
            var handler = PauseHandler;
            if (handler != null)
            {
                handler();
            }
        }

        public void RegisterCleanup(Action cleanup)
        {
            if (cleanup != null)
            {
                cleanupActions.Add(cleanup);
            }
        }

        public void CleanUp()
        {
            foreach (var cleanup in cleanupActions)
            {
                try
                {
                    cleanup();
                }
                catch (Exception)
                {
                }
            }

            cleanupActions.Clear();

            foreach (var disposable in AppInstances.Values.OfType<IDisposable>())
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                }
            }

            AppInstances.Clear();
        }
    }
}
