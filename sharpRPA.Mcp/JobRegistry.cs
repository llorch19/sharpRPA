using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace sharpRPA.Mcp
{
    internal sealed class JobInfo
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public object Result { get; set; }
        public string Error { get; set; }
    }

    /// <summary>
    /// Runs bounded work off the request thread and hands back an id the agent can poll.
    /// Jobs are deliberately process-local: every job we expose wraps idempotent work
    /// (rebuilding derived data), so losing them on restart is safe - just run it again.
    /// </summary>
    internal sealed class JobRegistry
    {
        private readonly ConcurrentDictionary<string, JobState> jobs = new ConcurrentDictionary<string, JobState>();

        public JobInfo Start(string kind, Func<object> work)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            var state = new JobState(Guid.NewGuid().ToString("N"), kind);
            jobs[state.Id] = state;

            state.Completion = Task.Run(() =>
            {
                try
                {
                    state.Complete("succeeded", work(), null);
                }
                catch (Exception ex)
                {
                    state.Complete("failed", null, ex.GetType().Name + ": " + ex.Message);
                }
            });

            return state.Snapshot();
        }

        public JobInfo Get(string jobId)
        {
            JobState state;
            return jobs.TryGetValue(jobId ?? string.Empty, out state) ? state.Snapshot() : null;
        }

        private sealed class JobState
        {
            private readonly object sync = new object();

            public JobState(string id, string kind)
            {
                Id = id;
                Kind = kind;
                StartedAt = DateTimeOffset.Now;
                Status = "running";
            }

            public string Id { get; private set; }
            public string Kind { get; private set; }
            public DateTimeOffset StartedAt { get; private set; }
            public string Status { get; private set; }
            public DateTimeOffset? EndedAt { get; private set; }
            public object Result { get; private set; }
            public string Error { get; private set; }
            public Task Completion { get; set; }

            public void Complete(string status, object result, string error)
            {
                lock (sync)
                {
                    Status = status;
                    Result = result;
                    Error = error;
                    EndedAt = DateTimeOffset.Now;
                }
            }

            public JobInfo Snapshot()
            {
                lock (sync)
                {
                    return new JobInfo
                    {
                        Id = Id,
                        Kind = Kind,
                        Status = Status,
                        StartedAt = StartedAt,
                        EndedAt = EndedAt,
                        Result = Result,
                        Error = Error
                    };
                }
            }
        }
    }
}
