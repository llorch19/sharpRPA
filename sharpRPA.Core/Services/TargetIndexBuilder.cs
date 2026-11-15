using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;
using ScriptModel = sharpRPA.Core.Script.Script;

namespace sharpRPA.Core.Services
{
    /// <summary>
    /// Derives the target index from scripts and recordings. The index is read-only
    /// for consumers and can always be rebuilt from those two sources.
    /// </summary>
    public sealed class TargetIndexBuilder
    {
        public const string SourceScript = "script";
        public const string SourceRecording = "recording";

        public const string KindFile = "file";
        public const string KindUrl = "url";
        public const string KindWindow = "window";
        public const string KindUiElement = "ui_element";

        private static readonly Regex DrivePath = new Regex(@"^[A-Za-z]:[\\/]", RegexOptions.Compiled);

        private readonly TargetIndexStore store;

        public TargetIndexBuilder(TargetIndexStore store)
        {
            this.store = store;
        }

        /// <summary>
        /// Rebuilds every script-derived row. Returns the number of targets indexed.
        /// </summary>
        public int RebuildScripts(ScriptCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            store.Clear(SourceScript);
            var indexed = 0;

            foreach (var summary in catalog.ListScripts())
            {
                ScriptModel script;
                try
                {
                    script = catalog.LoadScript(summary.Path);
                }
                catch (Exception)
                {
                    // an unreadable script must not abort the whole rebuild
                    continue;
                }

                foreach (var action in ScriptCatalog.Walk(script.Commands))
                {
                    var command = action.ScriptCommand;
                    if (command == null)
                    {
                        continue;
                    }

                    var seq = command.LineNumber > 0 ? (long?)command.LineNumber : null;
                    foreach (var record in ExtractFromCommand(command, summary.Path, seq))
                    {
                        store.Upsert(record);
                        indexed++;
                    }
                }
            }

            return indexed;
        }

        /// <summary>
        /// Replaces the index rows belonging to a single recording.
        /// </summary>
        public int RebuildRecording(string recordingId, IEnumerable<RecordedAction> events)
        {
            if (string.IsNullOrWhiteSpace(recordingId))
            {
                throw new ArgumentException("A recording id is required.", nameof(recordingId));
            }

            store.Clear(SourceRecording, recordingId);

            var indexed = 0;
            long seq = 0;
            foreach (var action in events ?? Enumerable.Empty<RecordedAction>())
            {
                foreach (var record in ExtractFromEvent(action, recordingId, seq))
                {
                    store.Upsert(record);
                    indexed++;
                }

                seq++;
            }

            return indexed;
        }

        public static IEnumerable<TargetRecord> ExtractFromCommand(ScriptCommand command, string sourceId, long? seq)
        {
            if (command == null)
            {
                yield break;
            }

            var appHint = ExtractAppHint(command);

            foreach (var property in command.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!property.CanRead || !property.Name.StartsWith("v_"))
                {
                    continue;
                }

                var locatorKind = string.Empty;
                string kind;

                if (property.PropertyType == typeof(string))
                {
                    kind = Classify(property.Name, property.GetValue(command) as string, out locatorKind);
                    if (kind != null)
                    {
                        yield return Create(SourceScript, kind, (string)property.GetValue(command), locatorKind, appHint, sourceId, seq);
                    }
                }
                else if (property.PropertyType == typeof(DataTable))
                {
                    foreach (var record in ExtractFromTable(property.GetValue(command) as DataTable, appHint, sourceId, seq))
                    {
                        yield return record;
                    }
                }
            }
        }

        public static IEnumerable<TargetRecord> ExtractFromEvent(RecordedAction action, string sourceId, long seq)
        {
            if (action == null)
            {
                yield break;
            }

            var metadata = action.Metadata ?? new Dictionary<string, string>();

            string locator = null;
            string kind = null;
            string locatorKind = null;
            string appHint = null;

            metadata.TryGetValue("selector", out locator);
            if (string.IsNullOrWhiteSpace(locator))
            {
                locator = action.Target;
            }

            if (string.IsNullOrWhiteSpace(locator))
            {
                yield break;
            }

            if (!metadata.TryGetValue("kind", out kind) || string.IsNullOrWhiteSpace(kind))
            {
                kind = LooksLikeUrl(locator) ? KindUrl : LooksLikePath(locator) ? KindFile : KindUiElement;
            }

            if (!metadata.TryGetValue("selectorKind", out locatorKind) || string.IsNullOrWhiteSpace(locatorKind))
            {
                locatorKind = kind == KindUrl ? "url" : kind == KindFile ? "path" : "recorded";
            }

            if (!metadata.TryGetValue("window", out appHint) && !metadata.TryGetValue("process", out appHint))
            {
                metadata.TryGetValue("pageUrl", out appHint);
            }

            yield return Create(SourceRecording, kind, locator, locatorKind, appHint, sourceId, seq);
        }

        private static IEnumerable<TargetRecord> ExtractFromTable(DataTable table, string appHint, string sourceId, long? seq)
        {
            if ((table == null) || (table.Columns.Count == 0))
            {
                yield break;
            }

            var hasNameColumn = table.Columns.Contains("Parameter Name");
            var hasValueColumn = table.Columns.Contains("Parameter Value");

            foreach (DataRow row in table.Rows)
            {
                if (!hasValueColumn)
                {
                    continue;
                }

                var value = row["Parameter Value"] as string;
                var name = hasNameColumn ? row["Parameter Name"] as string : "Parameter Value";

                var locatorKind = string.Empty;
                var kind = Classify(name, value, out locatorKind);
                if (kind != null)
                {
                    yield return Create(SourceScript, kind, value, locatorKind, appHint, sourceId, seq);
                }
            }
        }

        private static TargetRecord Create(string sourceKind, string kind, string locator, string locatorKind, string appHint, string sourceId, long? seq)
        {
            return new TargetRecord
            {
                Kind = kind,
                Locator = locator.Trim(),
                LocatorKind = locatorKind,
                AppHint = string.IsNullOrWhiteSpace(appHint) ? null : appHint.Trim(),
                SourceKind = sourceKind,
                SourceId = sourceId,
                Seq = seq,
                LastSeenAt = DateTimeOffset.Now
            };
        }

        private static string ExtractAppHint(ScriptCommand command)
        {
            foreach (var property in command.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (property.CanRead && (property.PropertyType == typeof(string)) && property.Name.Contains("Window"))
                {
                    var value = property.GetValue(command) as string;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static string Classify(string propertyName, string value, out string locatorKind)
        {
            locatorKind = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim();
            var name = propertyName ?? string.Empty;

            // "Search Type"/"Element Action" describe the command, they are not locators
            if (name.EndsWith("Type") || name.EndsWith("Action") || name.EndsWith("Mode"))
            {
                return null;
            }

            if (LooksLikeUrl(value))
            {
                locatorKind = "url";
                return KindUrl;
            }

            if (name.Contains("Path") || LooksLikePath(value))
            {
                locatorKind = "path";
                return KindFile;
            }

            if (name.Contains("Window"))
            {
                locatorKind = "window_title";
                return KindWindow;
            }

            if (name.Contains("XPath"))
            {
                locatorKind = "xpath";
                return KindUiElement;
            }

            if (name.Contains("AutomationID"))
            {
                locatorKind = "automation_id";
                return KindUiElement;
            }

            if (name.Contains("Search") || name.Contains("Selector"))
            {
                locatorKind = value.StartsWith("//") || value.StartsWith("/") ? "xpath" : "selector";
                return KindUiElement;
            }

            if (name.Contains("Handle"))
            {
                locatorKind = "name";
                return KindUiElement;
            }

            return null;
        }

        private static bool LooksLikeUrl(string value)
        {
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool LooksLikePath(string value)
        {
            if (DrivePath.IsMatch(value))
            {
                return true;
            }

            if (value.StartsWith("\\\\"))
            {
                return true;
            }

            // unix absolute path, but not an xpath (which also starts with '/')
            return value.StartsWith("/") && !value.StartsWith("//");
        }
    }
}
