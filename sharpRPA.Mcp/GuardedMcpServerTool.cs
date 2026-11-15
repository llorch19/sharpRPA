using System;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace sharpRPA.Mcp
{
    /// <summary>
    /// Converts unexpected tool faults into the structured INTERNAL envelope instead of
    /// letting the SDK replace them with a generic "An error occurred invoking ..." message,
    /// and echoes the real exception to stderr so the operator can diagnose it.
    /// Expected failures never reach this class - tools return them directly.
    /// </summary>
    internal sealed class GuardedMcpServerTool : DelegatingMcpServerTool
    {
        public GuardedMcpServerTool(McpServerTool innerTool)
            : base(innerTool)
        {
        }

        public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
        {
            try
            {
                return await base.InvokeAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Fault(ex);
            }
        }

        /// <summary>Maps an unexpected exception to the INTERNAL envelope. Kept separate so it is unit testable.</summary>
        internal static CallToolResult Fault(Exception ex)
        {
            Console.Error.WriteLine("sharpRPA MCP tool fault: " + ex);

            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                ok = false,
                error = new
                {
                    code = ErrorCodes.Internal,
                    message = ex.GetType().Name + ": " + ex.Message,
                    hint = "Unexpected server fault. The full exception was written to the MCP server log (stderr)."
                }
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            return new CallToolResult
            {
                IsError = true,
                Content = new System.Collections.Generic.List<ContentBlock> { new TextContentBlock { Text = payload } }
            };
        }
    }
}
