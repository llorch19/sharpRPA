using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Services;

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
            var runs = new ScriptRunManager();
            var recordings = CreateRecordingService();
            RecoverStagingFiles(recordings);

            var builder = Host.CreateApplicationBuilder(args);

            // stdout is reserved for JSON-RPC, so every log line must go to stderr
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(options => options.SingleLine = true);
            builder.Services.Configure<ConsoleLoggerOptions>(options =>
                options.LogToStandardErrorThreshold = LogLevel.Trace);

            builder.Services.AddSingleton(scripts);
            builder.Services.AddSingleton(commands);
            builder.Services.AddSingleton(runs);

            builder.Services.AddMcpServer(options =>
                {
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
                .WithTools(SharpRpaTools.Create(scripts, commands, runs, recordings));

            await builder.Build().RunAsync();
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
