using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using sharpRPA.Core.AutomationCommands;
using Xunit;

namespace sharpRPA.Tests
{
    /// <summary>
    /// Drives the real MCP stdio server as a child process and speaks JSON-RPC to it,
    /// proving the wire protocol works end to end.
    /// </summary>
    public class McpServerIntegrationTests : IAsyncLifetime
    {
        private readonly object ioLock = new object();
        private readonly StringBuilder stderr = new StringBuilder();
        private Process process;
        private TempScriptDirectory scriptFolder;
        private int nextId;

        public async Task InitializeAsync()
        {
            scriptFolder = new TempScriptDirectory();
            scriptFolder.Write("agent-script.xml", new List<ScriptCommand>
            {
                new CommentCommand { v_Comment = "created by agent" },
                new PauseCommand { v_PauseLength = 1 }
            });

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "\"" + FindMcpDll() + "\"",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["SHARPRPA_SCRIPT_DIR"] = scriptFolder.Directory;

            process = Process.Start(startInfo);
            Assert.NotNull(process);
            process.ErrorDataReceived += (sender, args) =>
            {
                if (args.Data != null)
                {
                    lock (stderr)
                    {
                        stderr.AppendLine(args.Data);
                    }
                }
            };
            process.BeginErrorReadLine();

            var initialize = await RequestAsync("initialize", new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "sharpRPA-tests", version = "1.0" }
            });

            var serverInfo = initialize.GetProperty("result").GetProperty("serverInfo");
            Assert.Equal("sharprpa", serverInfo.GetProperty("name").GetString());

            await NotifyAsync("notifications/initialized");
        }

        public Task DisposeAsync()
        {
            try
            {
                if ((process != null) && !process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                process?.Dispose();
                scriptFolder?.Dispose();
            }

            return Task.CompletedTask;
        }

        [Fact]
        public async Task Server_ExposesTheExpectedToolset()
        {
            var response = await RequestAsync("tools/list");
            var toolList = response.GetProperty("result").GetProperty("tools");
            var tools = toolList.EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .ToList();

            Assert.Equal(
                new[]
                {
                    "cancel_run",
                    "describe_command",
                    "get_run_log",
                    "get_run_status",
                    "get_script",
                    "list_commands",
                    "list_scripts",
                    "run_script",
                    "validate_script"
                },
                tools.OrderBy(name => name, StringComparer.Ordinal).ToArray());

            var runScript = toolList.EnumerateArray()
                .First(tool => tool.GetProperty("name").GetString() == "run_script");
            Assert.False(runScript.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
        }

        [Fact]
        public async Task ListScripts_ReturnsTheConfiguredScriptFolder()
        {
            var result = await CallAsync("list_scripts", new { });

            Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), result.GetRawText());
            Assert.Contains("agent-script.xml", ResultText(result));
        }

        [Fact]
        public async Task DescribeCommand_ReturnsTheParameterSchema()
        {
            var result = await CallAsync("describe_command", new { commandName = "VariableCommand" });
            var text = ResultText(result);

            Assert.Contains("v_userVariableName", text);
            Assert.Contains("Set Variable", text);
        }

        [Fact]
        public async Task DescribeCommand_UnknownCommandFails()
        {
            try
            {
                var result = await CallAsync("describe_command", new { commandName = "NopeCommand" });
                Assert.True(result.GetProperty("isError").GetBoolean(), result.GetRawText());
            }
            catch (InvalidOperationException ex)
            {
                // acceptable alternative: the SDK surfaced the tool exception as a JSON-RPC error
                Assert.Contains("Unknown command", ex.Message);
            }
        }

        [Fact]
        public async Task ValidateAndRunScript_ToCompletion()
        {
            var validation = await CallAsync("validate_script", new { path = "agent-script.xml" });
            Assert.Contains("\"isValid\":true", ResultText(validation));

            var run = await CallAsync("run_script", new { path = "agent-script.xml" });
            var runId = JsonDocument.Parse(ResultText(run)).RootElement.GetProperty("id").GetString();
            Assert.False(string.IsNullOrEmpty(runId));

            ScriptRunStatusResponse status = null;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var statusResult = await CallAsync("get_run_status", new { runId });
                status = ParseStatus(ResultText(statusResult));
                if (status.Status != "Running")
                {
                    break;
                }

                await Task.Delay(100);
            }

            Assert.NotNull(status);
            Assert.Equal("Succeeded", status.Status);
            Assert.Null(status.Error);

            var logResult = await CallAsync("get_run_log", new { runId, maxLines = 50 });
            Assert.Contains("Running Line", ResultText(logResult));
        }

        private static ScriptRunStatusResponse ParseStatus(string json)
        {
            return JsonSerializer.Deserialize<ScriptRunStatusResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private static string ResultText(JsonElement result)
        {
            var content = result.GetProperty("content").EnumerateArray().First();
            return content.GetProperty("text").GetString();
        }

        private async Task<JsonElement> CallAsync(string name, object arguments)
        {
            var response = await RequestAsync("tools/call", new { name, arguments });
            return response.GetProperty("result");
        }

        private Task NotifyAsync(string method)
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = method
            });

            SendLine(payload);
            return Task.CompletedTask;
        }

        private async Task<JsonElement> RequestAsync(string method, object parameters = null)
        {
            var id = Interlocked.Increment(ref nextId);
            var payload = new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method
            };

            if (parameters != null)
            {
                payload["params"] = parameters;
            }

            SendLine(JsonSerializer.Serialize(payload));

            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                var line = await ReadLineAsync(deadline);
                using (var document = JsonDocument.Parse(line))
                {
                    var root = document.RootElement;

                    // the server may talk to us first (roots/list, ping, ...): answer and keep reading
                    if (root.TryGetProperty("method", out var serverMethod))
                    {
                        if (root.TryGetProperty("id", out var requestId))
                        {
                            ReplyMethodNotFound(requestId);
                        }

                        continue;
                    }

                    if (!HasMatchingId(root, id))
                    {
                        continue;
                    }

                    if (root.TryGetProperty("error", out var error))
                    {
                        throw new InvalidOperationException("MCP error: " + error.GetRawText() + StderrText());
                    }

                    if (root.TryGetProperty("result", out _))
                    {
                        return root.Clone();
                    }

                    throw new InvalidOperationException("Unexpected MCP message: " + line + StderrText());
                }
            }

            throw new TimeoutException("Timed out waiting for MCP response." + StderrText());
        }

        private static bool HasMatchingId(JsonElement root, int expected)
        {
            if (!root.TryGetProperty("id", out var responseId))
            {
                return false;
            }

            return responseId.ValueKind switch
            {
                JsonValueKind.Number => responseId.GetInt32() == expected,
                JsonValueKind.String => responseId.GetString() == expected.ToString(),
                _ => false
            };
        }

        private void ReplyMethodNotFound(JsonElement requestId)
        {
            SendLine(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = requestId.Clone(),
                ["error"] = new Dictionary<string, object>
                {
                    ["code"] = -32601,
                    ["message"] = "Method not supported by the test client"
                }
            }));
        }

        private void SendLine(string payload)
        {
            lock (ioLock)
            {
                process.StandardInput.WriteLine(payload);
                process.StandardInput.Flush();
            }
        }

        private async Task<string> ReadLineAsync(DateTime deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.FromMilliseconds(1);
            }

            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(remaining);
            if (line == null)
            {
                throw new InvalidOperationException("MCP server closed stdout." + StderrText());
            }

            return line;
        }

        private string StderrText()
        {
            lock (stderr)
            {
                return Environment.NewLine + "--- server stderr ---" + Environment.NewLine + stderr;
            }
        }

        private static string FindMcpDll()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while ((directory != null) && !File.Exists(Path.Combine(directory.FullName, "sharpRPA.sln")))
            {
                directory = directory.Parent;
            }

            Assert.True(directory != null, "Could not locate repository root from " + AppContext.BaseDirectory);

            var parts = AppContext.BaseDirectory.Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            var binIndex = Array.LastIndexOf(parts, "bin");
            var configuration = (binIndex >= 0) && (binIndex + 1 < parts.Length) ? parts[binIndex + 1] : "Debug";

            var dll = Path.Combine(directory.FullName, "sharpRPA.Mcp", "bin", configuration, "net10.0", "sharpRPA.Mcp.dll");
            Assert.True(File.Exists(dll), "MCP server was not built: " + dll + " (run 'dotnet build sharpRPA.sln')");

            return dll;
        }

        private sealed class ScriptRunStatusResponse
        {
            public string Id { get; set; }
            public string Status { get; set; }
            public string Error { get; set; }
        }
    }
}
