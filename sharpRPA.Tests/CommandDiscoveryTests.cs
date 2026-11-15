using System.Linq;
using sharpRPA.Core;
using sharpRPA.Core.AutomationCommands;
using Xunit;

namespace sharpRPA.Tests
{
    public class CommandDiscoveryTests
    {
        public CommandDiscoveryTests()
        {
            // mirrors Program.Main registering the Windows command assembly at startup
            Common.RegisterCommandAssembly(typeof(FakePlatformCommand).Assembly);
        }

        [Fact]
        public void GetCommandTypes_IncludesCoreAndRegisteredAssemblies()
        {
            var types = Common.GetCommandTypes();

            Assert.Contains(typeof(VariableCommand), types);
            Assert.Contains(typeof(BeginIfCommand), types);
            Assert.Contains(typeof(FakePlatformCommand), types);
            Assert.DoesNotContain(typeof(ScriptCommand), types);
        }

        [Fact]
        public void GetCommandTypes_DoesNotDuplicateRegistrations()
        {
            var before = Common.GetCommandTypes().Count;

            Common.RegisterCommandAssembly(typeof(FakePlatformCommand).Assembly);
            Common.RegisterCommandAssembly(typeof(FakePlatformCommand).Assembly);

            Assert.Equal(before, Common.GetCommandTypes().Count);
        }

        [Fact]
        public void GetGroupedCommands_GroupsAcrossRegisteredAssemblies()
        {
            var groups = Common.GetGroupedCommands();

            Assert.Contains(groups, group => group.Any(type => type == typeof(VariableCommand)));
            Assert.Contains(groups, group => group.Any(type => type == typeof(FakePlatformCommand)));
        }
    }
}
