using System;
using System.Collections.Generic;
using System.Threading;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Execution;
using sharpRPA.Core.Script;
using Xunit;

namespace sharpRPA.Tests
{
    public class ScriptRunnerTests
    {
        private static ScriptAction Action(ScriptCommand command)
        {
            return new ScriptAction { ScriptCommand = command };
        }

        private static Script BuildScript(params ScriptAction[] commands)
        {
            var script = new Script();
            script.Commands.AddRange(commands);
            return script;
        }

        [Fact]
        public void Run_ExecutesTopLevelCommandsInOrder()
        {
            var first = new CountingCommand();
            var second = new CountingCommand();
            var runner = new ScriptRunner();

            runner.Run(BuildScript(Action(first), Action(second)));

            Assert.Equal(1, first.Count);
            Assert.Equal(1, second.Count);
        }

        [Fact]
        public void Run_SkipsCommentsAndCommentedCommands()
        {
            var comment = new CommentCommand { v_Comment = "ignored" };
            var commented = new CountingCommand { IsCommented = true };
            var normal = new CountingCommand();
            var runner = new ScriptRunner();
            var progress = new List<string>();
            runner.ProgressChanged += (line, message) => progress.Add(message);

            runner.Run(BuildScript(Action(comment), Action(commented), Action(normal)));

            Assert.Equal(1, normal.Count);
            Assert.Contains(progress, line => line.StartsWith("Skipping Line"));
        }

        [Fact]
        public void Run_LoopRepeatsChildCommands()
        {
            var counter = new CountingCommand();
            var loop = new BeginLoopCommand
            {
                v_LoopType = "Loop Number Of Times",
                v_LoopParameter = "3"
            };
            var loopAction = Action(loop);
            loopAction.AdditionalScriptCommands.Add(Action(counter));

            var runner = new ScriptRunner();
            runner.Run(BuildScript(loopAction));

            Assert.Equal(3, counter.Count);
        }

        [Fact]
        public void Run_IfEvaluatesTrueBranch()
        {
            var counter = new CountingCommand();
            var ifCommand = BuildIf("1", "is equal to", "1");
            var ifAction = Action(ifCommand);
            ifAction.AdditionalScriptCommands.Add(Action(counter));

            var runner = new ScriptRunner();
            runner.Run(BuildScript(ifAction));

            Assert.Equal(1, counter.Count);
        }

        [Fact]
        public void Run_IfSkipsChildrenWhenFalse()
        {
            var counter = new CountingCommand();
            var ifCommand = BuildIf("1", "is equal to", "2");
            var ifAction = Action(ifCommand);
            ifAction.AdditionalScriptCommands.Add(Action(counter));

            var runner = new ScriptRunner();
            runner.Run(BuildScript(ifAction));

            Assert.Equal(0, counter.Count);
        }

        [Fact]
        public void Run_ContinueOnErrorExecutesFollowingCommand()
        {
            var counter = new CountingCommand();
            var script = BuildScript(
                Action(new ErrorHandlingCommand { v_ErrorHandlingAction = "Continue Processing" }),
                Action(new ThrowingCommand()),
                Action(counter));

            var runner = new ScriptRunner();
            var progress = new List<string>();
            runner.ProgressChanged += (line, message) => progress.Add(message);

            runner.Run(script);

            Assert.Equal(1, counter.Count);
            Assert.Contains(progress, line => line.Contains("Continuing Per Error Handling"));
        }

        [Fact]
        public void Run_StopsOnErrorByDefault()
        {
            var counter = new CountingCommand();
            var script = BuildScript(Action(new ThrowingCommand()), Action(counter));

            var runner = new ScriptRunner();

            Assert.Throws<InvalidOperationException>(() => runner.Run(script));
            Assert.Equal(0, counter.Count);
        }

        [Fact]
        public void Run_ThrowsWhenAlreadyCancelled()
        {
            var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var runner = new ScriptRunner { CancellationToken = cancellation.Token };

            Assert.Throws<OperationCanceledException>(() => runner.Run(BuildScript(Action(new CountingCommand()))));
        }

        [Fact]
        public void Run_ReportsProgressForExecutedCommands()
        {
            var runner = new ScriptRunner();
            var progress = new List<string>();
            runner.ProgressChanged += (line, message) => progress.Add(message);

            runner.Run(BuildScript(Action(new CountingCommand()), Action(new CountingCommand())));

            Assert.Equal(2, progress.FindAll(line => line.StartsWith("Running Line")).Count);
        }

        [Fact]
        public void Run_UsesOnlyContextState_NoWinFormsRequired()
        {
            var variable = new ScriptVariable { variableName = "Target", variableValue = "before" };
            var setVariable = new VariableCommand
            {
                v_userVariableName = "Target",
                v_Input = "after"
            };

            var runner = new ScriptRunner();
            var script = new Script();
            script.Variables.Add(variable);
            script.Commands.Add(Action(setVariable));

            runner.Run(script);

            Assert.Equal("after", variable.variableValue);
        }

        private static BeginIfCommand BuildIf(string value1, string operand, string value2)
        {
            var ifCommand = new BeginIfCommand { v_IfActionType = "Value" };
            ifCommand.v_IfActionParameterTable.Rows.Add("Value1", value1);
            ifCommand.v_IfActionParameterTable.Rows.Add("Operand", operand);
            ifCommand.v_IfActionParameterTable.Rows.Add("Value2", value2);
            return ifCommand;
        }
    }
}
