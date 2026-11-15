using System;
using System.IO;
using System.Linq;
using sharpRPA.Core.Recording;
using Xunit;

namespace sharpRPA.Tests
{
    /// <summary>
    /// Covers the crash windows around a recording commit: the staging file is the
    /// only thing that exists until the row is written, so recovery must handle both
    /// "file without row" and "row without file cleanup".
    /// </summary>
    public class RecordingRecoveryTests
    {
        private static RecordedAction Action(string target)
        {
            return new RecordedAction { ActionType = "click", Target = target, Timestamp = DateTimeOffset.Now };
        }

        private static string WriteStagingFile(RecordingService service, string id, params RecordedAction[] events)
        {
            var path = Path.Combine(service.StagingDirectory, id + ".jsonl");

            using (var sink = new StagingRecordingSink(path, id, "browser", "orphan capture", DateTimeOffset.Now.AddMinutes(-5)))
            {
                foreach (var action in events)
                {
                    sink.Record(action);
                }

                sink.FlushToDisk();
            }

            return path;
        }

        [Fact]
        public void CollectStaging_ImportsOrphanedFileAsIncomplete()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var id = Guid.NewGuid().ToString("N");
                var stagingFile = WriteStagingFile(service, id, Action("button#ok"), Action("input#name"));
                File.SetLastWriteTimeUtc(stagingFile, DateTime.UtcNow.AddHours(-2));

                var handled = service.CollectStaging(TimeSpan.Zero);

                Assert.Equal(1, handled);
                Assert.False(File.Exists(stagingFile));

                var imported = service.Get(id);
                Assert.NotNull(imported);
                Assert.Equal(RecordingService.StatusIncomplete, imported.Status);
                Assert.Equal("browser", imported.Source);
                Assert.Equal("orphan capture", imported.Title);
                Assert.Equal(2, imported.ActionCount);
                Assert.Equal(2, imported.Events.Count);
            }
        }

        [Fact]
        public void CollectStaging_RemovesStagingFileWhenTheRowAlreadyExists()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var session = service.Start("browser");
                session.Sink.Record(Action("a"));
                var info = session.Stop();

                // simulate a crash between COMMIT and the staging cleanup
                var leftover = WriteStagingFile(service, info.Id, Action("a"));
                File.SetLastWriteTimeUtc(leftover, DateTime.UtcNow.AddHours(-2));

                var handled = service.CollectStaging(TimeSpan.Zero);

                Assert.Equal(1, handled);
                Assert.False(File.Exists(leftover));
                Assert.Single(service.List());
            }
        }

        [Fact]
        public void CollectStaging_SkipsFilesThatAreTooFresh()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var id = Guid.NewGuid().ToString("N");
                var stagingFile = WriteStagingFile(service, id, Action("a"));

                var handled = service.CollectStaging(TimeSpan.FromHours(1));

                Assert.Equal(0, handled);
                Assert.True(File.Exists(stagingFile));
            }
        }

        [Fact]
        public void CollectStaging_NeverTouchesAnActiveSession()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var session = service.Start("browser");
                session.Sink.Record(Action("live"));

                File.SetLastWriteTimeUtc(session.StagingPath, DateTime.UtcNow.AddHours(-2));

                var handled = service.CollectStaging(TimeSpan.Zero);

                Assert.Equal(0, handled);
                Assert.True(File.Exists(session.StagingPath));
                Assert.Contains(session.Id, service.ActiveIds);
            }
        }

        [Fact]
        public void CollectStaging_IsIdempotent()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var id = Guid.NewGuid().ToString("N");
                var stagingFile = WriteStagingFile(service, id, Action("a"));
                File.SetLastWriteTimeUtc(stagingFile, DateTime.UtcNow.AddHours(-2));

                Assert.Equal(1, service.CollectStaging(TimeSpan.Zero));
                Assert.Equal(0, service.CollectStaging(TimeSpan.Zero));
                Assert.Single(service.List());
            }
        }

        [Fact]
        public void CollectStaging_HandlesAHeaderOnlyFile()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var id = Guid.NewGuid().ToString("N");
                var stagingFile = WriteStagingFile(service, id);
                File.SetLastWriteTimeUtc(stagingFile, DateTime.UtcNow.AddHours(-2));

                var handled = service.CollectStaging(TimeSpan.Zero);

                Assert.Equal(1, handled);
                var imported = service.Get(id, includeEvents: false);
                Assert.NotNull(imported);
                Assert.Equal(0, imported.ActionCount);
            }
        }

        [Fact]
        public void CommittedRecordingIsNotDuplicatedByRecovery()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var session = service.Start("browser");
                session.Sink.Record(Action("a"));
                var info = session.Stop();

                var stored = Assert.Single(service.List());
                Assert.Equal(info.Id, stored.Id);
                Assert.Empty(Directory.GetFiles(service.StagingDirectory, "*.jsonl"));
            }
        }
    }
}
