using System;
using System.IO;
using sharpRPA.Core.Security;
using Xunit;

namespace sharpRPA.Tests
{
    public class ScriptPolicyTests
    {
        [Fact]
        public void Defaults_AreUnrestrictedUntilARootIsDeclared()
        {
            var policy = new ScriptPolicy();

            Assert.Empty(policy.ScriptRoots);
            Assert.True(policy.IsPathAllowed(Path.Combine(Path.GetTempPath(), "anything.xml")));
            Assert.True(policy.IsCommandAllowed("StartProcessCommand"));
            Assert.Equal(300, policy.RunTimeoutSeconds);
            Assert.Equal("(unrestricted)", policy.DescribeRoots());
        }

        [Fact]
        public void UseDefaultRoot_AllowsInsideAndDeniesOutside()
        {
            var root = Path.Combine(Path.GetTempPath(), "sharprpa-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            try
            {
                var policy = new ScriptPolicy();
                policy.UseDefaultRoot(root);

                Assert.True(policy.IsPathAllowed(Path.Combine(root, "flow.xml")));
                Assert.True(policy.IsPathAllowed(Path.Combine(root, "nested", "flow.xml")));
                Assert.False(policy.IsPathAllowed(Path.Combine(Path.GetTempPath(), "outside.xml")));

                // a second call must not add a duplicate root
                policy.UseDefaultRoot(root);
                Assert.Single(policy.ScriptRoots);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void UseDefaultRoot_IgnoresEmptyDirectory()
        {
            var policy = new ScriptPolicy();
            policy.UseDefaultRoot(" ");
            policy.UseDefaultRoot(null);

            Assert.Empty(policy.ScriptRoots);
        }

        [Fact]
        public void Load_ReadsTheWorkspacePolicyFileCaseInsensitively()
        {
            var workspace = Path.Combine(Path.GetTempPath(), "sharprpa-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try
            {
                File.WriteAllText(Path.Combine(workspace, "sharprpa.policy.json"), @"{
                    // comments and trailing commas are tolerated
                    ""scriptRoots"": [""" + Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) + @"""],
                    ""deniedCommandTypes"": [ ""StartProcessCommand"", ""RunScriptCommand"" ],
                    ""runTimeoutSeconds"": 42,
                }");

                var policy = ScriptPolicy.Load(workspaceDirectory: workspace);

                Assert.Single(policy.ScriptRoots);
                Assert.Equal(42, policy.RunTimeoutSeconds);
                Assert.Contains("StartProcessCommand", policy.DeniedCommandTypes);
                Assert.False(policy.IsCommandAllowed("runscriptcommand"));
                Assert.True(policy.IsCommandAllowed("VariableCommand"));
            }
            finally
            {
                Directory.Delete(workspace, true);
            }
        }

        [Fact]
        public void Load_WithoutAFileReturnsDefaults()
        {
            var missing = Path.Combine(Path.GetTempPath(), "no-policy-" + Guid.NewGuid().ToString("N") + ".json");
            var policy = ScriptPolicy.Load(missing);

            Assert.Empty(policy.ScriptRoots);
            Assert.Equal(300, policy.RunTimeoutSeconds);
        }

        [Fact]
        public void Load_InvalidJsonThrowsActionableError()
        {
            var path = Path.Combine(Path.GetTempPath(), "bad-policy-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, "{ not json");
            try
            {
                var ex = Assert.Throws<InvalidDataException>(() => ScriptPolicy.Load(path));
                Assert.Contains(path, ex.Message);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void IsCommandAllowed_HandlesBlankEntries()
        {
            var policy = new ScriptPolicy { DeniedCommandTypes = new System.Collections.Generic.List<string> { " ", null } };

            Assert.True(policy.IsCommandAllowed("AnythingCommand"));
            Assert.True(policy.IsCommandAllowed(null));
        }
    }
}
