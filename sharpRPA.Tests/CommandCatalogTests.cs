using System.Linq;
using sharpRPA.Core.Services;
using Xunit;

namespace sharpRPA.Tests
{
    public class CommandCatalogTests
    {
        private readonly CommandCatalog catalog = new CommandCatalog();

        [Fact]
        public void ListSummaries_ReturnsEveryEnabledCommandGrouped()
        {
            var summaries = catalog.ListSummaries();

            Assert.NotEmpty(summaries);
            Assert.Contains(summaries, summary => summary.TypeName == "VariableCommand");
            Assert.Contains(summaries, summary => summary.TypeName == "BeginIfCommand");
            Assert.Contains(summaries, summary => summary.TypeName == "SeleniumBrowserCreateCommand");
            Assert.All(summaries, summary =>
            {
                Assert.False(string.IsNullOrWhiteSpace(summary.Group));
                Assert.False(string.IsNullOrWhiteSpace(summary.SelectionName));
            });
        }

        [Fact]
        public void Describe_ByTypeName_ReturnsParameterSchema()
        {
            var info = catalog.Describe("VariableCommand");

            Assert.NotNull(info);
            Assert.Equal("Variable Commands", info.Group);
            Assert.Equal("Set Variable", info.SelectionName);

            var nameProperty = info.Properties.Single(property => property.Name == "v_userVariableName");
            Assert.Equal("string", nameProperty.Type);
            Assert.False(string.IsNullOrWhiteSpace(nameProperty.Description));
        }

        [Fact]
        public void Describe_BySelectionName_IsCaseInsensitive()
        {
            var info = catalog.Describe("set variable");

            Assert.NotNull(info);
            Assert.Equal("VariableCommand", info.TypeName);
        }

        [Fact]
        public void Describe_ReportsSelectionOptions()
        {
            var info = catalog.Describe("BeginLoopCommand");
            var loopType = info.Properties.Single(property => property.Name == "v_LoopType");

            Assert.Contains("Loop Number Of Times", loopType.Options);
            Assert.Contains("Loop Through List", loopType.Options);
        }

        [Fact]
        public void Describe_ReportsUiHelpers()
        {
            var messageBox = catalog.Describe("MessageBoxCommand");
            var message = messageBox.Properties.Single(property => property.Name == "v_Message");
            Assert.True(message.SupportsVariables);

            var variable = catalog.Describe("VariableCommand");
            var input = variable.Properties.Single(property => property.Name == "v_Input");
            Assert.True(input.SupportsVariables);
            Assert.False(input.SupportsFilePicker);
        }

        [Fact]
        public void Describe_UnknownCommand_ReturnsNull()
        {
            Assert.Null(catalog.Describe("DoesNotExistCommand"));
            Assert.Throws<System.ArgumentException>(() => catalog.Describe(" "));
        }
    }
}
