using System;
using sharpRPA.Core;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Script;
using Xunit;

namespace sharpRPA.Tests
{
    public class ExecutionAbstractionTests
    {
        [Fact]
        public void VariableCommand_RunsAgainstFakeContext()
        {
            var context = new FakeScriptContext();
            context.Variables.Add(new ScriptVariable { variableName = "Name", variableValue = "initial" });

            var command = new VariableCommand
            {
                v_userVariableName = "Name",
                v_Input = "resolved"
            };
            command.RunCommand(context);

            Assert.Equal("resolved", context.Variables[0].variableValue);
        }

        [Fact]
        public void VariableCommand_ThrowsWhenVariableMissing()
        {
            var context = new FakeScriptContext();
            var command = new VariableCommand
            {
                v_userVariableName = "Missing",
                v_Input = "value"
            };

            Assert.Throws<Exception>(() => command.RunCommand(context));
        }

        [Fact]
        public void MessageBoxCommand_UsesContextInsteadOfWinFormsForm()
        {
            var context = new FakeScriptContext();
            context.Variables.Add(new ScriptVariable { variableName = "Name", variableValue = "World" });

            var command = new MessageBoxCommand
            {
                v_Message = "Hello [Name]",
                v_AutoCloseAfter = 5
            };
            command.RunCommand(context);

            Assert.Single(context.Messages);
            Assert.Equal("Hello World", context.Messages[0].Message);
            Assert.Equal("MessageBox Command", context.Messages[0].Title);
            Assert.Equal(5, context.Messages[0].CloseAfter);
        }

        [Fact]
        public void ErrorHandlingCommand_SetsPolicyOnContext()
        {
            var context = new FakeScriptContext();
            var command = new ErrorHandlingCommand { v_ErrorHandlingAction = "Continue Processing" };

            command.RunCommand(context);

            Assert.Same(command, context.ErrorHandling);
            Assert.Equal("Continue Processing", context.ErrorHandling.v_ErrorHandlingAction);
        }

        [Fact]
        public void ConvertToUserVariable_ResolvesBracketsAndMath()
        {
            var context = new FakeScriptContext();
            context.Variables.Add(new ScriptVariable { variableName = "Name", variableValue = "World" });

            Assert.Equal("Hello World", "Hello [Name]".ConvertToUserVariable(context));
            Assert.Equal("3", "1+2".ConvertToUserVariable(context));
            Assert.Equal(string.Empty, ((string)null).ConvertToUserVariable(context));
        }

        [Fact]
        public void ConvertToUserVariable_ResolvesSystemVariables()
        {
            var context = new FakeScriptContext();

            var resolved = "[PC.MachineName]".ConvertToUserVariable(context);

            Assert.Equal(Environment.MachineName, resolved);
        }

        [Fact]
        public void StoreInUserVariable_WritesThroughContext()
        {
            var context = new FakeScriptContext();
            context.Variables.Add(new ScriptVariable { variableName = "Result", variableValue = "old" });

            "new-value".StoreInUserVariable(context, "Result");

            Assert.Equal("new-value", context.Variables[0].variableValue);
        }
    }
}
