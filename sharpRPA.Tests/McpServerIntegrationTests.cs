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
using sharpRPA.Core.Storage;
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
            startInfo.Environment["SHARPRPA_DB"] = Path.Combine(scriptFolder.Directory, "sharprpa-mcp.db");

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
                    "create_script",
                    "describe_command",
                    "find_targets",
                    "get_artifact",
                    "get_job",
                    "get_recording",
                    "get_run_log",
                    "get_run_status",
                    "get_script",
                    "list_artifacts",
                    "list_commands",
                    "list_recordings",
                    "list_runs",
                    "list_scripts",
                    "rebuild_target_index",
                    "record_abort",
                    "record_event",
                    "record_start",
                    "record_stop",
                    "run_script",
                    "update_script",
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
            var envelope = Envelope(result);

            Assert.True(envelope.GetProperty("ok").GetBoolean(), envelope.GetRawText());
            Assert.Contains("agent-script.xml", ResultText(result));

            var page = envelope.GetProperty("page");
            Assert.True(page.GetProperty("total").GetInt32() >= 1);
            // the SDK omits null properties, so "no cursor" means last page
            Assert.False(page.TryGetProperty("nextCursor", out _), "last page must not advertise a cursor");
        }

        [Fact]
        public async Task ListCommands_ReturnsTheCatalogueAndPaginates()
        {
            var result = await CallAsync("list_commands", new { limit = 5 });
            var envelope = Envelope(result);

            Assert.True(envelope.GetProperty("ok").GetBoolean(), envelope.GetRawText());

            var items = envelope.GetProperty("data").EnumerateArray().ToList();
            Assert.Equal(5, items.Count);
            Assert.All(items, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("typeName").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("group").GetString()));
            });

            var total = envelope.GetProperty("page").GetProperty("total").GetInt32();
            Assert.True(total > 5, "expected more than one page of commands");
            Assert.True(envelope.GetProperty("page").TryGetProperty("nextCursor", out _), "first page must advertise a cursor");
        }

        [Fact]
        public async Task DescribeCommand_ReturnsTheParameterSchema()
        {
            var result = await CallAsync("describe_command", new { commandName = "VariableCommand" });
            var text = ResultText(result);

            Assert.True(Envelope(result).GetProperty("ok").GetBoolean());
            Assert.Contains("v_userVariableName", text);
            Assert.Contains("Set Variable", text);
        }

        [Fact]
        public async Task DescribeCommand_UnknownCommandFailsWithACode()
        {
            var result = await CallAsync("describe_command", new { commandName = "NopeCommand" });

            AssertIsError(result);

            var envelope = Envelope(result);
            Assert.False(envelope.GetProperty("ok").GetBoolean());
            Assert.Equal("NOT_FOUND", envelope.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("list_commands", envelope.GetProperty("error").GetProperty("hint").GetString());
        }

        [Fact]
        public async Task GetScript_MissingFileFailsWithNotFound()
        {
            var result = await CallAsync("get_script", new { path = "does-not-exist.xml" });

            AssertIsError(result);
            Assert.Equal("NOT_FOUND", Envelope(result).GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task GetScript_CorruptFileFailsWithParseFailed()
        {
            File.WriteAllText(Path.Combine(scriptFolder.Directory, "corrupt.xml"), "this is not xml at all");

            var result = await CallAsync("get_script", new { path = "corrupt.xml" });

            AssertIsError(result);
            Assert.Equal("PARSE_FAILED", Envelope(result).GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task ListScripts_InvalidCursorFailsWithInvalidArgument()
        {
            var result = await CallAsync("list_scripts", new { cursor = "not-a-cursor" });

            AssertIsError(result);
            Assert.Equal("INVALID_ARGUMENT", Envelope(result).GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task CreateScript_RejectsFolderNames()
        {
            var result = await CallAsync("create_script", new { name = "sub/flow" });

            AssertIsError(result);
            var envelope = Envelope(result);
            Assert.Equal("INVALID_ARGUMENT", envelope.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("plain file name", envelope.GetProperty("error").GetProperty("message").GetString());
        }

        [Fact]
        public async Task GetScript_SummaryModeOnlyReturnsTopLevelCommands()
        {
            CopySampleScript("String Split Sample.xml", "nested.xml");

            var full = await CallAsync("get_script", new { path = "nested.xml", mode = "full" });
            var summary = await CallAsync("get_script", new { path = "nested.xml", mode = "summary" });

            var fullRoot = Data(full);
            var summaryRoot = Data(summary);

            Assert.Equal("full", fullRoot.GetProperty("mode").GetString());
            Assert.Equal("summary", summaryRoot.GetProperty("mode").GetString());

            var totalCount = fullRoot.GetProperty("commandCount").GetInt32();
            Assert.True(totalCount > 1, "sample script should have nested commands");

            var summaryCount = summaryRoot.GetProperty("commands").GetArrayLength();
            Assert.True(summaryCount < totalCount, "summary must drop nested commands");
            Assert.All(summaryRoot.GetProperty("commands").EnumerateArray(), command => Assert.Equal(0, command.GetProperty("depth").GetInt32()));
        }

        [Fact]
        public async Task CreateAndUpdateScript_RoundTrip()
        {
            var created = await CallAsync("create_script", new { name = "agent-created" });
            var createdRoot = Data(created);
            Assert.True(createdRoot.GetProperty("created").GetBoolean());
            var path = createdRoot.GetProperty("path").GetString();
            Assert.EndsWith("agent-created.xml", path);

            var conflict = await CallAsync("create_script", new { name = "agent-created" });
            AssertIsError(conflict);
            Assert.Equal("ALREADY_EXISTS", Envelope(conflict).GetProperty("error").GetProperty("code").GetString());

            // seed with a real script, then replace it
            CopySampleScript("If Conditional Sample.xml", "seeded.xml");
            var seedXml = File.ReadAllText(Path.Combine(scriptFolder.Directory, "seeded.xml"));

            var updated = await CallAsync("update_script", new { path = "agent-created.xml", xml = seedXml });
            Assert.True(Data(updated).GetProperty("updated").GetBoolean());

            var reloaded = await CallAsync("get_script", new { path = "agent-created.xml", mode = "summary" });
            Assert.True(Data(reloaded).GetProperty("commandCount").GetInt32() > 0);

            var invalid = await CallAsync("update_script", new { path = "agent-created.xml", xml = "<not-a-script/>" });
            AssertIsError(invalid);
            Assert.Equal("PARSE_FAILED", Envelope(invalid).GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task Pagination_ReturnsCursorAndTotal()
        {
            CopySampleScript("String Split Sample.xml", "page-two.xml");

            var firstPage = await CallAsync("list_scripts", new { limit = 1 });
            var firstEnvelope = Envelope(firstPage);
            var firstItems = firstEnvelope.GetProperty("data").EnumerateArray().ToList();
            Assert.Single(firstItems);

            var nextCursor = firstEnvelope.GetProperty("page").GetProperty("nextCursor").GetString();
            Assert.False(string.IsNullOrEmpty(nextCursor));
            Assert.Equal(2, firstEnvelope.GetProperty("page").GetProperty("total").GetInt32());

            var secondPage = await CallAsync("list_scripts", new { limit = 1, cursor = nextCursor });
            var secondEnvelope = Envelope(secondPage);
            var secondItems = secondEnvelope.GetProperty("data").EnumerateArray().ToList();
            Assert.Single(secondItems);
            Assert.NotEqual(firstItems[0].GetProperty("name").GetString(), secondItems[0].GetProperty("name").GetString());
            Assert.False(secondEnvelope.GetProperty("page").TryGetProperty("nextCursor", out _), "last page must not advertise a cursor");
        }

        [Fact]
        public async Task ValidateAndRunScript_ToCompletion()
        {
            var validation = await CallAsync("validate_script", new { path = "agent-script.xml" });
            Assert.True(Envelope(validation).GetProperty("ok").GetBoolean());
            Assert.Contains("\"isValid\":true", ResultText(validation));

            var run = await CallAsync("run_script", new { path = "agent-script.xml" });
            var runId = Data(run).GetProperty("id").GetString();
            Assert.False(string.IsNullOrEmpty(runId));

            ScriptRunStatusResponse status = null;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var statusResult = await CallAsync("get_run_status", new { runId });
                status = ParseStatus(Data(statusResult).GetRawText());
                if (status.Status != "running")
                {
                    break;
                }

                await Task.Delay(100);
            }

            Assert.NotNull(status);
            Assert.Equal("succeeded", status.Status);
            Assert.Null(status.Error);

            var logResult = await CallAsync("get_run_log", new { runId, maxLines = 50 });
            Assert.Contains("Running Line", ResultText(logResult));

            // the run is persisted, so it shows up in history and can be filtered by status
            var listedRuns = await CallAsync("list_runs", new { });
            var listedRunsRoot = Envelope(listedRuns);
            Assert.True(listedRunsRoot.GetProperty("ok").GetBoolean(), listedRunsRoot.GetRawText());
            Assert.Contains(runId, ResultText(listedRuns));

            var succeeded = await CallAsync("list_runs", new { status = "succeeded" });
            Assert.Contains(runId, ResultText(succeeded));

            var badStatus = await CallAsync("list_runs", new { status = "nope" });
            AssertIsError(badStatus);
            Assert.Equal("INVALID_ARGUMENT", Envelope(badStatus).GetProperty("error").GetProperty("code").GetString());

            // the run log is also persisted as an artifact so the agent can read it as a file
            var artifactList = await CallAsync("list_artifacts", new { runId });
            var artifacts = Data(artifactList);
            Assert.Equal(1, artifacts.GetProperty("total").GetInt32());
            var artifactId = artifacts.GetProperty("artifacts")[0].GetProperty("id").GetString();
            Assert.Equal("log", artifacts.GetProperty("artifacts")[0].GetProperty("kind").GetString());

            var artifact = await CallAsync("get_artifact", new { artifactId });
            var artifactRoot = Data(artifact);
            Assert.Equal("text", artifactRoot.GetProperty("encoding").GetString());
            Assert.Contains("Running Line", artifactRoot.GetProperty("content").GetString());

            var missingArtifact = await CallAsync("get_artifact", new { artifactId = "nope" });
            AssertIsError(missingArtifact);
            Assert.Equal("NOT_FOUND", Envelope(missingArtifact).GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task RecordLifecycle_CommitsToSqliteAndIndexesTargets()
        {
            var started = await CallAsync("record_start", new { source = "browser", title = "mcp capture" });
            var recordingId = Data(started).GetProperty("id").GetString();
            Assert.False(string.IsNullOrEmpty(recordingId));

            var firstEvent = await CallAsync("record_event", new
            {
                recordingId,
                actionType = "click",
                target = "//button[@id='save']",
                metadata = new Dictionary<string, string>
                {
                    ["kind"] = "ui_element",
                    ["selectorKind"] = "xpath",
                    ["pageUrl"] = "https://example.com/checkout"
                }
            });
            Assert.Contains("\"actionCount\":1", ResultText(firstEvent));

            await CallAsync("record_event", new
            {
                recordingId,
                actionType = "navigate",
                target = "https://example.com/checkout"
            });

            var stopped = await CallAsync("record_stop", new { recordingId, title = "mcp capture" });
            var stoppedText = ResultText(stopped);
            Assert.Contains("\"status\":\"completed\"", stoppedText);
            Assert.Contains("\"actionCount\":2", stoppedText);
            Assert.Contains("\"title\":\"mcp capture\"", stoppedText);

            // stopping an already committed recording is a distinct error from an unknown id
            var stoppedAgain = await CallAsync("record_stop", new { recordingId });
            AssertIsError(stoppedAgain);
            Assert.Equal("ALREADY_FINISHED", Envelope(stoppedAgain).GetProperty("error").GetProperty("code").GetString());

            var listed = await CallAsync("list_recordings", new { });
            Assert.Contains(recordingId, ResultText(listed));

            var detail = await CallAsync("get_recording", new { recordingId });
            {
                var root = Data(detail);
                Assert.Equal("completed", root.GetProperty("status").GetString());
                Assert.Equal(2, root.GetProperty("events").GetArrayLength());
                Assert.Equal("//button[@id='save']", root.GetProperty("events")[0].GetProperty("target").GetString());
                Assert.Equal("https://example.com/checkout", root.GetProperty("events")[1].GetProperty("target").GetString());
            }

            var targets = await CallAsync("find_targets", new { query = "save" });
            var targetText = ResultText(targets);
            Assert.Contains("ui_element", targetText);
            Assert.Contains("xpath", targetText);

            var rebuild = await CallAsync("rebuild_target_index", new { });
            var jobId = Data(rebuild).GetProperty("jobId").GetString();
            Assert.False(string.IsNullOrEmpty(jobId));
            Assert.Equal("rebuild_index", Data(rebuild).GetProperty("kind").GetString());

            var job = await WaitForJob(jobId);
            Assert.Equal("succeeded", job.GetProperty("status").GetString());
            Assert.True(job.GetProperty("result").GetProperty("indexed").GetInt32() > 0);

            // only after the rebuild finished does the index reflect the recording
            var targetsAfterRebuild = await CallAsync("find_targets", new { query = "save" });
            Assert.Contains("ui_element", ResultText(targetsAfterRebuild));

            var unknownJob = await CallAsync("get_job", new { jobId = "nope" });
            AssertIsError(unknownJob);
            Assert.Equal("NOT_FOUND", Envelope(unknownJob).GetProperty("error").GetProperty("code").GetString());
        }

        private async Task<JsonElement> WaitForJob(string jobId)
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                var response = await CallAsync("get_job", new { jobId });
                var job = Data(response);
                if (job.GetProperty("status").GetString() != "running")
                {
                    return job;
                }

                await Task.Delay(20);
            }

            Assert.Fail("job did not reach a terminal state in time");
            return default;
        }

        [Fact]
        public async Task RecordAbort_WritesNothing()
        {
            var started = await CallAsync("record_start", new { source = "windows-desktop" });
            var recordingId = Data(started).GetProperty("id").GetString();

            await CallAsync("record_event", new { recordingId, actionType = "click", target = "OK" });

            var aborted = await CallAsync("record_abort", new { recordingId });
            Assert.True(Data(aborted).GetProperty("aborted").GetBoolean(), ResultText(aborted));

            var listed = await CallAsync("list_recordings", new { });
            Assert.DoesNotContain(recordingId, ResultText(listed));

            var rejected = await CallAsync("record_stop", new { recordingId });
            AssertIsError(rejected);
            Assert.Equal("NOT_IN_PROGRESS", Envelope(rejected).GetProperty("error").GetProperty("code").GetString());

            // an id that never existed reports the same code
            var unknown = await CallAsync("record_stop", new { recordingId = "never-existed" });
            AssertIsError(unknown);
            Assert.Equal("NOT_IN_PROGRESS", Envelope(unknown).GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public async Task Resources_AreListedAndReadable()
        {
            var list = await RequestAsync("resources/list", new { });
            var resources = list.GetProperty("result").GetProperty("resources").EnumerateArray()
                .Select(resource => resource.GetProperty("uri").GetString())
                .ToList();

            Assert.Contains("sharprpa://scripts", resources);
            Assert.Contains("sharprpa://recordings", resources);
            // the templated script resource is advertised through resources/templates/list
            Assert.DoesNotContain("sharprpa://script/{path}", resources);

            var templates = await RequestAsync("resources/templates/list", new { });
            var templateUris = templates.GetProperty("result").GetProperty("resourceTemplates").EnumerateArray()
                .Select(template => template.GetProperty("uriTemplate").GetString())
                .ToList();
            Assert.Contains("sharprpa://script/{path}", templateUris);

            var readScripts = await RequestAsync("resources/read", new { uri = "sharprpa://scripts" });
            var scriptsText = readScripts.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString();
            Assert.Contains("agent-script.xml", scriptsText);

            var readScript = await RequestAsync("resources/read", new { uri = "sharprpa://script/agent-script.xml" });
            var scriptText = readScript.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString();
            Assert.Contains("<Script", scriptText);
        }

        [Fact]
        public async Task Prompts_AreListedAndReturnTheirProcedure()
        {
            var list = await RequestAsync("prompts/list", new { });
            var prompts = list.GetProperty("result").GetProperty("prompts").EnumerateArray()
                .ToDictionary(
                    prompt => prompt.GetProperty("name").GetString(),
                    prompt => prompt.GetProperty("arguments").EnumerateArray()
                        .Select(argument => argument.GetProperty("name").GetString()).ToList());

            Assert.Equal(new[] { "optimise-script", "record-flow" }, prompts.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
            Assert.Equal(new[] { "scriptPath" }, prompts["optimise-script"]);
            Assert.Equal(new[] { "source" }, prompts["record-flow"]);

            var get = await RequestAsync("prompts/get", new
            {
                name = "optimise-script",
                arguments = new { scriptPath = "agent-script.xml" }
            });

            var text = get.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString();
            Assert.Contains("agent-script.xml", text);
            Assert.Contains("validate_script", text);
            Assert.Contains("update_script", text);
        }

        [Fact]
        public async Task RunScript_OutsideTheScriptRootIsDeniedAndAudited()
        {
            var outside = Path.Combine(Path.GetTempPath(), "sharprpa-outside-" + Guid.NewGuid().ToString("N") + ".xml");
            File.Copy(Path.Combine(scriptFolder.Directory, "agent-script.xml"), outside);

            try
            {
                var result = await CallAsync("run_script", new { path = outside });
                AssertIsError(result);
                Assert.Equal("PERMISSION_DENIED", Envelope(result).GetProperty("error").GetProperty("code").GetString());

                // the denial is on the audit trail, readable straight from the database
                var audit = new AuditStore(new SqliteDatabase(Path.Combine(scriptFolder.Directory, "sharprpa-mcp.db")));
                var entry = audit.List().FirstOrDefault(row => row.Action == "run_script");

                Assert.NotNull(entry);
                Assert.Equal("denied", entry.Outcome);
                Assert.Equal("PERMISSION_DENIED", entry.Detail);
            }
            finally
            {
                if (File.Exists(outside))
                {
                    File.Delete(outside);
                }
            }
        }

        [Fact]
        public async Task CancelRun_StopsARunningScript()
        {
            // a 30s pause guarantees the run is still alive when we cancel it
            var script = new sharpRPA.Core.Script.Script();
            script.Commands.Add(new sharpRPA.Core.Script.ScriptAction
            {
                ScriptCommand = new PauseCommand { v_PauseLength = 30000 }
            });
            File.WriteAllText(
                Path.Combine(scriptFolder.Directory, "slow.xml"),
                sharpRPA.Core.Script.Script.SerializeToString(script));

            var run = await CallAsync("run_script", new { path = "slow.xml" });
            var runId = Data(run).GetProperty("id").GetString();

            // wait until it is observably running
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var status = await CallAsync("get_run_status", new { runId });
                if (Data(status).GetProperty("status").GetString() == "running")
                {
                    break;
                }

                await Task.Delay(50);
            }

            var cancelled = await CallAsync("cancel_run", new { runId });
            Assert.True(Data(cancelled).GetProperty("cancelled").GetBoolean(), ResultText(cancelled));

            for (var attempt = 0; attempt < 100; attempt++)
            {
                var status = await CallAsync("get_run_status", new { runId });
                if (Data(status).GetProperty("status").GetString() != "running")
                {
                    Assert.Equal("cancelled", Data(status).GetProperty("status").GetString());
                    return;
                }

                await Task.Delay(100);
            }

            Assert.Fail("run did not reach a terminal state after cancel_run");
        }

        private static ScriptRunStatusResponse ParseStatus(string json)
        {
            return JsonSerializer.Deserialize<ScriptRunStatusResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private static void AssertIsError(JsonElement result)
        {
            // the SDK omits isError when it is false, so treat a missing flag as success
            Assert.True(result.TryGetProperty("isError", out var flag) && flag.GetBoolean(),
                "expected an error result but got: " + result.GetRawText());
        }

        private static string ResultText(JsonElement result)
        {
            var content = result.GetProperty("content").EnumerateArray().First();
            return content.GetProperty("text").GetString();
        }

        /// <summary>Parses the { ok, data, page } envelope returned by every tool.</summary>
        private static JsonElement Envelope(JsonElement result)
        {
            using (var document = JsonDocument.Parse(ResultText(result)))
            {
                return document.RootElement.Clone();
            }
        }

        private static JsonElement Data(JsonElement result)
        {
            return Envelope(result).GetProperty("data");
        }

        private void CopySampleScript(string sampleFileName, string targetFileName)
        {
            var source = Path.Combine(AppContext.BaseDirectory, "Samples", sampleFileName);
            File.Copy(source, Path.Combine(scriptFolder.Directory, targetFileName), true);
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
