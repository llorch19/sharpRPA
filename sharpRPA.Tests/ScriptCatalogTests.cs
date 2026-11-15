using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Script;
using sharpRPA.Core.Services;
using Xunit;

namespace sharpRPA.Tests
{
    public sealed class TempScriptDirectory : IDisposable
    {
        public TempScriptDirectory()
        {
            Directory = Path.Combine(Path.GetTempPath(), "sharprpa-catalog-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public string Write(string fileName, IEnumerable<ScriptCommand> commands, List<ScriptVariable> variables = null)
        {
            var path = Path.Combine(Directory, fileName);
            Script.SerializeScript(path, commands.ToList(), variables ?? new List<ScriptVariable>());
            return path;
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, true);
            }
        }
    }

    public class ScriptCatalogTests
    {
        [Fact]
        public void ListScripts_ReturnsXmlFiles()
        {
            using (var folder = new TempScriptDirectory())
            {
                folder.Write("first.xml", new List<ScriptCommand> { new CommentCommand { v_Comment = "a" } });
                folder.Write("second.xml", new List<ScriptCommand> { new CommentCommand { v_Comment = "b" } });
                File.WriteAllText(Path.Combine(folder.Directory, "notes.txt"), "ignored");

                var catalog = new ScriptCatalog(folder.Directory);
                var scripts = catalog.ListScripts();

                Assert.Equal(2, scripts.Count);
                Assert.Contains(scripts, script => script.Name == "first.xml");
                Assert.DoesNotContain(scripts, script => script.Name.EndsWith(".txt"));
            }
        }

        [Fact]
        public void ListScripts_ReturnsEmptyWhenDirectoryMissing()
        {
            var catalog = new ScriptCatalog(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N")));

            Assert.Empty(catalog.ListScripts());
        }

        [Fact]
        public void ResolvePath_CombinesRelativeAndKeepsAbsolute()
        {
            using (var folder = new TempScriptDirectory())
            {
                var catalog = new ScriptCatalog(folder.Directory);
                var absolute = Path.Combine(folder.Directory, "abs.xml");

                Assert.Equal(Path.Combine(folder.Directory, "rel.xml"), catalog.ResolvePath("rel.xml"));
                Assert.Equal(absolute, catalog.ResolvePath(absolute));
                Assert.Throws<ArgumentException>(() => catalog.ResolvePath(" "));
            }
        }

        [Fact]
        public void GetScript_FlattensNestedCommandsWithDepth()
        {
            using (var folder = new TempScriptDirectory())
            {
                var loop = new BeginLoopCommand { v_LoopType = "Loop Number Of Times", v_LoopParameter = "2" };
                folder.Write("nested.xml", new List<ScriptCommand>
                {
                    new CommentCommand { v_Comment = "root" },
                    loop,
                    new PauseCommand { v_PauseLength = 1 },
                    new EndLoopCommand()
                });

                var detail = new ScriptCatalog(folder.Directory).GetScript("nested.xml");

                Assert.Equal(4, detail.Commands.Count);
                Assert.Equal(0, detail.Commands[0].Depth);
                Assert.Equal(0, detail.Commands[1].Depth);
                Assert.Equal(1, detail.Commands[2].Depth);
                Assert.Equal(1, detail.Commands[3].Depth);
                Assert.Equal("BeginLoopCommand", detail.Commands[1].CommandType);
            }
        }

        [Fact]
        public void ValidateScript_AcceptsWellFormedScript()
        {
            using (var folder = new TempScriptDirectory())
            {
                folder.Write("ok.xml", new List<ScriptCommand>
                {
                    new BeginIfCommand { v_IfActionType = "Value" },
                    new CommentCommand { v_Comment = "then" },
                    new EndIfCommand()
                });

                var result = new ScriptCatalog(folder.Directory).ValidateScript("ok.xml");

                Assert.True(result.IsValid, string.Join("; ", result.Errors));
                Assert.Empty(result.Errors);
            }
        }

        [Fact]
        public void ValidateScript_ReportsMissingEndMarker()
        {
            using (var folder = new TempScriptDirectory())
            {
                folder.Write("broken.xml", new List<ScriptCommand>
                {
                    new BeginLoopCommand { v_LoopType = "Loop Number Of Times", v_LoopParameter = "2" },
                    new PauseCommand { v_PauseLength = 1 }
                });

                var result = new ScriptCatalog(folder.Directory).ValidateScript("broken.xml");

                Assert.False(result.IsValid);
                Assert.Contains(result.Errors, error => error.Contains("End Loop"));
            }
        }

        [Fact]
        public void ValidateScript_ReportsDuplicateVariables()
        {
            using (var folder = new TempScriptDirectory())
            {
                folder.Write(
                    "dup.xml",
                    new List<ScriptCommand> { new CommentCommand { v_Comment = "x" } },
                    new List<ScriptVariable>
                    {
                        new ScriptVariable { variableName = "Dup", variableValue = "1" },
                        new ScriptVariable { variableName = "Dup", variableValue = "2" }
                    });

                var result = new ScriptCatalog(folder.Directory).ValidateScript("dup.xml");

                Assert.False(result.IsValid);
                Assert.Contains(result.Errors, error => error.Contains("Duplicate variable"));
            }
        }

        [Fact]
        public void ValidateScript_WarnsOnUnknownVariableReference()
        {
            using (var folder = new TempScriptDirectory())
            {
                folder.Write("vars.xml", new List<ScriptCommand>
                {
                    new MessageBoxCommand { v_Message = "value=[MissingOne]" },
                    new MessageBoxCommand { v_Message = "safe=[Folder.Desktop]" }
                });

                var result = new ScriptCatalog(folder.Directory).ValidateScript("vars.xml");

                Assert.True(result.IsValid);
                Assert.Contains(result.Warnings, warning => warning.Contains("[MissingOne]"));
                Assert.DoesNotContain(result.Warnings, warning => warning.Contains("[Folder.Desktop]"));
            }
        }

        [Fact]
        public void ValidateScript_ReportsMissingFile()
        {
            using (var folder = new TempScriptDirectory())
            {
                var result = new ScriptCatalog(folder.Directory).ValidateScript("nope.xml");

                Assert.False(result.IsValid);
                Assert.Contains(result.Errors, error => error.Contains("not found"));
            }
        }
    }
}
