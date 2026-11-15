using System;
using System.Collections.Generic;
using System.IO;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;
using Xunit;

namespace sharpRPA.Tests
{
    public class ArtifactStoreTests
    {
        private static ArtifactStore CreateStore(TempWorkspace workspace)
        {
            var database = new SqliteDatabase(workspace.DatabasePath);
            database.Initialize();
            return new ArtifactStore(database, Path.Combine(workspace.Root, "runs"));
        }

        [Fact]
        public void Register_MakesTheFileQueryableByRun()
        {
            using (var workspace = new TempWorkspace())
            {
                var store = CreateStore(workspace);
                var directory = store.EnsureRunDirectory("run-1");
                var logPath = Path.Combine(directory, "run.log");
                File.WriteAllText(logPath, "line one\nline two");

                var info = store.Register("run-1", "log", logPath, "text/plain");

                Assert.False(string.IsNullOrEmpty(info.Id));
                Assert.Equal("run-1", info.RunId);
                Assert.Equal(17, info.Bytes);

                var single = Assert.Single(store.List("run-1"));
                Assert.Equal(info.Id, single.Id);
                Assert.Equal("text/plain", single.Mime);
                Assert.True(File.Exists(single.Path));

                Assert.Equal(info.Id, store.Get(info.Id).Id);
                Assert.Null(store.Get("missing"));
                Assert.Empty(store.List("run-2"));
            }
        }

        [Fact]
        public void Register_ReportsMissingFilesAsZeroBytes()
        {
            using (var workspace = new TempWorkspace())
            {
                var store = CreateStore(workspace);
                store.EnsureRunDirectory("run-1");

                var info = store.Register("run-1", "screenshot", Path.Combine(workspace.Root, "nope.png"), "image/png");

                Assert.Equal(0, info.Bytes);
            }
        }

        [Fact]
        public void Run_WritesALogArtifactWithTheRunId()
        {
            using (var workspace = new TempWorkspace())
            using (var folder = new TempScriptDirectory())
            {
                var scriptPath = folder.Write("artifact.xml", new List<ScriptCommand>
                {
                    new CommentCommand { v_Comment = "captured" },
                    new PauseCommand { v_PauseLength = 1 }
                });

                var store = CreateStore(workspace);
                var manager = new ScriptRunManager(store);
                var run = manager.Start(scriptPath);

                Assert.True(manager.WaitForCompletion(run.Id, TimeSpan.FromSeconds(10)));
                Assert.Equal("succeeded", manager.Get(run.Id).Status);

                var log = Assert.Single(store.List(run.Id));
                Assert.Equal("log", log.Kind);
                Assert.Equal("text/plain", log.Mime);
                Assert.Contains("Running Line", File.ReadAllText(log.Path));
                Assert.Contains(run.Id, log.Path);
            }
        }

        [Fact]
        public void Run_WithoutArtifactStoreStillCompletes()
        {
            using (var folder = new TempScriptDirectory())
            {
                var scriptPath = folder.Write("plain.xml", new List<ScriptCommand> { new PauseCommand { v_PauseLength = 1 } });

                var manager = new ScriptRunManager();
                var run = manager.Start(scriptPath);

                Assert.True(manager.WaitForCompletion(run.Id, TimeSpan.FromSeconds(10)));
                Assert.Equal("succeeded", manager.Get(run.Id).Status);
            }
        }
    }
}
