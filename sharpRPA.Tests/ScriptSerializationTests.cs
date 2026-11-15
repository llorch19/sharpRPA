using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Script;
using Xunit;

namespace sharpRPA.Tests
{
    public class ScriptSerializationTests : IDisposable
    {
        private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), "sharpRPA-tests-" + Guid.NewGuid().ToString("N"));

        public ScriptSerializationTests()
        {
            Directory.CreateDirectory(tempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }

        [Fact]
        public void SerializeScript_WritesNestedCommandsAndVariables()
        {
            var script = new Script();
            script.Variables.Add(new ScriptVariable { variableName = "Counter", variableValue = "0" });

            var loop = new BeginLoopCommand
            {
                v_LoopType = "Loop Number Of Times",
                v_LoopParameter = "2"
            };
            var inner = new VariableCommand
            {
                v_userVariableName = "Counter",
                v_Input = "1"
            };

            var flatCommands = new List<ScriptCommand>
            {
                new CommentCommand { v_Comment = "header" },
                loop,
                inner,
                new EndLoopCommand()
            };

            var path = Path.Combine(tempDirectory, "script.xml");
            Script.SerializeScript(path, flatCommands, script.Variables);

            var deserialized = Script.DeserializeFile(path);

            Assert.True(File.Exists(path));
            Assert.Single(deserialized.Variables);
            Assert.Equal("Counter", deserialized.Variables[0].variableName);
            Assert.Equal(2, deserialized.Commands.Count);

            var deserializedLoop = Assert.IsType<BeginLoopCommand>(deserialized.Commands[1].ScriptCommand);
            Assert.Equal("2", deserializedLoop.v_LoopParameter);
            Assert.Equal(2, deserialized.Commands[1].AdditionalScriptCommands.Count);
            Assert.IsType<VariableCommand>(deserialized.Commands[1].AdditionalScriptCommands[0].ScriptCommand);
            Assert.IsType<EndLoopCommand>(deserialized.Commands[1].AdditionalScriptCommands[1].ScriptCommand);
        }

        [Fact]
        public void SerializeScript_AssignsSequentialLineNumbers()
        {
            var flatCommands = new List<ScriptCommand>
            {
                new CommentCommand { v_Comment = "one" },
                new CommentCommand { v_Comment = "two" },
                new CommentCommand { v_Comment = "three" }
            };

            var path = Path.Combine(tempDirectory, "lines.xml");
            Script.SerializeScript(path, flatCommands, new List<ScriptVariable>());

            Assert.Equal(new[] { 1, 2, 3 }, flatCommands.Select(command => command.LineNumber).ToArray());

            var deserialized = Script.DeserializeFile(path);
            Assert.Equal(new[] { 1, 2, 3 }, deserialized.Commands.Select(action => action.ScriptCommand.LineNumber).ToArray());
        }

        [Fact]
        public void DeserializeXML_RoundTripsControlFlowCommands()
        {
            var flatCommands = new List<ScriptCommand>
            {
                new BeginIfCommand { v_IfActionType = "Value" },
                new VariableCommand { v_userVariableName = "X", v_Input = "1" },
                new EndIfCommand()
            };

            var path = Path.Combine(tempDirectory, "if.xml");
            Script.SerializeScript(path, flatCommands, new List<ScriptVariable>());

            var xml = File.ReadAllText(path);
            var deserialized = Script.DeserializeXML(xml);

            Assert.IsType<BeginIfCommand>(deserialized.Commands[0].ScriptCommand);
            Assert.IsType<EndIfCommand>(deserialized.Commands[0].AdditionalScriptCommands[1].ScriptCommand);
        }

        [Fact]
        public void SerializeScript_KeepsLegacyXsiTypeFormat()
        {
            var path = Path.Combine(tempDirectory, "format.xml");
            Script.SerializeScript(
                path,
                new List<ScriptCommand> { new VariableCommand { v_userVariableName = "X", v_Input = "1" } },
                new List<ScriptVariable>());

            var xml = File.ReadAllText(path);

            Assert.Contains("xsi:type=\"VariableCommand\"", xml);
            Assert.Contains("<ScriptCommand ", xml);
        }

        [Fact]
        public void RegisterKnownTypes_RoundTripsCommandsFromOtherAssemblies()
        {
            // mirrors Program.Main registering the Windows command assembly at startup
            Script.RegisterKnownTypes(typeof(FakePlatformCommand).Assembly);

            var path = Path.Combine(tempDirectory, "platform.xml");
            Script.SerializeScript(
                path,
                new List<ScriptCommand> { new FakePlatformCommand { v_Value = "windows" } },
                new List<ScriptVariable>());

            var deserialized = Script.DeserializeFile(path);

            var restored = Assert.IsType<FakePlatformCommand>(deserialized.Commands[0].ScriptCommand);
            Assert.Equal("windows", restored.v_Value);
        }
    }
}
