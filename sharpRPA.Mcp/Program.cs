using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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
                        "list_commands/describe_command to learn the automation vocabulary, and run_script followed by " +
                        "get_run_status/get_run_log to execute a script and observe it. Scripts that use Windows-only " +
                        "commands (window, UI Automation, Excel, OCR) require the Windows build of this server.";
                })
                .WithStdioServerTransport()
                .WithTools(SharpRpaTools.Create(scripts, commands, runs));

            await builder.Build().RunAsync();
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
