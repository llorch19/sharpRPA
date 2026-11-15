using System;
using System.Threading;
using sharpRPA.Mcp;
using Xunit;

namespace sharpRPA.Tests
{
    public class JobRegistryTests
    {
        private static JobInfo WaitUntil(JobRegistry jobs, string id, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var job = jobs.Get(id);
                if ((job != null) && (job.Status != "running"))
                {
                    return job;
                }

                Thread.Sleep(10);
            }

            return jobs.Get(id);
        }

        [Fact]
        public void Start_ReturnsImmediatelyAndCompletesWithAResult()
        {
            var jobs = new JobRegistry();
            var entered = new ManualResetEventSlim();

            var started = jobs.Start("rebuild_index", () =>
            {
                entered.Set();
                Thread.Sleep(200);
                return (object)new { indexed = 7 };
            });

            Assert.Equal("running", started.Status);
            Assert.Null(started.Result);
            Assert.True(entered.Wait(2000), "work should have started");

            var finished = WaitUntil(jobs, started.Id, TimeSpan.FromSeconds(5));
            Assert.Equal("succeeded", finished.Status);
            Assert.NotNull(finished.EndedAt);

            using (var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(finished.Result)))
            {
                Assert.Equal(7, document.RootElement.GetProperty("indexed").GetInt32());
            }
        }

        [Fact]
        public void Start_ReportsFailuresWithoutThrowing()
        {
            var jobs = new JobRegistry();
            var started = jobs.Start("rebuild_index", () => { throw new InvalidOperationException("boom"); });

            var finished = WaitUntil(jobs, started.Id, TimeSpan.FromSeconds(5));

            Assert.Equal("failed", finished.Status);
            Assert.Contains("boom", finished.Error);
            Assert.Contains("InvalidOperationException", finished.Error);
        }

        [Fact]
        public void Get_UnknownJobReturnsNull()
        {
            Assert.Null(new JobRegistry().Get("missing"));
            Assert.Null(new JobRegistry().Get(null));
        }
    }
}
