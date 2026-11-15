using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using sharpRPA.Core.Script;
using Xunit;

namespace sharpRPA.Tests
{
    public class SampleScriptTests
    {
        private static readonly string[] CoreOnlySamples =
        {
            "If Conditional Sample.xml",
            "String Split Sample.xml",
            "Web Automation Sample.xml",
            "WebAPI HTTP Sample.xml"
        };

        private static readonly string[] WindowsCommandNames =
        {
            "ActivateWindowCommand",
            "MoveWindowCommand",
            "ResizeWindowCommand",
            "CloseWindowCommand",
            "SetWindowStateCommand",
            "WaitForWindowCommand",
            "ClipboardGetTextCommand",
            "SendKeysCommand",
            "SendMouseMoveCommand",
            "ThickAppClickItemCommand",
            "ThickAppGetTextCommand",
            "ExcelCreateApplicationCommand",
            "ExcelOpenWorkbookCommand",
            "ExcelAddWorkbookCommand",
            "ExcelGoToCellCommand",
            "ExcelSetCellCommand",
            "ExcelGetCellCommand",
            "ExcelRunMacroCommand",
            "ExcelCloseApplicationCommand",
            "OCRCommand",
            "ScreenshotCommand"
        };

        private static string SamplePath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Samples", fileName);
        }

        private static IEnumerable<Core.AutomationCommands.ScriptCommand> Flatten(IEnumerable<ScriptAction> actions)
        {
            return actions.SelectMany(action =>
                new[] { action.ScriptCommand }.Concat(Flatten(action.AdditionalScriptCommands)));
        }

        [Theory]
        [InlineData("If Conditional Sample.xml")]
        [InlineData("String Split Sample.xml")]
        [InlineData("Web Automation Sample.xml")]
        [InlineData("WebAPI HTTP Sample.xml")]
        public void CoreSampleScripts_DeserializeAndRoundTrip(string fileName)
        {
            var original = Script.DeserializeFile(SamplePath(fileName));
            var originalCommands = Flatten(original.Commands).ToList();

            Assert.NotEmpty(originalCommands);
            Assert.All(originalCommands, command => Assert.NotNull(command));

            var roundTripPath = Path.Combine(Path.GetTempPath(), "sharpRPA-roundtrip-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                Script.SerializeScript(roundTripPath, originalCommands, original.Variables);

                var reparsed = Script.DeserializeFile(roundTripPath);
                var reparsedCommands = Flatten(reparsed.Commands).ToList();

                Assert.Equal(
                    originalCommands.Select(command => command.GetType().FullName + "|" + command.GetDisplayValue()).ToList(),
                    reparsedCommands.Select(command => command.GetType().FullName + "|" + command.GetDisplayValue()).ToList());
                Assert.Equal(original.Variables.Count, reparsed.Variables.Count);
            }
            finally
            {
                if (File.Exists(roundTripPath))
                {
                    File.Delete(roundTripPath);
                }
            }
        }

        [Fact]
        public void EverySampleCommandType_IsEitherCoreOrRegisteredWindowsCommand()
        {
            var sampleDirectory = Path.GetDirectoryName(SamplePath("x.xml"));
            var coreTypes = typeof(Script).Assembly.GetTypes()
                .Where(type => typeof(Core.AutomationCommands.ScriptCommand).IsAssignableFrom(type) && !type.IsAbstract)
                .Select(type => type.Name)
                .ToHashSet();

            foreach (var file in Directory.GetFiles(sampleDirectory, "*.xml"))
            {
                var document = XDocument.Load(file);
                var declaredTypes = document.Descendants()
                    .Where(element => element.Name.LocalName == "ScriptCommand")
                    .Select(element => element.Attribute(XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance") + "type")?.Value)
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Distinct();

                foreach (var declaredType in declaredTypes)
                {
                    Assert.True(
                        coreTypes.Contains(declaredType) || WindowsCommandNames.Contains(declaredType),
                        Path.GetFileName(file) + " references unknown command type: " + declaredType);
                }
            }
        }
    }
}
