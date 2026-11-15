using sharpRPA.Core.Execution;

namespace sharpRPA.Core.AutomationCommands
{
    /// <summary>
    /// Simulates a command compiled into another assembly (e.g. the Windows-only command layer).
    /// Lives in the command namespace on purpose so discovery behaves exactly like the real
    /// platform command assembly.
    /// </summary>
    [Attributes.ClassAttributes.Group("Test Commands")]
    [Attributes.ClassAttributes.Description("Test-only command used to verify cross-assembly registration.")]
    public sealed class FakePlatformCommand : ScriptCommand
    {
        public string v_Value { get; set; }

        public FakePlatformCommand()
        {
            this.CommandName = "FakePlatformCommand";
            this.SelectionName = "Fake Platform Command";
            this.CommandEnabled = true;
            this.DefaultPause = 0;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
        }
    }
}
