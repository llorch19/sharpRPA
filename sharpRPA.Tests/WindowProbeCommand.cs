using sharpRPA.Core.Execution;

namespace sharpRPA.Core.AutomationCommands
{
    /// <summary>
    /// Test-only command that carries a window target, so script side window
    /// extraction can be verified without the Windows command assembly.
    /// </summary>
    [Attributes.ClassAttributes.Group("Test Commands")]
    [Attributes.ClassAttributes.Description("Test-only command carrying a window target.")]
    public sealed class WindowProbeCommand : ScriptCommand
    {
        public string v_WindowName { get; set; }

        public WindowProbeCommand()
        {
            this.CommandName = "WindowProbeCommand";
            this.SelectionName = "Window Probe";
            this.CommandEnabled = true;
            this.DefaultPause = 0;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
        }
    }
}
