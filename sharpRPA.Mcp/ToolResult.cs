using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace sharpRPA.Mcp
{
    /// <summary>
    /// Closed set of machine readable error codes. Adding a code is compatible,
    /// changing the meaning of an existing one is not.
    /// </summary>
    internal static class ErrorCodes
    {
        public const string InvalidArgument = "INVALID_ARGUMENT";
        public const string NotFound = "NOT_FOUND";
        public const string AlreadyExists = "ALREADY_EXISTS";
        public const string AlreadyFinished = "ALREADY_FINISHED";
        public const string NotInProgress = "NOT_IN_PROGRESS";
        public const string ParseFailed = "PARSE_FAILED";
        public const string UnsupportedCommand = "UNSUPPORTED_COMMAND";
        public const string PermissionDenied = "PERMISSION_DENIED";
        public const string Timeout = "TIMEOUT";
        public const string Internal = "INTERNAL";
    }

    /// <summary>
    /// Response envelope shared by every tool: { ok, data, page } and the
    /// structured failure { ok:false, error:{ code, message, hint } }.
    ///
    /// Failures are returned, never thrown: the MCP SDK replaces exception text with
    /// a generic "An error occurred invoking ..." message, which would lose the code.
    /// </summary>
    internal static class ToolResult
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static object Ok(object data, object page = null)
        {
            return new { ok = true, data, page };
        }

        public static object Fail(string code, string message, string hint = null)
        {
            var payload = JsonSerializer.Serialize(new
            {
                ok = false,
                error = new { code, message, hint }
            }, SerializerOptions);

            return new CallToolResult
            {
                IsError = true,
                Content = new List<ContentBlock> { new TextContentBlock { Text = payload } }
            };
        }
    }

    internal static class Cursor
    {
        public const int DefaultLimit = 50;
        public const int MaxLimit = 200;

        public static int NormalizeLimit(int limit)
        {
            if (limit <= 0)
            {
                return DefaultLimit;
            }

            return Math.Min(limit, MaxLimit);
        }

        /// <summary>
        /// Decodes a cursor, returning null when it is not a valid cursor so the
        /// caller can report INVALID_ARGUMENT without throwing.
        /// </summary>
        public static int? Decode(string cursor)
        {
            if (string.IsNullOrWhiteSpace(cursor))
            {
                return 0;
            }

            try
            {
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var offset = int.Parse(text);
                return offset < 0 ? 0 : offset;
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentException)
            {
                return null;
            }
        }

        public static string Encode(int offset)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString()));
        }

        public static object Page(int total, int offset, int size, int fetched)
        {
            var nextCursor = ((offset + fetched < total) && (fetched > 0)) ? Encode(offset + size) : null;
            return new { nextCursor, total };
        }

        public static Tuple<List<T>, object> Paginate<T>(IReadOnlyList<T> all, int limit, int offset)
        {
            var size = NormalizeLimit(limit);
            var items = all.Skip(offset).Take(size).ToList();
            return Tuple.Create(items, Page(all.Count, offset, size, items.Count));
        }

        /// <summary>
        /// Common prelude for paginated tools: validates the cursor and normalises the limit.
        /// Returns an error object when the cursor is unusable.
        /// </summary>
        public static bool TryStartPage(string cursor, int limit, out int offset, out int size, out object failure)
        {
            var decoded = Decode(cursor);
            if (!decoded.HasValue)
            {
                offset = 0;
                size = 0;
                failure = ToolResult.Fail(ErrorCodes.InvalidArgument, "cursor is not a valid pagination cursor.", "Drop the cursor parameter to start from the beginning.");
                return false;
            }

            offset = decoded.Value;
            size = NormalizeLimit(limit);
            failure = null;
            return true;
        }
    }
}
