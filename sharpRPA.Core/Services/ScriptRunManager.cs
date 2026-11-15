using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using sharpRPA.Core.Execution;
using sharpRPA.Core.Script;
using sharpRPA.Core.Storage;
using ScriptModel = sharpRPA.Core.Script.Script;

namespace sharpRPA.Core.Services
{
    public enum ScriptRunStatus
    {
        Running,
        Succeeded,
        Failed,
        Cancelled
    }

    public sealed class ScriptRunInfo
    {
        public string Id { get; set; }
        public string ScriptPath { get; set; }
        public string Status { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int? CurrentLineNumber { get; set; }
        public string Error { get; set; }
        public List<string> Log { get; set; } = new List<string>();

        /// <summary>Total log lines for the run; for store-backed runs this may exceed <see cref="Log"/>.</summary>
        public int LogLineCount { get; set; }
    }

    /// <summary>
    /// Starts headless script runs and exposes status/log/cancellation to any host
    /// (MCP server, CLI, tests) without depending on WinForms.
    /// </summary>
    public sealed class ScriptRunManager
    {
        private const int MaxRetainedRuns = 50;

        private const int MaxStoredLogLines = 5000;

        private readonly ConcurrentDictionary<string, ScriptRunState> runs = new ConcurrentDictionary<string, ScriptRunState>();
        private readonly ArtifactStore artifacts;
        private readonly RunStore runStore;

        public ScriptRunManager(ArtifactStore artifacts = null, RunStore runStore = null)
        {
            this.artifacts = artifacts;
            this.runStore = runStore;
        }

        public ScriptRunInfo Start(string scriptPath, int timeoutSeconds = 0)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                throw new ArgumentException("A script path is required.", nameof(scriptPath));
            }

            // parse up front so callers get an immediate, actionable error
            var script = ScriptModel.DeserializeFile(scriptPath);

            PruneCompletedRuns();

            var state = new ScriptRunState(Guid.NewGuid().ToString("N"), scriptPath, timeoutSeconds);
            var runner = new ScriptRunner { CancellationToken = state.Cancellation.Token };

            if (timeoutSeconds > 0)
            {
                state.Cancellation.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            }

            runner.ProgressChanged += (lineNumber, message) => state.Record(lineNumber, message);
            runner.MessageHandler = (message, title, closeAfter) =>
            {
                state.Record(null, "MESSAGE [" + title + "]: " + message);
                return true;
            };

            state.Runner = runner;
            runs[state.Id] = state;

            if (runStore != null)
            {
                try
                {
                    runStore.Insert(new RunRecord
                    {
                        Id = state.Id,
                        ScriptPath = scriptPath,
                        Status = "running",
                        StartedAt = state.StartedAt
                    });
                }
                catch (Exception)
                {
                    // persistence must never block a run from starting
                }
            }

            state.CompletionTask = Task.Run(() => Execute(state, runner, script));

            return state.Snapshot();
        }

        public ScriptRunInfo Get(string runId)
        {
            ScriptRunState state;
            if (runs.TryGetValue(runId ?? string.Empty, out state))
            {
                return state.Snapshot();
            }

            return LoadFromStore(runId);
        }

        private ScriptRunInfo LoadFromStore(string runId)
        {
            if (runStore == null)
            {
                return null;
            }

            var record = runStore.Get(runId);
            if (record == null)
            {
                return null;
            }

            return ToInfo(record, runStore.TailLog(record.Id, MaxStoredLogLines), runStore.CountLog(record.Id));
        }

        public List<ScriptRunInfo> ListRuns(string status, int limit, int offset)
        {
            if (runStore != null)
            {
                return runStore.List(status, limit, offset)
                    .Select(record => ToInfo(record, runStore.TailLog(record.Id, 0), runStore.CountLog(record.Id)))
                    .ToList();
            }

            return FilterMemoryRuns(status).Skip(offset).Take(limit).Select(state => state.Snapshot()).ToList();
        }

        public int CountRuns(string status)
        {
            return runStore != null ? runStore.Count(status) : FilterMemoryRuns(status).Count();
        }

        private IEnumerable<ScriptRunState> FilterMemoryRuns(string status)
        {
            var all = runs.Values.OrderByDescending(state => state.StartedAt);
            if (string.IsNullOrWhiteSpace(status))
            {
                return all;
            }

            return all.Where(state => string.Equals(state.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));
        }

        private static ScriptRunInfo ToInfo(RunRecord record, List<RunLogEntry> log, int logLineCount)
        {
            return new ScriptRunInfo
            {
                Id = record.Id,
                ScriptPath = record.ScriptPath,
                Status = record.Status,
                StartedAt = record.StartedAt,
                EndedAt = record.EndedAt,
                CurrentLineNumber = record.CurrentLine,
                Error = record.Error,
                Log = log.Select(FormatEntry).ToList(),
                LogLineCount = logLineCount
            };
        }

        private static string FormatEntry(RunLogEntry entry)
        {
            return entry.LineNumber.HasValue
                ? "Line " + entry.LineNumber + ": " + entry.Message
                : entry.Message;
        }

        public List<ScriptRunInfo> List()
        {
            return runs.Values
                .Select(state => state.Snapshot())
                .OrderByDescending(run => run.StartedAt)
                .ToList();
        }

        public bool Cancel(string runId)
        {
            ScriptRunState state;
            if (!runs.TryGetValue(runId ?? string.Empty, out state))
            {
                return false;
            }

            state.RequestCancel();
            return true;
        }

        public bool WaitForCompletion(string runId, TimeSpan timeout)
        {
            ScriptRunState state;
            if (!runs.TryGetValue(runId ?? string.Empty, out state))
            {
                return false;
            }

            return state.CompletionTask.Wait(timeout);
        }

        private void Execute(ScriptRunState state, ScriptRunner runner, ScriptModel script)
        {
            ScriptRunStatus status;
            string error = null;

            try
            {
                runner.Run(script);
                status = ScriptRunStatus.Succeeded;
            }
            catch (OperationCanceledException)
            {
                if (state.TimeoutSeconds > 0 && !state.WasManuallyCancelled)
                {
                    // a CancelAfter expiry is a failure, not a user cancel
                    status = ScriptRunStatus.Failed;
                    error = "Timed out after " + state.TimeoutSeconds + "s.";
                }
                else
                {
                    status = ScriptRunStatus.Cancelled;
                }
            }
            catch (Exception ex)
            {
                status = ScriptRunStatus.Failed;
                error = ex.ToString();
            }

            // persist before the terminal status becomes observable: a caller that sees
            // "succeeded" must already be able to read the run back from the store
            FlushRun(state, status, error);
            state.Complete(status, error);

            try
            {
                // capture while browser/excel instances are still alive
                CaptureArtifacts(state, runner);

                runner.CleanUp();
            }
            catch (Exception)
            {
            }

            state.SignalCompletion();
        }

        /// <summary>
        /// Persists the final run row and its full log. Best effort: a persistence failure
        /// must never change the run outcome the caller observes.
        /// </summary>
        private void FlushRun(ScriptRunState state, ScriptRunStatus status, string error)
        {
            if (runStore == null)
            {
                return;
            }

            try
            {
                runStore.Complete(state.ToRunRecord(status, error), state.LogEntries);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Writes the run log and any reachable browser state to the artifact folder.
        /// Artifact capture must never fail a run, so every step is best effort.
        /// </summary>
        private void CaptureArtifacts(ScriptRunState state, ScriptRunner runner)
        {
            if (artifacts == null)
            {
                return;
            }

            try
            {
                var directory = artifacts.EnsureRunDirectory(state.Id);
                var snapshot = state.Snapshot();

                var logPath = Path.Combine(directory, "run.log");
                File.WriteAllLines(logPath, snapshot.Log, new UTF8Encoding(false));
                artifacts.Register(state.Id, "log", logPath, "text/plain");

                CaptureBrowserArtifacts(state.Id, directory, runner);
            }
            catch (Exception)
            {
            }
        }

        private void CaptureBrowserArtifacts(string runId, string directory, ScriptRunner runner)
        {
            var index = 0;

            foreach (var session in runner.AppInstances.Values.OfType<BrowserSession>())
            {
                index++;
                var page = session.Page;
                if ((page == null) || page.IsClosed)
                {
                    continue;
                }

                try
                {
                    var screenshotPath = Path.Combine(directory, "browser-" + index + ".png");
                    page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath }).GetAwaiter().GetResult();
                    artifacts.Register(runId, "screenshot", screenshotPath, "image/png", index);

                    var domPath = Path.Combine(directory, "browser-" + index + ".html");
                    File.WriteAllText(domPath, page.ContentAsync().GetAwaiter().GetResult(), new UTF8Encoding(false));
                    artifacts.Register(runId, "dom", domPath, "text/html", index);
                }
                catch (Exception)
                {
                    // a dead page or missing browser must not mask the run result
                }
            }
        }

        private void PruneCompletedRuns()
        {
            var completed = runs.Values
                .Where(state => state.Status != ScriptRunStatus.Running)
                .OrderByDescending(state => state.StartedAt)
                .Skip(MaxRetainedRuns)
                .ToList();

            foreach (var state in completed)
            {
                ScriptRunState removed;
                runs.TryRemove(state.Id, out removed);
            }
        }

        private sealed class ScriptRunState
        {
            private readonly object sync = new object();
            private readonly List<RunLogEntry> log = new List<RunLogEntry>();

            public ScriptRunState(string id, string scriptPath, int timeoutSeconds = 0)
            {
                Id = id;
                ScriptPath = scriptPath;
                TimeoutSeconds = timeoutSeconds;
                StartedAt = DateTimeOffset.Now;
                Status = ScriptRunStatus.Running;
                Cancellation = new CancellationTokenSource();
            }

            public int TimeoutSeconds { get; private set; }

            public bool WasManuallyCancelled { get; private set; }

            public void RequestCancel()
            {
                lock (sync)
                {
                    WasManuallyCancelled = true;
                }

                Cancellation.Cancel();
            }

            public string Id { get; private set; }
            public string ScriptPath { get; private set; }
            public DateTimeOffset StartedAt { get; private set; }
            public ScriptRunStatus Status { get; private set; }
            public DateTimeOffset? EndedAt { get; private set; }
            public int? CurrentLineNumber { get; private set; }
            public string Error { get; private set; }
            public CancellationTokenSource Cancellation { get; private set; }
            public ScriptRunner Runner { get; set; }
            public Task CompletionTask { get; set; }
            public TaskCompletionSource<bool> Completion { get; private set; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Record(int? lineNumber, string message)
            {
                lock (sync)
                {
                    if (lineNumber.HasValue)
                    {
                        CurrentLineNumber = lineNumber;
                    }

                    log.Add(new RunLogEntry { LineNumber = lineNumber, Message = message });
                }
            }

            public RunRecord ToRunRecord(ScriptRunStatus status, string error)
            {
                lock (sync)
                {
                    return new RunRecord
                    {
                        Id = Id,
                        ScriptPath = ScriptPath,
                        Status = status.ToString().ToLowerInvariant(),
                        StartedAt = StartedAt,
                        EndedAt = DateTimeOffset.Now,
                        CurrentLine = CurrentLineNumber,
                        Error = error
                    };
                }
            }

            public List<RunLogEntry> LogEntries
            {
                get
                {
                    lock (sync)
                    {
                        return new List<RunLogEntry>(log);
                    }
                }
            }

            public void Complete(ScriptRunStatus status, string error)
            {
                lock (sync)
                {
                    Status = status;
                    Error = error;
                    EndedAt = DateTimeOffset.Now;
                }
            }

            public void SignalCompletion()
            {
                Completion.TrySetResult(true);
            }

            public ScriptRunInfo Snapshot()
            {
                lock (sync)
                {
                    return new ScriptRunInfo
                    {
                        Id = Id,
                        ScriptPath = ScriptPath,
                        Status = Status.ToString().ToLowerInvariant(),
                        StartedAt = StartedAt,
                        EndedAt = EndedAt,
                        CurrentLineNumber = CurrentLineNumber,
                        Error = Error,
                        Log = log.Select(FormatEntry).ToList(),
                        LogLineCount = log.Count
                    };
                }
            }
        }
    }
}
