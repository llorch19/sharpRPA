using System;
using System.IO;
using System.Linq;
using sharpRPA.Core.Recording;
using Xunit;

namespace sharpRPA.Tests
{
    public class RecordingLifecycleTests
    {
        private static RecordedAction Action(string type, string target, DateTimeOffset timestamp)
        {
            return new RecordedAction
            {
                ActionType = type,
                Target = target,
                Timestamp = timestamp
            };
        }

        [Fact]
        public void Stop_CommitsEventsAndRemovesStagingFile()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var start = DateTimeOffset.Now.AddSeconds(-2);

                var session = service.Start("browser", "checkout flow");
                session.Sink.Record(Action("click", "button#submit", start));
                session.Sink.Record(Action("type", "input#card", start.AddMilliseconds(250)));
                session.Sink.Record(Action("navigate", "https://example.com/checkout", start.AddMilliseconds(900)));

                Assert.Equal(3, session.ActionCount);
                Assert.True(File.Exists(session.StagingPath));

                var info = session.Stop();

                Assert.Equal(RecordingService.StatusCompleted, info.Status);
                Assert.Equal(3, info.ActionCount);
                Assert.Equal("checkout flow", info.Title);
                Assert.NotNull(info.DurationMs);
                Assert.Equal(900L, info.DurationMs.Value);
                Assert.False(File.Exists(session.StagingPath));

                var stored = service.Get(info.Id);
                Assert.NotNull(stored);
                Assert.Equal(3, stored.Events.Count);
                Assert.Equal("button#submit", stored.Events[0].Target);
                Assert.Equal("https://example.com/checkout", stored.Events[2].Target);
                Assert.Equal(start, stored.Events[0].Timestamp);
            }
        }

        [Fact]
        public void Stop_RecordsEndedAtAndSource()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var session = service.Start("windows-desktop");

                var info = session.Stop();

                Assert.Equal("windows-desktop", info.Source);
                Assert.NotNull(info.EndedAt);
                Assert.True(info.EndedAt >= info.StartedAt);
                Assert.Equal(0, info.ActionCount);
            }
        }

        [Fact]
        public void Abort_WritesNothingToTheDatabase()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var session = service.Start("browser");
                session.Sink.Record(Action("click", "a", DateTimeOffset.Now));

                var stagingPath = session.StagingPath;
                session.Abort();

                Assert.True(session.IsFinished);
                Assert.False(File.Exists(stagingPath));
                Assert.Empty(service.List());
                Assert.Empty(service.ActiveIds);
            }
        }

        [Fact]
        public void DisposeWithoutStop_BehavesLikeAbort()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                string stagingPath;

                using (var session = service.Start("browser"))
                {
                    stagingPath = session.StagingPath;
                    session.Sink.Record(Action("click", "a", DateTimeOffset.Now));
                }

                Assert.False(File.Exists(stagingPath));
                Assert.Empty(service.List());
            }
        }

        [Fact]
        public void Stop_Twice_Throws()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var session = service.Start("browser");
                session.Stop();

                Assert.Throws<InvalidOperationException>(() => session.Stop());
            }
        }

        [Fact]
        public void Stop_UnknownId_Throws()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();

                Assert.Throws<ArgumentException>(() => service.Stop("nope"));
                Assert.Throws<ArgumentException>(() => service.Record("nope", new RecordedAction { ActionType = "click" }));
                Assert.False(service.Abort("nope"));
            }
        }

        [Fact]
        public void Get_UnknownId_ReturnsNull()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();

                Assert.Null(service.Get("nope"));
                Assert.Null(service.GetInfo("nope"));
                Assert.Empty(service.List());
            }
        }

        [Fact]
        public void Start_RequiresSource()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();

                Assert.Throws<ArgumentException>(() => service.Start(" "));
            }
        }

        [Fact]
        public void List_IsNewestFirstAndScopedToTheDatabase()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();

                var first = service.Start("browser");
                first.Stop("one");
                var second = service.Start("browser");
                second.Stop("two");

                var listed = service.List();

                Assert.Equal(2, listed.Count);
                Assert.Equal(new[] { "two", "one" }, listed.Select(item => item.Title).ToArray());

                // a second service over the same database sees the same rows
                var reopened = workspace.CreateRecordingService();
                Assert.Equal(2, reopened.List().Count);
            }
        }
    }
}
