using System;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using sharpRPA.Mcp;
using Xunit;

namespace sharpRPA.Tests
{
    /// <summary>
    /// Unit tests for the MCP contract helpers. Everything here is exercised through
    /// the real server as well; these tests pin the shapes without spawning a process.
    /// </summary>
    public class McpContractTests
    {
        private static readonly JsonSerializerOptions CamelCase = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static JsonElement ToJson(object value)
        {
            using (var document = JsonDocument.Parse(JsonSerializer.Serialize(value, CamelCase)))
            {
                return document.RootElement.Clone();
            }
        }

        private static JsonElement ErrorJson(object failure)
        {
            var call = Assert.IsType<CallToolResult>(failure);
            Assert.True(call.IsError);

            var block = Assert.IsType<TextContentBlock>(Assert.Single(call.Content));
            using (var document = JsonDocument.Parse(block.Text))
            {
                return document.RootElement.Clone();
            }
        }

        [Fact]
        public void Ok_EnvelopeCarriesOkDataAndPage()
        {
            var envelope = ToJson(ToolResult.Ok(new { value = 1 }, Cursor.Page(10, 0, 5, 5)));

            Assert.True(envelope.GetProperty("ok").GetBoolean());
            Assert.Equal(1, envelope.GetProperty("data").GetProperty("value").GetInt32());

            var page = envelope.GetProperty("page");
            Assert.Equal(10, page.GetProperty("total").GetInt32());
            Assert.Equal(Cursor.Encode(5), page.GetProperty("nextCursor").GetString());
        }

        [Fact]
        public void Ok_DefaultsToANullPage()
        {
            var envelope = ToJson(ToolResult.Ok("payload"));

            Assert.True(envelope.GetProperty("ok").GetBoolean());
            Assert.Equal("payload", envelope.GetProperty("data").GetString());
            Assert.True(envelope.GetProperty("page").ValueKind == JsonValueKind.Null);
        }

        [Fact]
        public void Fail_IsAnErrorResultWithTheStructuredEnvelope()
        {
            var envelope = ErrorJson(ToolResult.Fail(ErrorCodes.NotFound, "missing", "use list"));

            Assert.False(envelope.GetProperty("ok").GetBoolean());
            Assert.Equal("NOT_FOUND", envelope.GetProperty("error").GetProperty("code").GetString());
            Assert.Equal("missing", envelope.GetProperty("error").GetProperty("message").GetString());
            Assert.Equal("use list", envelope.GetProperty("error").GetProperty("hint").GetString());
        }

        [Fact]
        public void Fault_MapsUnexpectedExceptionsToInternal()
        {
            var envelope = ErrorJson(GuardedMcpServerTool.Fault(new InvalidOperationException("boom")));

            Assert.False(envelope.GetProperty("ok").GetBoolean());
            Assert.Equal("INTERNAL", envelope.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("InvalidOperationException", envelope.GetProperty("error").GetProperty("message").GetString());
            Assert.Contains("stderr", envelope.GetProperty("error").GetProperty("hint").GetString());
        }

        [Fact]
        public void Cursor_RoundTripsOffsets()
        {
            Assert.Equal(0, Cursor.Decode(null));
            Assert.Equal(0, Cursor.Decode("  "));
            Assert.Equal(42, Cursor.Decode(Cursor.Encode(42)));
            Assert.Equal(0, Cursor.Decode(Cursor.Encode(-5)));
        }

        [Fact]
        public void Cursor_InvalidCursorProducesAnInvalidArgumentFailure()
        {
            Assert.Null(Cursor.Decode("not-a-cursor"));

            object failure;
            int offset;
            int size;
            Assert.False(Cursor.TryStartPage("not-a-cursor", 10, out offset, out size, out failure));

            var envelope = ErrorJson(failure);
            Assert.Equal("INVALID_ARGUMENT", envelope.GetProperty("error").GetProperty("code").GetString());
        }

        [Fact]
        public void Cursor_TryStartPageNormalisesTheLimit()
        {
            object failure;
            int offset;
            int size;

            Assert.True(Cursor.TryStartPage(null, 0, out offset, out size, out failure));
            Assert.Equal(0, offset);
            Assert.Equal(Cursor.DefaultLimit, size);
            Assert.Null(failure);

            Assert.True(Cursor.TryStartPage(null, 100000, out offset, out size, out failure));
            Assert.Equal(Cursor.MaxLimit, size);
        }

        [Fact]
        public void Paginate_SlicesAndSignalsTheLastPage()
        {
            var all = new[] { "a", "b", "c" };

            var first = Cursor.Paginate(all, 2, 0);
            Assert.Equal(new[] { "a", "b" }, first.Item1);
            var firstPage = ToJson(first.Item2);
            Assert.Equal(3, firstPage.GetProperty("total").GetInt32());
            Assert.Equal(Cursor.Encode(2), firstPage.GetProperty("nextCursor").GetString());

            var second = Cursor.Paginate(all, 2, 2);
            Assert.Equal(new[] { "c" }, second.Item1);
            var secondPage = ToJson(second.Item2);
            // the SDK omits nulls so the field disappears; a plain serializer writes null.
            // Either way the value must be null, which is what means "last page".
            if (secondPage.TryGetProperty("nextCursor", out var lastCursor))
            {
                Assert.Equal(JsonValueKind.Null, lastCursor.ValueKind);
            }
        }
    }
}
