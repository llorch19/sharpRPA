using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;
using Xunit;

namespace sharpRPA.Tests
{
    public class RunStoreTests
    {
        private static RunStore CreateStore(TempWorkspace workspace)
        {
            var database = new SqliteDatabase(workspace.DatabasePath);
            database.Initialize();
            return new RunStore(database);
        }

        private static RunRecord RunningRun(string id = "run-1", int minutesAgo = 1)
        {
            return new RunRecord
            {
                Id = id,
                ScriptPath = @"C:\scripts\demo.xml",
                Status = "running",
                StartedAt = DateTimeOffset.Now.AddMinutes(-minutesAgo)
            };
        }

        [Fact]
        public void InsertAndComplete_RoundTripsStatusAndLog()
        {
            using (var workspace = new TempWorkspace())
            {
                var store = CreateStore(workspace);

                store.Insert(RunningRun());

                var started = store.Get("run-1");
                Assert.NotNull(started);
                Assert.Equal("running", started.Status);
                Assert.Null(started.EndedAt);
                Assert.Equal(0, store.CountLog("run-1"));

                store.Complete(new RunRecord
                {
                    Id = "run-1",
                    ScriptPath = started.ScriptPath,
                    Status = "succeeded",
                    StartedAt = started.StartedAt,
                    EndedAt = DateTimeOffset.Now,
                    CurrentLine = 7,
                    Error = null
                }, new List<RunLogEntry>
                {
                    new RunLogEntry { LineNumber = 1, Message = "Running Line 1" },
                    new RunLogEntry { LineNumber = null, Message = "Bot Finished" }
                });

                var finished = store.Get("run-1");
                Assert.Equal("succeeded", finished.Status);
                Assert.Equal(7, finished.CurrentLine);
                Assert.NotNull(finished.EndedAt);
                Assert.Equal(2, store.CountLog("run-1"));

                var tail = store.TailLog("run-1", 1);
                var entry = Assert.Single(tail);
                Assert.Null(entry.LineNumber);
                Assert.Equal("Bot Finished", entry.Message);
            }
        }

        [Fact]
        public void List_FiltersByStatusAndPaginates()
        {
            using (var workspace = new TempWorkspace())
            {
                var store = CreateStore(workspace);

                for (var i = 1; i <= 3; i++)
                {
                    // started_at comes from Insert (Complete only finalises the row)
                    var startedRun = RunningRun("run-" + i, i);
                    store.Insert(startedRun);
                    store.Complete(new RunRecord
                    {
                        Id = startedRun.Id,
                        ScriptPath = startedRun.ScriptPath,
                        Status = i == 3 ? "failed" : "succeeded",
                        StartedAt = startedRun.StartedAt,
                        EndedAt = DateTimeOffset.Now
                    }, new List<RunLogEntry>());
                }

                Assert.Equal(3, store.Count(null));
                Assert.Equal(2, store.Count("succeeded"));
                Assert.Equal(1, store.Count("failed"));

                var page = store.List(null, 2, 0);
                Assert.Equal(2, page.Count);
                Assert.Equal("run-1", page[0].Id); // newest first
                Assert.Single(store.List(null, 2, 2));
                Assert.Equal("run-3", Assert.Single(store.List("failed", 10, 0)).Id);
            }
        }

        [Fact]
        public void MarkInterruptedRuns_OnlyTouchesRunningRows()
        {
            using (var workspace = new TempWorkspace())
            {
                var store = CreateStore(workspace);
                store.Insert(RunningRun("stuck"));
                store.Insert(new RunRecord
                {
                    Id = "done",
                    ScriptPath = "s.xml",
                    Status = "succeeded",
                    StartedAt = DateTimeOffset.Now.AddMinutes(-5),
                    EndedAt = DateTimeOffset.Now
                });

                Assert.Equal(1, store.MarkInterruptedRuns());

                Assert.Equal("failed", store.Get("stuck").Status);
                Assert.Contains("restart", store.Get("stuck").Error);
                Assert.Equal("succeeded", store.Get("done").Status);
                Assert.Equal(0, store.MarkInterruptedRuns());
            }
        }

        [Fact]
        public void ManagerPersistsRunAndLogForLaterReaders()
        {
            using (var workspace = new TempWorkspace())
            using (var folder = new TempScriptDirectory())
            {
                var scriptPath = folder.Write("persisted.xml", new List<ScriptCommand>
                {
                    new CommentCommand { v_Comment = "check" },
                    new PauseCommand { v_PauseLength = 1 }
                });

                var store = CreateStore(workspace);
                var manager = new ScriptRunManager(null, store);
                var run = manager.Start(scriptPath);
                Assert.True(manager.WaitForCompletion(run.Id, TimeSpan.FromSeconds(10)));

                // a fresh manager over the same database models a server restart
                var reopened = new ScriptRunManager(null, CreateStore(workspace));
                var loaded = reopened.Get(run.Id);

                Assert.NotNull(loaded);
                Assert.Equal("succeeded", loaded.Status);
                Assert.Null(loaded.Error);
                Assert.True(loaded.LogLineCount > 0);
                Assert.Contains(loaded.Log, line => line.Contains("Running Line"));
                Assert.Contains(loaded.Log, line => line.Contains("MESSAGE") || line.Contains("Skipping"));
            }
        }

        [Fact]
        public void ManagerListsRunsFromTheStore()
        {
            using (var workspace = new TempWorkspace())
            using (var folder = new TempScriptDirectory())
            {
                var scriptPath = folder.Write("listed.xml", new List<ScriptCommand> { new PauseCommand { v_PauseLength = 1 } });
                var store = CreateStore(workspace);
                var manager = new ScriptRunManager(null, store);

                var run = manager.Start(scriptPath);
                Assert.True(manager.WaitForCompletion(run.Id, TimeSpan.FromSeconds(10)));

                var runs = manager.ListRuns(null, 10, 0);
                Assert.Single(runs);
                Assert.Equal(run.Id, runs[0].Id);
                Assert.Equal(1, manager.CountRuns(null));
                Assert.Equal(0, manager.CountRuns("running"));
                Assert.Equal(1, manager.CountRuns("succeeded"));
            }
        }

        [Fact]
        public void ManagerWithoutStoreRemainsInMemoryOnly()
        {
            using (var folder = new TempScriptDirectory())
            {
                var scriptPath = folder.Write("memory.xml", new List<ScriptCommand> { new PauseCommand { v_PauseLength = 1 } });

                var manager = new ScriptRunManager();
                var run = manager.Start(scriptPath);
                Assert.True(manager.WaitForCompletion(run.Id, TimeSpan.FromSeconds(10)));

                Assert.NotNull(manager.Get(run.Id));
                Assert.Single(manager.ListRuns(null, 10, 0));
                Assert.Null(manager.Get("unknown"));
            }
        }
    }
}
