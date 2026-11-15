using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Security;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;

namespace sharpRPA.Mcp
{
    /// <summary>
    /// Tool implementations. Every tool returns the { ok, data, page } envelope and
    /// reports expected failures through <see cref="ToolResult.Fail"/> so the agent gets
    /// a stable error code instead of a generic SDK error message.
    /// </summary>
    internal static class SharpRpaTools
    {
        // the log window is deliberately larger than a page: agents read it in one go
        private const int DefaultLogLines = 200;
        private const int MaxLogLines = 5000;

        public static McpServerTool[] Create(ScriptCatalog scripts, CommandCatalog commands, ScriptRunManager runs, RecordingService recordings, ArtifactStore artifacts, ToolHost security)
        {
            // one guard for every tool: unexpected faults become a structured INTERNAL error
            return new McpServerTool[]
            {
                // ---------------------------------------------------------------- scripts
                McpServerTool.Create(
                    (int limit = Cursor.DefaultLimit, string cursor = null) => ListScripts(scripts, limit, cursor),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_scripts",
                        Description = "List the sharpRPA script files available to this server. Paginated with limit/cursor.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string path, string mode = "full") => GetScript(scripts, security, path, mode),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_script",
                        Description = "Load a script. mode='summary' returns variables plus top-level commands only (use for large scripts); mode='full' expands nested commands with line numbers and depth. 'path' may be a file name inside the script folder or an absolute path.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string path) => ValidateScript(scripts, security, path),
                    new McpServerToolCreateOptions
                    {
                        Name = "validate_script",
                        Description = "Parse a script and report structural errors (missing end markers, duplicate variables) and warnings (unknown variable references). Returns ok=true with findings; ok=false only when the script could not be located or read.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string name, string xml = null) => Audited(security, "create_script", name, () => CreateScript(scripts, security, name, xml)),
                    new McpServerToolCreateOptions
                    {
                        Name = "create_script",
                        Description = "Create a new script file in the script folder. Pass xml to seed it (validated first), or omit it for an empty script.",
                        ReadOnly = false,
                        Destructive = true
                    }),

                McpServerTool.Create(
                    (string path, string xml) => Audited(security, "update_script", path, () => UpdateScript(scripts, security, path, xml)),
                    new McpServerToolCreateOptions
                    {
                        Name = "update_script",
                        Description = "Replace a script's XML. The payload is validated before it is written and the write is atomic.",
                        ReadOnly = false,
                        Destructive = true
                    }),

                // ---------------------------------------------------------------- commands
                McpServerTool.Create(
                    (int limit = Cursor.DefaultLimit, string cursor = null) => ListCommands(commands, limit, cursor),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_commands",
                        Description = "List automation commands with group, selection name and description. Paginated with limit/cursor.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string commandName) => DescribeCommand(commands, commandName),
                    new McpServerToolCreateOptions
                    {
                        Name = "describe_command",
                        Description = "Return the full parameter schema of a command: property names, descriptions, types, allowed options and whether variables/file pickers are supported.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                // ---------------------------------------------------------------- execution
                McpServerTool.Create(
                    (string path) => Audited(security, "run_script", path, () => RunScript(scripts, runs, security, path)),
                    new McpServerToolCreateOptions
                    {
                        Name = "run_script",
                        Description = "Start executing a script headlessly and return the run id. Use get_run_status/get_run_log to follow progress.",
                        ReadOnly = false,
                        Destructive = true,
                        OpenWorld = true
                    }),

                McpServerTool.Create(
                    (string runId) => GetRunStatus(runs, runId),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_run_status",
                        Description = "Return the status of a script run: running/succeeded/failed/cancelled, current line, error and log size.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string runId, int maxLines = 200) => GetRunLog(runs, runId, maxLines),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_run_log",
                        Description = "Return the most recent log lines of a script run. maxLines defaults to 200 and returns the tail of the log.",
                        ReadOnly = true,
                        Idempotent = true
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
                    (string status = null, int limit = Cursor.DefaultLimit, string cursor = null) =>
                        ListRuns(runs, status, limit, cursor),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_runs",
                        Description = "List script runs persisted in SQLite (newest first, survives a server restart). status may be running, succeeded, failed or cancelled. Paginated with limit/cursor.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string runId) => ListArtifacts(artifacts, runId),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_artifacts",
                        Description = "List the artifacts captured for a run (run log, browser screenshots and DOM snapshots).",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string artifactId, int maxBytes = 1048576) => GetArtifact(artifacts, artifactId, maxBytes),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_artifact",
                        Description = "Read one artifact. Text artifacts are returned inline as text, binary ones as base64, up to maxBytes (default 1MB). Larger artifacts return only the file path.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                // ---------------------------------------------------------------- recording
                McpServerTool.Create(
                    (string source, string title = null) => Audited(security, "record_start", source, () => StartRecording(recordings, source, title)),
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
                        Description = "Append one observed action to an in-progress recording. metadata may carry selector, selectorKind, kind, window, process, pageUrl or sensitive so the target index and privacy rules can be applied later.",
                        ReadOnly = false
                    }),

                McpServerTool.Create(
                    (string recordingId, string title = null, string scriptPath = null) => Audited(security, "record_stop", recordingId, () => StopRecording(recordings, recordingId, title, scriptPath)),
                    new McpServerToolCreateOptions
                    {
                        Name = "record_stop",
                        Description = "Finish a recording: flush the staged events, commit them to SQLite and refresh the target index.",
                        ReadOnly = false,
                        Destructive = true
                    }),

                McpServerTool.Create(
                    (string recordingId) => Audited(security, "record_abort", recordingId, () => AbortRecording(recordings, recordingId)),
                    new McpServerToolCreateOptions
                    {
                        Name = "record_abort",
                        Description = "Discard an in-progress recording without writing anything to the database.",
                        ReadOnly = false
                    }),

                McpServerTool.Create(
                    (int limit = Cursor.DefaultLimit, string cursor = null) => ListRecordings(recordings, limit, cursor),
                    new McpServerToolCreateOptions
                    {
                        Name = "list_recordings",
                        Description = "List committed recordings, newest first, with status and action counts. Paginated with limit/cursor.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string recordingId, bool includeEvents = true) => GetRecording(recordings, recordingId, includeEvents),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_recording",
                        Description = "Return one recording. Set includeEvents=false for metadata only.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                // ---------------------------------------------------------------- targets
                McpServerTool.Create(
                    (string query, string kind = null, int limit = Cursor.DefaultLimit, string cursor = null) =>
                        FindTargets(recordings, query, kind, limit, cursor),
                    new McpServerToolCreateOptions
                    {
                        Name = "find_targets",
                        Description = "Search the derived target index (files, urls, windows, ui elements) across scripts and recordings. kind may be file, url, window or ui_element. Paginated with limit/cursor.",
                        ReadOnly = true,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    () => Audited(security, "rebuild_target_index", null, () => StartRebuildIndex(security, recordings, scripts)),
                    new McpServerToolCreateOptions
                    {
                        Name = "rebuild_target_index",
                        Description = "Start rebuilding the target index from every script and recording. Returns a jobId immediately; poll get_job for the result. Safe to re-run: the index is derived data.",
                        ReadOnly = false,
                        Idempotent = true
                    }),

                McpServerTool.Create(
                    (string jobId) => GetJob(security, jobId),
                    new McpServerToolCreateOptions
                    {
                        Name = "get_job",
                        Description = "Return the status of a background job started by a tool such as rebuild_index.",
                        ReadOnly = true,
                        Idempotent = true
                    })
            }.Select(inner => (McpServerTool)new GuardedMcpServerTool(inner)).ToArray();
        }

        /// <summary>
        /// Runs a state changing tool and writes one audit row (ok / denied / error).
        /// </summary>
        private static object Audited(ToolHost security, string action, string target, Func<object> body)
        {
            object result;
            try
            {
                result = body();
            }
            catch (Exception ex)
            {
                RecordAudit(security, action, target, "error", ex.GetType().Name + ": " + ex.Message);
                throw;
            }

            var code = ExtractErrorCode(result);
            RecordAudit(security, action, target, code == null ? "ok" : "denied", code);
            return result;
        }

        private static void RecordAudit(ToolHost security, string action, string target, string outcome, string detail)
        {
            if (security.Audit == null)
            {
                return;
            }

            string client = null;
            try
            {
                client = security.ClientName == null ? null : security.ClientName();
            }
            catch (Exception)
            {
            }

            security.Audit.Record(client, action, target, outcome, detail);
        }

        /// <summary>Returns the error code of a failure envelope, or null when the tool succeeded.</summary>
        private static string ExtractErrorCode(object result)
        {
            var failure = result as CallToolResult;
            if ((failure == null) || (failure.IsError != true))
            {
                return null;
            }

            try
            {
                var block = failure.Content == null ? null : failure.Content.FirstOrDefault() as TextContentBlock;
                if (block == null)
                {
                    return "unknown";
                }

                using (var document = JsonDocument.Parse(block.Text))
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var code))
                    {
                        return code.GetString();
                    }
                }
            }
            catch (Exception)
            {
            }

            return "unknown";
        }

        // ------------------------------------------------------------- scripts

        private static object ListScripts(ScriptCatalog scripts, int limit, string cursor)
        {
            int offset;
            int size;
            object failure;
            if (!Cursor.TryStartPage(cursor, limit, out offset, out size, out failure))
            {
                return failure;
            }

            var page = Cursor.Paginate(scripts.ListScripts(), size, offset);
            return ToolResult.Ok(page.Item1, page.Item2);
        }

        /// <summary>
        /// Resolves a script path and enforces the configured script roots.
        /// Returns null when the path is rejected; <paramref name="failure"/> then holds the error.
        /// </summary>
        internal static string ResolveAllowedPath(ScriptCatalog scripts, ScriptPolicy policy, string path, out object failure)
        {
            failure = null;

            string resolved;
            try
            {
                resolved = scripts.ResolvePath(path);
            }
            catch (ArgumentException ex)
            {
                failure = ToolResult.Fail(ErrorCodes.InvalidArgument, ex.Message, "Provide a script file name or path.");
                return null;
            }

            if (policy.IsPathAllowed(resolved))
            {
                return resolved;
            }

            failure = ToolResult.Fail(
                ErrorCodes.PermissionDenied,
                "Path is outside the configured script roots: " + resolved,
                "Allowed roots: " + policy.DescribeRoots() + ". Update sharprpa.policy.json if this is intentional.");
            return null;
        }

        private static object GetScript(ScriptCatalog scripts, ToolHost security, string path, string mode)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "path is required.", "Use list_scripts to see available scripts.");
            }

            var normalizedMode = (mode ?? "full").Trim().ToLowerInvariant();
            if ((normalizedMode != "full") && (normalizedMode != "summary"))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "mode must be 'full' or 'summary'.", "Use summary for large scripts to keep the response small.");
            }

            object failure;
            var resolved = ResolveAllowedPath(scripts, security.Policy, path, out failure);
            if (failure != null)
            {
                return failure;
            }

            if (!File.Exists(resolved))
            {
                return ToolResult.Fail(ErrorCodes.NotFound, "Script file not found: " + resolved, "Use list_scripts to see available scripts.");
            }

            ScriptDetail detail;
            try
            {
                detail = scripts.GetScript(resolved);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is InvalidOperationException)
            {
                return ToolResult.Fail(ErrorCodes.ParseFailed, "Script could not be parsed: " + ex.Message, "Run validate_script to see the structural problems.");
            }

            var commands = normalizedMode == "summary"
                ? detail.Commands.Where(command => command.Depth == 0).ToList()
                : detail.Commands;

            return ToolResult.Ok(new
            {
                detail.Path,
                detail.Variables,
                commandCount = detail.Commands.Count,
                mode = normalizedMode,
                commands
            });
        }

        private static object ValidateScript(ScriptCatalog scripts, ToolHost security, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "path is required.", "Use list_scripts to see available scripts.");
            }

            object failure;
            var resolved = ResolveAllowedPath(scripts, security.Policy, path, out failure);
            if (failure != null)
            {
                return failure;
            }

            if (!File.Exists(resolved))
            {
                return ToolResult.Fail(ErrorCodes.NotFound, "Script file not found: " + resolved, "Use list_scripts to see available scripts.");
            }

            return ToolResult.Ok(scripts.ValidateScript(resolved));
        }

        private static object CreateScript(ScriptCatalog scripts, ToolHost security, string name, string xml)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "name is required.", "Provide a file name such as 'my-flow'.");
            }

            var nameError = ValidateScriptName(name);
            if (nameError != null)
            {
                return nameError;
            }

            if (!string.IsNullOrWhiteSpace(xml))
            {
                var invalidXml = ValidateXmlOrError(scripts, xml);
                if (invalidXml != null)
                {
                    return invalidXml;
                }
            }

            string path;
            try
            {
                path = scripts.CreateScript(name, xml);
            }
            catch (IOException)
            {
                return ToolResult.Fail(ErrorCodes.AlreadyExists, "A script named '" + name + "' already exists.", "Use update_script to modify it.");
            }

            return ToolResult.Ok(new { path, created = true });
        }

        /// <summary>
        /// A script name must be a plain file name: folders are implicit (the script folder)
        /// and allowing separators would turn "sub/flow" into a misleading ALREADY_EXISTS.
        /// </summary>
        private static object ValidateScriptName(string name)
        {
            if ((name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                || (name.IndexOf('/') >= 0)
                || (name.IndexOf('\\') >= 0))
            {
                return ToolResult.Fail(
                    ErrorCodes.InvalidArgument,
                    "name must be a plain file name without folders.",
                    "Use a name such as 'my-flow'; the file is created inside the script folder.");
            }

            if (name.Length > 200)
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "name is too long.", "Use at most 200 characters.");
            }

            return null;
        }

        private static object UpdateScript(ScriptCatalog scripts, ToolHost security, string path, string xml)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "path is required.", "Use list_scripts to see available scripts.");
            }

            if (string.IsNullOrWhiteSpace(xml))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "xml is required.", "Pass the complete script document to replace the file contents.");
            }

            var invalidXml = ValidateXmlOrError(scripts, xml);
            if (invalidXml != null)
            {
                return invalidXml;
            }

            object failure;
            var resolved = ResolveAllowedPath(scripts, security.Policy, path, out failure);
            if (failure != null)
            {
                return failure;
            }

            if (!File.Exists(resolved))
            {
                return ToolResult.Fail(ErrorCodes.NotFound, "Script file not found: " + resolved, "Use create_script to create it first.");
            }

            scripts.UpdateScript(resolved, xml);
            var validation = scripts.ValidateXml(xml);

            return ToolResult.Ok(new
            {
                path = resolved,
                updated = true,
                warnings = validation.Warnings
            });
        }

        /// <summary>Returns null when the XML is acceptable, otherwise a failure result.</summary>
        private static object ValidateXmlOrError(ScriptCatalog scripts, string xml)
        {
            var validation = scripts.ValidateXml(xml);
            if (validation.IsValid)
            {
                return null;
            }

            return ToolResult.Fail(
                ErrorCodes.ParseFailed,
                "Script XML was rejected: " + string.Join("; ", validation.Errors),
                "Fix the structural errors listed, or compare with the current file via validate_script.");
        }

        // ------------------------------------------------------------- commands

        private static object ListCommands(CommandCatalog commands, int limit, string cursor)
        {
            int offset;
            int size;
            object failure;
            if (!Cursor.TryStartPage(cursor, limit, out offset, out size, out failure))
            {
                return failure;
            }

            var page = Cursor.Paginate(commands.ListSummaries(), size, offset);
            return ToolResult.Ok(page.Item1, page.Item2);
        }

        private static object DescribeCommand(CommandCatalog commands, string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "commandName is required.", "Use list_commands to see available commands.");
            }

            var info = commands.Describe(commandName);
            if (info == null)
            {
                return ToolResult.Fail(
                    ErrorCodes.NotFound,
                    "Unknown command: '" + commandName + "'.",
                    "Use list_commands to see available commands, or describe_command with the type name such as 'VariableCommand'.");
            }

            return ToolResult.Ok(info);
        }

        // ------------------------------------------------------------- execution

        private static object RunScript(ScriptCatalog scripts, ScriptRunManager runs, ToolHost security, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "path is required.", "Use list_scripts to see available scripts.");
            }

            object failure;
            var resolved = ResolveAllowedPath(scripts, security.Policy, path, out failure);
            if (failure != null)
            {
                return failure;
            }

            if (!File.Exists(resolved))
            {
                return ToolResult.Fail(ErrorCodes.NotFound, "Script file not found: " + resolved, "Use list_scripts to see available scripts.");
            }

            ScriptRunInfo run;
            try
            {
                var denied = FindDeniedCommands(scripts, security.Policy, resolved);
                if (denied.Count > 0)
                {
                    return ToolResult.Fail(
                        ErrorCodes.PermissionDenied,
                        "Script uses denied command(s): " + string.Join(", ", denied),
                        "Remove them or update deniedCommandTypes in sharprpa.policy.json.");
                }

                run = runs.Start(resolved, security.Policy.RunTimeoutSeconds);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is InvalidOperationException)
            {
                return ToolResult.Fail(ErrorCodes.ParseFailed, "Script could not be parsed: " + ex.Message, "Run validate_script to see the structural problems.");
            }

            return ToolResult.Ok(new
            {
                id = run.Id,
                run.Status,
                run.StartedAt,
                run.ScriptPath
            });
        }

        private static object GetRunStatus(ScriptRunManager runs, string runId)
        {
            var failure = RunNotFound(runs, runId, out var run);
            if (failure != null)
            {
                return failure;
            }

            return ToolResult.Ok(ToStatus(run));
        }

        private static object GetRunLog(ScriptRunManager runs, string runId, int maxLines)
        {
            var failure = RunNotFound(runs, runId, out var run);
            if (failure != null)
            {
                return failure;
            }

            var lines = run.Log;
            var size = maxLines <= 0 ? DefaultLogLines : Math.Min(maxLines, MaxLogLines);
            var tail = (size >= lines.Count) ? lines : lines.Skip(lines.Count - size).ToList();

            return ToolResult.Ok(new RunLogResponse
            {
                Id = run.Id,
                Status = run.Status,
                TotalLines = run.LogLineCount > 0 ? run.LogLineCount : lines.Count,
                Lines = tail
            });
        }

        private static object CancelRun(ScriptRunManager runs, string runId)
        {
            var failure = RunNotFound(runs, runId, out var run);
            if (failure != null)
            {
                return failure;
            }

            var cancelled = runs.Cancel(run.Id);

            return ToolResult.Ok(new
            {
                id = run.Id,
                cancelled,
                status = runs.Get(run.Id).Status
            });
        }

        private static object RunNotFound(ScriptRunManager runs, string runId, out ScriptRunInfo run)
        {
            run = runs.Get(runId);
            if (run != null)
            {
                return null;
            }

            return ToolResult.Fail(
                ErrorCodes.NotFound,
                "Unknown run id: '" + runId + "'.",
                "Run the script first; runs do not survive a server restart yet.");
        }

        private static object ListRuns(ScriptRunManager runs, string status, int limit, string cursor)
        {
            int offset;
            int size;
            object failure;
            if (!Cursor.TryStartPage(cursor, limit, out offset, out size, out failure))
            {
                return failure;
            }

            if (!string.IsNullOrWhiteSpace(status)
                && Array.IndexOf(new[] { "running", "succeeded", "failed", "cancelled" }, status.Trim().ToLowerInvariant()) < 0)
            {
                return ToolResult.Fail(
                    ErrorCodes.InvalidArgument,
                    "status must be one of running, succeeded, failed, cancelled.",
                    "Omit status to list every run.");
            }

            var items = runs.ListRuns(status, size, offset);
            var page = Cursor.Page(runs.CountRuns(status), offset, size, items.Count);

            return ToolResult.Ok(items, page);
        }

        private static RunStatusResponse ToStatus(ScriptRunInfo run)
        {
            return new RunStatusResponse
            {
                Id = run.Id,
                ScriptPath = run.ScriptPath,
                Status = run.Status,
                StartedAt = run.StartedAt,
                EndedAt = run.EndedAt,
                CurrentLineNumber = run.CurrentLineNumber,
                Error = run.Error,
                LogLineCount = run.LogLineCount
            };
        }

        /// <summary>Returns the denied command types used by a script, or an empty list when none are denied.</summary>
        internal static List<string> FindDeniedCommands(ScriptCatalog scripts, ScriptPolicy policy, string path)
        {
            if (policy.DeniedCommandTypes.Count == 0)
            {
                return new List<string>();
            }

            var script = scripts.LoadScript(path);

            return ScriptCatalog.Walk(script.Commands)
                .Select(action => action.ScriptCommand)
                .Where(command => (command != null) && !policy.IsCommandAllowed(command.GetType().Name))
                .Select(command => command.GetType().Name)
                .Distinct()
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        // ------------------------------------------------------------- artifacts

        private static object ListArtifacts(ArtifactStore artifacts, string runId)
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "runId is required.", "Start a run with run_script first.");
            }

            var items = artifacts.List(runId);

            return ToolResult.Ok(new { runId, total = items.Count, artifacts = items });
        }

        private static object GetArtifact(ArtifactStore artifacts, string artifactId, int maxBytes)
        {
            if (string.IsNullOrWhiteSpace(artifactId))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "artifactId is required.", "Use list_artifacts to see available artifacts.");
            }

            var info = artifacts.Get(artifactId);
            if (info == null)
            {
                return ToolResult.Fail(ErrorCodes.NotFound, "Unknown artifact id: '" + artifactId + "'.", "Use list_artifacts to see available artifacts.");
            }

            if (!File.Exists(info.Path))
            {
                return ToolResult.Fail(
                    ErrorCodes.NotFound,
                    "Artifact file no longer exists: " + info.Path,
                    "Re-run the script to regenerate it.");
            }

            var actualBytes = new FileInfo(info.Path).Length;
            var size = maxBytes <= 0 ? 1048576 : maxBytes;

            if (actualBytes > size)
            {
                return ToolResult.Ok(new
                {
                    artifactId = info.Id,
                    info.RunId,
                    info.Kind,
                    info.Mime,
                    bytes = actualBytes,
                    info.Path,
                    encoding = "omitted",
                    hint = "Artifact is larger than maxBytes (" + size + "). Read the file directly or raise maxBytes."
                });
            }

            if (IsTextMime(info.Mime))
            {
                return ToolResult.Ok(new
                {
                    artifactId = info.Id,
                    info.RunId,
                    info.Kind,
                    info.Mime,
                    bytes = actualBytes,
                    info.Path,
                    encoding = "text",
                    content = File.ReadAllText(info.Path)
                });
            }

            return ToolResult.Ok(new
            {
                artifactId = info.Id,
                info.RunId,
                info.Kind,
                info.Mime,
                bytes = actualBytes,
                info.Path,
                encoding = "base64",
                content = Convert.ToBase64String(File.ReadAllBytes(info.Path))
            });
        }

        private static bool IsTextMime(string mime)
        {
            if (string.IsNullOrEmpty(mime))
            {
                return false;
            }

            return mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || mime.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0
                || mime.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0
                || mime.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------- recording

        private static object StartRecording(RecordingService recordings, string source, string title)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "source is required.", "Use 'browser' or 'windows-desktop'.");
            }

            var session = recordings.Start(source, title);

            return ToolResult.Ok(new RecordStartResponse
            {
                Id = session.Id,
                Source = session.Source,
                Title = session.Title,
                StartedAt = session.StartedAt,
                Status = "recording"
            });
        }

        private static object RecordEvent(
            RecordingService recordings,
            string recordingId,
            string actionType,
            string target,
            string value,
            Dictionary<string, string> metadata)
        {
            var failure = RecordingNotActive(recordings, recordingId);
            if (failure != null)
            {
                return failure;
            }

            if (string.IsNullOrWhiteSpace(actionType))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "actionType is required.", "Use 'click', 'type', 'navigate', 'key', 'open' ...");
            }

            var count = recordings.Record(recordingId, new RecordedAction
            {
                ActionType = actionType,
                Target = target,
                Value = value,
                Metadata = metadata ?? new Dictionary<string, string>(),
                Timestamp = DateTimeOffset.Now
            });

            return ToolResult.Ok(new RecordEventResponse { Id = recordingId, ActionCount = count });
        }

        private static object StopRecording(RecordingService recordings, string recordingId, string title, string scriptPath)
        {
            var failure = RecordingNotActive(recordings, recordingId);
            if (failure != null)
            {
                return failure;
            }

            return ToolResult.Ok(recordings.Stop(recordingId, title, scriptPath));
        }

        private static object AbortRecording(RecordingService recordings, string recordingId)
        {
            var failure = RecordingNotActive(recordings, recordingId);
            if (failure != null)
            {
                return failure;
            }

            recordings.Abort(recordingId);

            return ToolResult.Ok(new { id = recordingId, aborted = true, status = "aborted" });
        }

        private static object RecordingNotActive(RecordingService recordings, string recordingId)
        {
            if (string.IsNullOrWhiteSpace(recordingId))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "recordingId is required.", "Use record_start first.");
            }

            if (recordings.ActiveIds.Contains(recordingId))
            {
                return null;
            }

            // distinguish "already committed" from "never existed" so the agent knows
            // whether to inspect it with get_recording or to start a new one
            if (recordings.GetInfo(recordingId) != null)
            {
                return ToolResult.Fail(
                    ErrorCodes.AlreadyFinished,
                    "Recording '" + recordingId + "' has already been finished.",
                    "Use get_recording to inspect it, or record_start to begin a new capture.");
            }

            return ToolResult.Fail(
                ErrorCodes.NotInProgress,
                "No recording is in progress with id '" + recordingId + "'.",
                "The session may have been aborted, or recovered as incomplete after a restart. Use list_recordings to check.");
        }

        private static object ListRecordings(RecordingService recordings, int limit, string cursor)
        {
            int offset;
            int size;
            object failure;
            if (!Cursor.TryStartPage(cursor, limit, out offset, out size, out failure))
            {
                return failure;
            }

            var items = recordings.List(size, offset);
            var page = Cursor.Page(recordings.CountRecordings(), offset, size, items.Count);

            return ToolResult.Ok(items, page);
        }

        private static object GetRecording(RecordingService recordings, string recordingId, bool includeEvents)
        {
            if (string.IsNullOrWhiteSpace(recordingId))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "recordingId is required.", "Use list_recordings to see available recordings.");
            }

            var detail = recordings.Get(recordingId, includeEvents);
            if (detail == null)
            {
                return ToolResult.Fail(ErrorCodes.NotFound, "Unknown recording id: '" + recordingId + "'.", "Use list_recordings to see available recordings.");
            }

            return ToolResult.Ok(detail);
        }

        // ------------------------------------------------------------- targets

        private static object FindTargets(RecordingService recordings, string query, string kind, int limit, string cursor)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "query is required.", "Search for a path, url, window title or selector fragment.");
            }

            int offset;
            int size;
            object failure;
            if (!Cursor.TryStartPage(cursor, limit, out offset, out size, out failure))
            {
                return failure;
            }

            var items = recordings.FindTargets(query, kind, size, offset);
            var page = Cursor.Page(recordings.CountTargets(query, kind), offset, size, items.Count);

            return ToolResult.Ok(items, page);
        }

        private static object StartRebuildIndex(ToolHost security, RecordingService recordings, ScriptCatalog scripts)
        {
            var job = security.Jobs.Start("rebuild_index",
                () => (object)new { indexed = recordings.RebuildTargetIndex(scripts) });

            return ToolResult.Ok(new { jobId = job.Id, job.Kind, job.Status, job.StartedAt });
        }

        private static object GetJob(ToolHost security, string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                return ToolResult.Fail(ErrorCodes.InvalidArgument, "jobId is required.", "Use the jobId returned by rebuild_target_index.");
            }

            var job = security.Jobs.Get(jobId);
            if (job == null)
            {
                return ToolResult.Fail(
                    ErrorCodes.NotFound,
                    "Unknown job id: '" + jobId + "'.",
                    "Jobs are process-local and disappear on restart. Jobs wrap idempotent work, so it is safe to start it again.");
            }

            return ToolResult.Ok(job);
        }
    }

    /// <summary>Policy, audit trail and client identity shared by the mutating tools.</summary>
    internal sealed class ToolHost
    {
        public ScriptPolicy Policy { get; set; } = new ScriptPolicy();
        public AuditStore Audit { get; set; }
        public Func<string> ClientName { get; set; }
        public JobRegistry Jobs { get; set; } = new JobRegistry();
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
}
