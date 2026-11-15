using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Security;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;

namespace sharpRPA.Mcp
{
    /// <summary>
    /// Stdio MCP server that exposes sharpRPA scripts, the command catalogue and
    /// headless script execution to AI agents such as opencode.
    /// </summary>
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            RegisterPlatformCommands();

            var scriptDirectory = Environment.GetEnvironmentVariable("SHARPRPA_SCRIPT_DIR");
            var scripts = new ScriptCatalog(scriptDirectory);
            var commands = new CommandCatalog();
            var recordings = CreateRecordingService();
            RecoverStagingFiles(recordings);
            var artifacts = new ArtifactStore(recordings.Database, recordings.RunsDirectory);
            var runStore = new RunStore(recordings.Database);
            RecoverInterruptedRuns(runStore);
            var runs = new ScriptRunManager(artifacts, runStore);

            // execution policy: roots default to the script folder, so run_script cannot
            // be pointed at an arbitrary file unless the operator opts in
            var workspaceDirectory = Path.GetDirectoryName(recordings.DatabasePath);
            var policy = ScriptPolicy.Load(workspaceDirectory: workspaceDirectory);
            policy.UseDefaultRoot(scripts.ScriptDirectory);
            var audit = new AuditStore(recordings.Database);

            Console.Error.WriteLine("sharpRPA MCP: script roots: " + policy.DescribeRoots());
            Console.Error.WriteLine("sharpRPA MCP: run timeout: " + policy.RunTimeoutSeconds + "s, denied commands: " +
                (policy.DeniedCommandTypes.Count == 0 ? "(none)" : string.Join(", ", policy.DeniedCommandTypes)));

            var builder = Host.CreateApplicationBuilder(args);

            // stdout is reserved for JSON-RPC, so every log line must go to stderr
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
            builder.Services.Configure<ConsoleLoggerOptions>(options =>
                options.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services.AddSingleton(scripts);
            builder.Services.AddSingleton(commands);
            builder.Services.AddSingleton(runs);

            McpServerOptions serverOptions = null;

            builder.Services.AddMcpServer(options =>
                {
                    serverOptions = options;
                    options.ServerInfo = new Implementation
                    {
                        Name = "sharprpa",
                        Version = "0.0.9"
                    };
                    options.ServerInstructions =
                        "sharpRPA automation server. Use list_scripts/get_script/validate_script to inspect scripts, " +
                        "list_commands/describe_command to learn the automation vocabulary, run_script followed by " +
                        "get_run_status/get_run_log to execute a script and observe it, record_start/record_event/" +
                        "record_stop to capture actions (staged to disk, committed to SQLite on stop), and " +
                        "find_targets to search files, urls, windows and UI elements referenced by scripts or recordings. " +
                        "Scripts that use Windows-only commands (window, UI Automation, Excel, OCR) require the Windows build of this server.";
                })
                .WithStdioServerTransport()
                .WithResources(CreateResources(scripts, recordings))
                .WithPrompts(CreatePrompts(scripts))
                .WithTools(SharpRpaTools.Create(scripts, commands, runs, recordings, artifacts, new ToolHost
                {
                    Policy = policy,
                    Audit = audit,
                    // read lazily: the options object is populated during initialize
                    ClientName = () => serverOptions == null || serverOptions.KnownClientInfo == null
                        ? null
                        : serverOptions.KnownClientInfo.Name
                }));

            await builder.Build().RunAsync();
        }

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Read-only resources so agents can pull script/recording content through the
        /// resource API instead of round-tripping it through tool results.
        /// </summary>
        private static McpServerResource[] CreateResources(ScriptCatalog scripts, RecordingService recordings)
        {
            return new McpServerResource[]
            {
                McpServerResource.Create(
                    () => JsonSerializer.Serialize(
                        scripts.ListScripts().Select(script => new
                        {
                            name = script.Name,
                            path = script.Path,
                            sizeBytes = script.SizeBytes,
                            lastModified = script.LastModified
                        }),
                        JsonOptions),
                    new McpServerResourceCreateOptions
                    {
                        Name = "scripts",
                        Title = "Scripts",
                        Description = "All sharpRPA scripts available to this server.",
                        MimeType = "application/json",
                        UriTemplate = "sharprpa://scripts"
                    }),

                McpServerResource.Create(
                    (string path) =>
                    {
                        // URIs arrive percent-encoded ("If%20Conditional%20Sample.xml")
                        var decoded = Uri.UnescapeDataString(path ?? string.Empty).TrimStart('/');
                        var resolved = scripts.ResolvePath(decoded);
                        if (!File.Exists(resolved))
                        {
                            throw new FileNotFoundException("Script not found: " + decoded, resolved);
                        }

                        return File.ReadAllText(resolved);
                    },
                    new McpServerResourceCreateOptions
                    {
                        Name = "script",
                        Title = "Script",
                        Description = "A single sharpRPA script document (XML).",
                        MimeType = "application/xml",
                        UriTemplate = "sharprpa://script/{path}"
                    }),

                McpServerResource.Create(
                    () => JsonSerializer.Serialize(
                        recordings.List().Select(recording => new
                        {
                            id = recording.Id,
                            source = recording.Source,
                            status = recording.Status,
                            title = recording.Title,
                            actionCount = recording.ActionCount,
                            startedAt = recording.StartedAt
                        }),
                        JsonOptions),
                    new McpServerResourceCreateOptions
                    {
                        Name = "recordings",
                        Title = "Recordings",
                        Description = "Committed recordings, newest first.",
                        MimeType = "application/json",
                        UriTemplate = "sharprpa://recordings"
                    })
            };
        }

        /// <summary>
        /// Reusable multi-step prompts. The body is a checklist so an agent starts from
        /// the same procedure every time instead of rediscovering it.
        /// </summary>
        private static McpServerPrompt[] CreatePrompts(ScriptCatalog scripts)
        {
            return new McpServerPrompt[]
            {
                McpServerPrompt.Create(
                    (string scriptPath) => BuildPrompt(
                        "Optimise the sharpRPA script at '" + scriptPath + "'.",
                        "optimise-script",
                        new[]
                        {
                            "1. Call validate_script and fix every reported error before anything else.",
                            "2. Call get_script with mode=summary to see the structure, then mode=full for the lines you intend to change.",
                            "3. Replace hard-coded paths, urls and credentials with [variables]; validate again.",
                            "4. Call find_targets to see where the selectors you touch are also used, so a change is not made in isolation.",
                            "5. Describe each edit you plan before making it, then apply it with update_script (pass the complete XML).",
                            "6. Re-run validate_script, then run_script and read get_run_log until the run reaches a terminal state.",
                            "7. Read get_artifact for the run log/screenshots if the result is unclear."
                        }),
                    new McpServerPromptCreateOptions
                    {
                        Name = "optimise-script",
                        Description = "End-to-end procedure for improving an existing sharpRPA script safely."
                    }),

                McpServerPrompt.Create(
                    (string source) => BuildPrompt(
                        "Record a new sharpRPA flow from the '" + source + "' capture channel.",
                        "record-flow",
                        new[]
                        {
                            "1. Call record_start with the capture channel (browser or windows-desktop) and keep the returned recordingId.",
                            "2. Feed observed actions with record_event; set metadata.selector, metadata.selectorKind, metadata.kind and metadata.window/pageUrl so the target index is useful.",
                            "3. Mark sensitive fields with metadata.sensitive=true - never record passwords.",
                            "4. Call record_stop to commit; the staged events are flushed to SQLite and indexed.",
                            "5. Call get_recording to review the captured actions, then build the script and validate it before running."
                        }),
                    new McpServerPromptCreateOptions
                    {
                        Name = "record-flow",
                        Description = "Procedure for capturing a new flow with the recording tools."
                    })
            };
        }

        private static GetPromptResult BuildPrompt(string goal, string name, IEnumerable<string> steps)
        {
            var text = goal + "\n\nProcedure (" + name + "):\n" + string.Join("\n", steps);

            return new GetPromptResult
            {
                Description = goal,
                Messages = new List<PromptMessage>
                {
                    new PromptMessage { Role = Role.User, Content = new TextContentBlock { Text = text } }
                }
            };
        }

        private static RecordingService CreateRecordingService()
        {
            try
            {
                return new RecordingService();
            }
            catch (Exception ex)
            {
                // the workspace folder may be read-only; keep the server usable for everything else
                Console.Error.WriteLine("sharpRPA MCP: workspace database unavailable (" + ex.Message + "), using temp storage.");
                var fallback = Path.Combine(Path.GetTempPath(), "sharprpa");
                return new RecordingService(
                    Path.Combine(fallback, "sharprpa.db"),
                    Path.Combine(fallback, "recording-staging"));
            }
        }

        private static void RecoverInterruptedRuns(RunStore runStore)
        {
            try
            {
                var recovered = runStore.MarkInterruptedRuns();
                if (recovered > 0)
                {
                    Console.Error.WriteLine("sharpRPA MCP: marked " + recovered + " interrupted run(s) as failed.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("sharpRPA MCP: run recovery failed: " + ex.Message);
            }
        }

        private static void RecoverStagingFiles(RecordingService recordings)
        {
            try
            {
                var recovered = recordings.CollectStaging();
                if (recovered > 0)
                {
                    Console.Error.WriteLine("sharpRPA MCP: recovered " + recovered + " interrupted recording(s).");
                }

                Console.Error.WriteLine("sharpRPA MCP: database at " + recordings.DatabasePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("sharpRPA MCP: staging recovery failed: " + ex.Message);
            }
        }

        private static void RegisterPlatformCommands()
        {
#if WINDOWS_COMMANDS
            var windowsAssembly = typeof(Core.AutomationCommands.User32Functions).Assembly;
            Core.Common.RegisterCommandAssembly(windowsAssembly);
            Core.Script.Script.RegisterKnownTypes(windowsAssembly);
            Console.Error.WriteLine("sharpRPA MCP: Windows command layer registered (" + windowsAssembly.GetName().Name + ").");
#else
            Console.Error.WriteLine("sharpRPA MCP: running without the Windows command layer; window/UI Automation/Excel/OCR commands are unavailable.");
#endif
        }
    }
}
