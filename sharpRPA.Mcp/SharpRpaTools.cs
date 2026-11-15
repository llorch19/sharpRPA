using System;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Server;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;

namespace sharpRPA.Mcp
{
    internal static class SharpRpaTools
    {
        public static McpServerTool[] Create(ScriptCatalog scripts, CommandCatalog commands, ScriptRunManager runs, RecordingService recordings)
        {
            return new McpServerTool[]
            {
                McpServerTool.Create(
                    () => scripts.ListScripts(),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_scripts",
                        Description = "List the sharpRPA script files available to this server.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string path) => scripts.GetScript(path),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_script",
                        Description = "Load a script and return its variables and flattened command list with line numbers and nesting depth. 'path' may be a file name inside the script folder or an absolute path.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string path) => scripts.ValidateScript(path),
                    new McpServerToolCreateOptions
                    {
                        Name = "validate_script",
                        Description = "Parse a script and report structural errors (missing end markers, duplicate variables) and warnings (unknown variable references).",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    () => commands.ListSummaries(),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_commands",
                        Description = "List every automation command with its group, selection name and description.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string commandName) => Describe(commands, commandName),
                    new McpServerToolCreateOptions
                    {
                        Name = "describe_command",
                        Description = "Return the full parameter schema of a command: property names, descriptions, types, allowed options and whether variables/file pickers are supported.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string path) => StartRun(scripts, runs, path),
                    new McpServerToolCreateOptions
                    {
                        Name = "run_script",
                        Description = "Start executing a script headlessly and return the run id. Use get_run_status/get_run_log to follow progress.",
                        ReadOnly = false,
                        Destructive = true
                    }),

                McpServerTool.Create(
                    (string runId) => GetStatus(runs, runId),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_run_status",
                        Description = "Return the status of a script run: running/succeeded/failed/cancelled, current line, error and log size.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string runId, int maxLines = 200) => GetLog(runs, runId, maxLines),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_run_log",
                        Description = "Return the most recent log lines of a script run. maxLines defaults to 200 and returns the tail of the log.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string runId) => CancelRun(runs, runId),
                    new McpServerToolCreateOptions
                    {
                        Name = "cancel_run",
                        Description = "Request cancellation of a running script.",
                        ReadOnly = false
                    }),

                McpServerTool.Create(
                    (string source, string title = null) => StartRecording(recordings, source, title),
                    new McpServerToolCreateOptions
                    {
                        Name = "record_start",
                        Description = "Start a recording session. 'source' identifies the capture channel, for example 'browser' or 'windows-desktop'. Events are staged to disk and only committed to the database by record_stop.",
                        ReadOnly = false,
                        Destructive = true
                    }),

                McpServerTool.Create(
                    (string recordingId, string actionType, string target = null, string value = null, Dictionary<string, string> metadata = null) =>
                        RecordEvent(recordings, recordingId, actionType, target, value, metadata),
                    new McpServerToolCreateOptions
                    {
                        Name = "record_event",
                        Description = "Append one observed action to an in-progress recording. metadata may carry selector, selectorKind, kind, window, process or pageUrl so the target index can be derived later.",
                        ReadOnly = false
                    }),

                McpServerTool.Create(
                    (string recordingId, string title = null, string scriptPath = null) => StopRecording(recordings, recordingId, title, scriptPath),
                    new McpServerToolCreateOptions
                    {
                        Name = "record_stop",
                        Description = "Finish a recording: flush the staged events, commit them to SQLite and refresh the target index.",
                        ReadOnly = false,
                        Destructive = true
                    }),

                McpServerTool.Create(
                    (string recordingId) => AbortRecording(recordings, recordingId),
                    new McpServerToolCreateOptions
                    {
                        Name = "record_abort",
                        Description = "Discard an in-progress recording without writing anything to the database.",
                        ReadOnly = false
                    }),

                McpServerTool.Create(
                    () => recordings.List(),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_recordings",
                        Description = "List committed recordings, newest first, with status and action counts.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string recordingId, bool includeEvents = true) => GetRecording(recordings, recordingId, includeEvents),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_recording",
                        Description = "Return one recording. Set includeEvents=false for metadata only.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    (string query, string kind = null) => FindTargets(recordings, query, kind),
                    new McpServerToolCreateOptions
                    {
                        Name = "find_targets",
                        Description = "Search the derived target index (files, urls, windows, ui elements) across scripts and recordings. kind may be file, url, window or ui_element.",
                        ReadOnly = true
                    }),

                McpServerTool.Create(
                    () => RebuildTargetIndex(recordings, scripts),
                    new McpServerToolCreateOptions
                    {
                        Name = "rebuild_target_index",
                        Description = "Rebuild the target index from every script and recording. Safe to run at any time; the index is derived data.",
                        ReadOnly = false
                    })
            };
        }

        private static RecordStartResponse StartRecording(RecordingService recordings, string source, string title)
        {
            var session = recordings.Start(source, title);

            return new RecordStartResponse
            {
                Id = session.Id,
                Source = session.Source,
                Title = session.Title,
                StartedAt = session.StartedAt,
                Status = "recording"
            };
        }

        private static RecordEventResponse RecordEvent(
            RecordingService recordings,
            string recordingId,
            string actionType,
            string target,
            string value,
            Dictionary<string, string> metadata)
        {
            if (string.IsNullOrWhiteSpace(actionType))
            {
                throw new ArgumentException("actionType is required (for example 'click', 'type', 'navigate').");
            }

            var count = recordings.Record(recordingId, new RecordedAction
            {
                ActionType = actionType,
                Target = target,
                Value = value,
                Metadata = metadata ?? new Dictionary<string, string>(),
                Timestamp = DateTimeOffset.Now
            });

            return new RecordEventResponse { Id = recordingId, ActionCount = count };
        }

        private static RecordingInfo StopRecording(RecordingService recordings, string recordingId, string title, string scriptPath)
        {
            return recordings.Stop(recordingId, title, scriptPath);
        }

        private static CancelResponse AbortRecording(RecordingService recordings, string recordingId)
        {
            var aborted = recordings.Abort(recordingId);

            return new CancelResponse
            {
                Id = recordingId,
                Cancelled = aborted,
                Status = aborted ? "aborted" : "unknown"
            };
        }

        private static RecordingDetail GetRecording(RecordingService recordings, string recordingId, bool includeEvents)
        {
            var detail = recordings.Get(recordingId, includeEvents);
            if (detail == null)
            {
                throw new ArgumentException("Unknown recording id: '" + recordingId + "'. Use list_recordings to see available recordings.");
            }

            return detail;
        }

        private static List<TargetHit> FindTargets(RecordingService recordings, string query, string kind)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                throw new ArgumentException("A search query is required.");
            }

            return recordings.FindTargets(query, kind);
        }

        private static RebuildIndexResponse RebuildTargetIndex(RecordingService recordings, ScriptCatalog scripts)
        {
            return new RebuildIndexResponse { Indexed = recordings.RebuildTargetIndex(scripts) };
        }

        private static CommandInfo Describe(CommandCatalog commands, string commandName)
        {
            var info = commands.Describe(commandName);
            if (info == null)
            {
                throw new ArgumentException("Unknown command: '" + commandName + "'. Use list_commands to see available commands.");
            }

            return info;
        }

        private static ScriptRunInfo StartRun(ScriptCatalog scripts, ScriptRunManager runs, string path)
        {
            return runs.Start(scripts.ResolvePath(path));
        }

        private static RunStatusResponse GetStatus(ScriptRunManager runs, string runId)
        {
            var run = RequireRun(runs, runId);
            return new RunStatusResponse
            {
                Id = run.Id,
                ScriptPath = run.ScriptPath,
                Status = run.Status,
                StartedAt = run.StartedAt,
                EndedAt = run.EndedAt,
                CurrentLineNumber = run.CurrentLineNumber,
                Error = run.Error,
                LogLineCount = run.Log.Count
            };
        }

        private static RunLogResponse GetLog(ScriptRunManager runs, string runId, int maxLines)
        {
            var run = RequireRun(runs, runId);
            var lines = run.Log;
            var tail = maxLines <= 0 || maxLines >= lines.Count
                ? lines
                : lines.Skip(lines.Count - maxLines).ToList();

            return new RunLogResponse
            {
                Id = run.Id,
                Status = run.Status,
                TotalLines = lines.Count,
                Lines = tail
            };
        }

        private static CancelResponse CancelRun(ScriptRunManager runs, string runId)
        {
            var run = RequireRun(runs, runId);
            var cancelled = runs.Cancel(run.Id);

            return new CancelResponse
            {
                Id = run.Id,
                Cancelled = cancelled,
                Status = runs.Get(run.Id).Status
            };
        }

        private static ScriptRunInfo RequireRun(ScriptRunManager runs, string runId)
        {
            var run = runs.Get(runId);
            if (run == null)
            {
                throw new ArgumentException("Unknown run id: '" + runId + "'. Use run_script first.");
            }

            return run;
        }
    }

    internal sealed class RunStatusResponse
    {
        public string Id { get; set; }
        public string ScriptPath { get; set; }
        public string Status { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int? CurrentLineNumber { get; set; }
        public string Error { get; set; }
        public int LogLineCount { get; set; }
    }

    internal sealed class RunLogResponse
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public int TotalLines { get; set; }
        public List<string> Lines { get; set; }
    }

    internal sealed class CancelResponse
    {
        public string Id { get; set; }
        public bool Cancelled { get; set; }
        public string Status { get; set; }
    }

    internal sealed class RecordStartResponse
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Title { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public string Status { get; set; }
    }

    internal sealed class RecordEventResponse
    {
        public string Id { get; set; }
        public long ActionCount { get; set; }
    }

    internal sealed class RebuildIndexResponse
    {
        public int Indexed { get; set; }
    }
}
