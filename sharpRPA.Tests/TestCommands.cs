using System;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Execution;

namespace sharpRPA.Tests
{
    /// <summary>
    /// Counts how many times the runner executed this command.
    /// </summary>
    internal sealed class CountingCommand : ScriptCommand
    {
        public int Count { get; private set; }

        public CountingCommand()
        {
            this.CommandName = "CountingCommand";
            this.SelectionName = "Counting Command";
            this.CommandEnabled = true;
            this.DefaultPause = 0;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            Count++;
        }
    }

    /// <summary>
    /// Always fails, used to verify error-handling policy.
    /// </summary>
    internal sealed class ThrowingCommand : ScriptCommand
    {
        public ThrowingCommand()
        {
            this.CommandName = "ThrowingCommand";
            this.SelectionName = "Throwing Command";
            this.CommandEnabled = true;
            this.DefaultPause = 0;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            throw new InvalidOperationException("boom");
        }
    }
}
