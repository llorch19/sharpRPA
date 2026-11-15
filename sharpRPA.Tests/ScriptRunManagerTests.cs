using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Services;
using Xunit;

namespace sharpRPA.Tests
{
    public class ScriptRunManagerTests
    {
        [Fact]
        public void Start_ExecutesScriptAndReportsSuccess()
        {
            using (var folder = new TempScriptDirectory())
            {
                var path = folder.Write("run.xml", new List<ScriptCommand>
                {
                    new CommentCommand { v_Comment = "hello" },
                    new PauseCommand { v_PauseLength = 1 }
                });

                var manager = new ScriptRunManager();
                var started = manager.Start(path);

                // the script is tiny, so it may already have finished by the time Start returns
                Assert.Contains(started.Status, new[] { "running", "succeeded" });
                Assert.True(manager.WaitForCompletion(started.Id, TimeSpan.FromSeconds(10)));

                var finished = manager.Get(started.Id);
                Assert.Equal("succeeded", finished.Status);
                Assert.Null(finished.Error);
                Assert.NotNull(finished.EndedAt);
                Assert.Contains(finished.Log, line => line.Contains("Running Line"));
            }
        }

        [Fact]
        public void Start_ThrowsWhenScriptMissing()
        {
            var manager = new ScriptRunManager();

            Assert.Throws<FileNotFoundException>(() =>
                manager.Start(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".xml")));
        }

        [Fact]
        public void Get_UnknownRunId_ReturnsNull()
        {
            Assert.Null(new ScriptRunManager().Get("nope"));
            Assert.False(new ScriptRunManager().Cancel("nope"));
            Assert.False(new ScriptRunManager().WaitForCompletion("nope", TimeSpan.FromMilliseconds(1)));
        }

        [Fact]
        public void Cancel_StopsAPauseBeforeItFinishes()
        {
            using (var folder = new TempScriptDirectory())
            {
                var path = folder.Write("slow.xml", new List<ScriptCommand>
                {
                    new PauseCommand { v_PauseLength = 30000 }
                });

                var manager = new ScriptRunManager();
                var started = manager.Start(path);

                Thread.Sleep(100);
                Assert.True(manager.Cancel(started.Id));
                Assert.True(manager.WaitForCompletion(started.Id, TimeSpan.FromSeconds(5)));

                var finished = manager.Get(started.Id);
                Assert.Equal("cancelled", finished.Status);
            }
        }

        [Fact]
        public void Start_WithTimeoutFailsTheRun()
        {
            using (var folder = new TempScriptDirectory())
            {
                var scriptPath = folder.Write("timeout.xml", new List<ScriptCommand>
                {
                    new PauseCommand { v_PauseLength = 30000 }
                });

                var manager = new ScriptRunManager();
                var run = manager.Start(scriptPath, timeoutSeconds: 1);

                Assert.True(manager.WaitForCompletion(run.Id, TimeSpan.FromSeconds(15)));

                var finished = manager.Get(run.Id);
                Assert.Equal("failed", finished.Status);
                Assert.Contains("Timed out after 1s", finished.Error);
            }
        }

        [Fact]
        public void Run_FailedScriptReportsError()
        {
            using (var folder = new TempScriptDirectory())
            {
                // VariableCommand throws when the target variable does not exist
                var path = folder.Write("fail.xml", new List<ScriptCommand>
                {
                    new VariableCommand { v_userVariableName = "DoesNotExist", v_Input = "value" }
                });

                var manager = new ScriptRunManager();
                var started = manager.Start(path);
                Assert.True(manager.WaitForCompletion(started.Id, TimeSpan.FromSeconds(10)));

                var finished = manager.Get(started.Id);
                Assert.Equal("failed", finished.Status);
                Assert.Contains("Variable was not found", finished.Error);
            }
        }

        [Fact]
        public void List_ReturnsRunsNewestFirst()
        {
            using (var folder = new TempScriptDirectory())
            {
                var path = folder.Write("list.xml", new List<ScriptCommand> { new PauseCommand { v_PauseLength = 1 } });

                var manager = new ScriptRunManager();
                var first = manager.Start(path);
                Assert.True(manager.WaitForCompletion(first.Id, TimeSpan.FromSeconds(10)));
                var second = manager.Start(path);
                Assert.True(manager.WaitForCompletion(second.Id, TimeSpan.FromSeconds(10)));

                var runs = manager.List();
                Assert.Equal(2, runs.Count);
                Assert.Contains(runs, run => run.Id == first.Id);
                Assert.Contains(runs, run => run.Id == second.Id);
                Assert.True(runs[0].StartedAt >= runs[1].StartedAt);
            }
        }
    }
}
