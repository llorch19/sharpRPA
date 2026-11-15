using System;
using System.Collections.Generic;
using System.Linq;
using ModelContextProtocol.Server;
using sharpRPA.Core.Services;

namespace sharpRPA.Mcp
{
    internal static class SharpRpaTools
    {
        public static McpServerTool[] Create(ScriptCatalog scripts, CommandCatalog commands, ScriptRunManager runs)
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
                    })
            };
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
}
