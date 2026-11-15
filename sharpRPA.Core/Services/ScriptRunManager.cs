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
    }

    /// <summary>
    /// Starts headless script runs and exposes status/log/cancellation to any host
    /// (MCP server, CLI, tests) without depending on WinForms.
    /// </summary>
    public sealed class ScriptRunManager
    {
        private const int MaxRetainedRuns = 50;

        private readonly ConcurrentDictionary<string, ScriptRunState> runs = new ConcurrentDictionary<string, ScriptRunState>();
        private readonly ArtifactStore artifacts;

        public ScriptRunManager(ArtifactStore artifacts = null)
        {
            this.artifacts = artifacts;
        }

        public ScriptRunInfo Start(string scriptPath)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
            {
                throw new ArgumentException("A script path is required.", nameof(scriptPath));
            }

            // parse up front so callers get an immediate, actionable error
            var script = ScriptModel.DeserializeFile(scriptPath);

            PruneCompletedRuns();

            var state = new ScriptRunState(Guid.NewGuid().ToString("N"), scriptPath);
            var runner = new ScriptRunner { CancellationToken = state.Cancellation.Token };

            runner.ProgressChanged += (lineNumber, message) => state.Record(lineNumber, message);
            runner.MessageHandler = (message, title, closeAfter) =>
            {
                state.Record(null, "MESSAGE [" + title + "]: " + message);
                return true;
            };

            state.Runner = runner;
            runs[state.Id] = state;

            state.CompletionTask = Task.Run(() => Execute(state, runner, script));

            return state.Snapshot();
        }

        public ScriptRunInfo Get(string runId)
        {
            ScriptRunState state;
            return runs.TryGetValue(runId ?? string.Empty, out state) ? state.Snapshot() : null;
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

            state.Cancellation.Cancel();
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
            try
            {
                runner.Run(script);
                state.Complete(ScriptRunStatus.Succeeded, null);
            }
            catch (OperationCanceledException)
            {
                state.Complete(ScriptRunStatus.Cancelled, null);
            }
            catch (Exception ex)
            {
                state.Complete(ScriptRunStatus.Failed, ex.ToString());
            }
            finally
            {
                // capture while browser/excel instances are still alive
                CaptureArtifacts(state, runner);

                try
                {
                    runner.CleanUp();
                }
                catch (Exception)
                {
                }

                state.SignalCompletion();
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
            private readonly List<string> log = new List<string>();

            public ScriptRunState(string id, string scriptPath)
            {
                Id = id;
                ScriptPath = scriptPath;
                StartedAt = DateTimeOffset.Now;
                Status = ScriptRunStatus.Running;
                Cancellation = new CancellationTokenSource();
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

                    log.Add(lineNumber.HasValue ? "Line " + lineNumber + ": " + message : message);
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
                        Status = Status.ToString(),
                        StartedAt = StartedAt,
                        EndedAt = EndedAt,
                        CurrentLineNumber = CurrentLineNumber,
                        Error = Error,
                        Log = new List<string>(log)
                    };
                }
            }
        }
    }
}
