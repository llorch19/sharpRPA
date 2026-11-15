using System;
using System.Collections.Generic;
using System.IO;
using ModelContextProtocol.Protocol;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Security;
using sharpRPA.Core.Services;
using sharpRPA.Mcp;
using Xunit;

namespace sharpRPA.Tests
{
    /// <summary>
    /// Exercises the policy helpers directly (the MCP project exposes them to this
    /// assembly) instead of driving a whole server for every case.
    /// </summary>
    public class McpPolicyTests
    {
        private static string ErrorCode(object failure)
        {
            var call = Assert.IsType<CallToolResult>(failure);
            Assert.True(call.IsError);
            var block = Assert.IsType<TextContentBlock>(Assert.Single(call.Content));
            using (var document = System.Text.Json.JsonDocument.Parse(block.Text))
            {
                return document.RootElement.GetProperty("error").GetProperty("code").GetString();
            }
        }

        [Fact]
        public void ResolveAllowedPath_ReturnsPathsInsideTheRoot()
        {
            using (var folder = new TempScriptDirectory())
            {
                var scripts = new ScriptCatalog(folder.Directory);
                var policy = new ScriptPolicy();
                policy.UseDefaultRoot(folder.Directory);

                object failure;
                var resolved = SharpRpaTools.ResolveAllowedPath(scripts, policy, "flow.xml", out failure);

                Assert.Null(failure);
                Assert.Equal(Path.Combine(folder.Directory, "flow.xml"), resolved);
            }
        }

        [Fact]
        public void ResolveAllowedPath_DeniesPathsOutsideTheRoot()
        {
            using (var folder = new TempScriptDirectory())
            {
                var scripts = new ScriptCatalog(folder.Directory);
                var policy = new ScriptPolicy();
                policy.UseDefaultRoot(folder.Directory);

                var outside = Path.Combine(Path.GetTempPath(), "sharprpa-outside.xml");
                object failure;
                var resolved = SharpRpaTools.ResolveAllowedPath(scripts, policy, outside, out failure);

                Assert.Null(resolved);
                Assert.Equal("PERMISSION_DENIED", ErrorCode(failure));
            }
        }

        [Fact]
        public void ResolveAllowedPath_RejectsAnEmptyPath()
        {
            using (var folder = new TempScriptDirectory())
            {
                var scripts = new ScriptCatalog(folder.Directory);

                object failure;
                var resolved = SharpRpaTools.ResolveAllowedPath(scripts, new ScriptPolicy(), "  ", out failure);

                Assert.Null(resolved);
                Assert.Equal("INVALID_ARGUMENT", ErrorCode(failure));
            }
        }

        [Fact]
        public void FindDeniedCommands_ReturnsEmptyWhenNothingIsDenied()
        {
            using (var folder = new TempScriptDirectory())
            {
                var path = folder.Write("allowed.xml", new List<ScriptCommand>
                {
                    new StartProcessCommand { v_ProgramName = "calc" }
                });

                var denied = SharpRpaTools.FindDeniedCommands(
                    new ScriptCatalog(folder.Directory), new ScriptPolicy(), path);

                Assert.Empty(denied);
            }
        }

        [Fact]
        public void FindDeniedCommands_ReportsDistinctDeniedTypes()
        {
            using (var folder = new TempScriptDirectory())
            {
                var path = folder.Write("denied.xml", new List<ScriptCommand>
                {
                    new StartProcessCommand { v_ProgramName = "calc" },
                    new CommentCommand { v_Comment = "fine" },
                    new RunScriptCommand { v_ScriptPath = "other.bat" },
                    new StartProcessCommand { v_ProgramName = "notepad" }
                });

                var policy = new ScriptPolicy
                {
                    DeniedCommandTypes = new List<string> { "StartProcessCommand", "runscriptcommand" }
                };

                var denied = SharpRpaTools.FindDeniedCommands(new ScriptCatalog(folder.Directory), policy, path);

                Assert.Equal(new[] { "RunScriptCommand", "StartProcessCommand" }, denied);
            }
        }
    }
}
