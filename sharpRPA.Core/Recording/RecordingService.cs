using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;

namespace sharpRPA.Core.Recording
{
    /// <summary>
    /// Manages the recording lifecycle: events are staged to disk while a capture runs,
    /// and only a finished recording is committed to SQLite together with its target index rows.
    /// </summary>
    public sealed class RecordingService
    {
        public const string StatusCompleted = "completed";
        public const string StatusIncomplete = "incomplete";

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly object sync = new object();
        private readonly Dictionary<string, RecordingSession> active = new Dictionary<string, RecordingSession>();
        private readonly SqliteDatabase database;
        private readonly RecordingStore recordings;
        private readonly TargetIndexStore targets;
        private readonly TargetIndexBuilder indexBuilder;
        private readonly string stagingDirectory;

        public RecordingService(string databasePath = null, string stagingDirectory = null)
        {
            var workspace = Common.GetWorkspaceFolderPath();

            if (string.IsNullOrWhiteSpace(databasePath))
            {
                databasePath = Environment.GetEnvironmentVariable("SHARPRPA_DB");
            }

            if (string.IsNullOrWhiteSpace(databasePath))
            {
                databasePath = Path.Combine(workspace, "sharprpa.db");
            }

            databasePath = Path.GetFullPath(databasePath);

            if (string.IsNullOrWhiteSpace(stagingDirectory))
            {
                // keep staging next to the database so a custom DB location stays self-contained
                stagingDirectory = Path.Combine(Path.GetDirectoryName(databasePath), "recording-staging");
            }

            this.stagingDirectory = stagingDirectory;

            database = new SqliteDatabase(databasePath);
            database.Initialize();

            recordings = new RecordingStore(database);
            targets = new TargetIndexStore(database);
            indexBuilder = new TargetIndexBuilder(targets);
        }

        public string DatabasePath
        {
            get { return database.DatabasePath; }
        }

        public string StagingDirectory
        {
            get { return stagingDirectory; }
        }

        /// <summary>Shared database so run artifacts live in the same workspace file.</summary>
        public SqliteDatabase Database
        {
            get { return database; }
        }

        public string RunsDirectory
        {
            get { return Path.Combine(Path.GetDirectoryName(DatabasePath), "runs"); }
        }

        public RecordingSession Start(string source, string title = null)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException("A recording source is required (for example 'browser' or 'windows-desktop').", nameof(source));
            }

            var id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stagingDirectory);

            var session = new RecordingSession(this, id, source, title, Path.Combine(stagingDirectory, id + ".jsonl"), DateTimeOffset.Now);

            lock (sync)
            {
                active[id] = session;
            }

            return session;
        }

        public List<RecordingInfo> List(int limit = 200, int offset = 0)
        {
            return recordings.List(limit, offset);
        }

        public int CountRecordings()
        {
            return recordings.Count();
        }

        public IReadOnlyList<string> ActiveIds
        {
            get
            {
                lock (sync)
                {
                    return new List<string>(active.Keys);
                }
            }
        }

        /// <summary>
        /// Appends one event to an in-flight recording.
        /// </summary>
        public long Record(string recordingId, RecordedAction action)
        {
            var session = RequireActive(recordingId);
            session.Sink.Record(action);
            return session.ActionCount;
        }

        /// <summary>
        /// Commits an in-flight recording to SQLite.
        /// </summary>
        public RecordingInfo Stop(string recordingId, string title = null, string scriptPath = null)
        {
            var session = RequireActive(recordingId);
            return session.Stop(title, scriptPath);
        }

        /// <summary>
        /// Drops an in-flight recording without touching the database.
        /// </summary>
        public bool Abort(string recordingId)
        {
            RecordingSession session;
            lock (sync)
            {
                if (!active.TryGetValue(recordingId ?? string.Empty, out session))
                {
                    return false;
                }
            }

            session.Abort();
            return true;
        }

        private RecordingSession RequireActive(string recordingId)
        {
            RecordingSession session;
            lock (sync)
            {
                if (!active.TryGetValue(recordingId ?? string.Empty, out session))
                {
                    throw new ArgumentException("No recording is in progress with id '" + recordingId + "'.", nameof(recordingId));
                }
            }

            return session;
        }

        public RecordingInfo GetInfo(string id)
        {
            return recordings.Get(id);
        }

        public RecordingDetail Get(string id, bool includeEvents = true)
        {
            var info = recordings.Get(id);
            if (info == null)
            {
                return null;
            }

            var detail = new RecordingDetail(info);
            if (includeEvents)
            {
                detail.Events = ParseEvents(recordings.GetEvents(id));
            }

            return detail;
        }

        public List<TargetHit> FindTargets(string query, string kind = null, int limit = 100, int offset = 0)
        {
            return targets.Find(query, kind, limit, offset);
        }

        public int CountTargets(string query = null, string kind = null)
        {
            return targets.Count(query, kind);
        }

        /// <summary>
        /// Rebuilds script and recording derived rows from scratch.
        /// </summary>
        public int RebuildTargetIndex(ScriptCatalog catalog)
        {
            var indexed = catalog == null ? 0 : indexBuilder.RebuildScripts(catalog);

            foreach (var info in recordings.List(1000))
            {
                indexed += indexBuilder.RebuildRecording(info.Id, ParseEvents(recordings.GetEvents(info.Id)));
            }

            return indexed;
        }

        /// <summary>
        /// Reconciles staging files that have no committed row (crash before COMMIT) and
        /// removes staging files left behind by a crash right after COMMIT.
        /// </summary>
        public int CollectStaging(TimeSpan? olderThan = null)
        {
            var threshold = DateTimeOffset.Now - (olderThan ?? TimeSpan.FromHours(1));

            if (!Directory.Exists(stagingDirectory))
            {
                return 0;
            }

            var handled = 0;

            foreach (var file in Directory.GetFiles(stagingDirectory, "*.jsonl"))
            {
                var id = Path.GetFileNameWithoutExtension(file);

                lock (sync)
                {
                    if (active.ContainsKey(id))
                    {
                        continue;
                    }
                }

                if (File.GetLastWriteTimeUtc(file) > threshold.UtcDateTime)
                {
                    continue;
                }

                if (recordings.Exists(id))
                {
                    // crash happened between COMMIT and the staging cleanup
                    TryDelete(file);
                    handled++;
                    continue;
                }

                try
                {
                    var content = ParseStaging(File.ReadAllBytes(file));
                    var now = DateTimeOffset.Now;
                    var duration = DurationOf(content.Events);
                    var startedAt = content.StartedAt ?? File.GetLastWriteTimeUtc(file);

                    var info = new RecordingInfo
                    {
                        Id = id,
                        Source = content.Source ?? "unknown",
                        Status = StatusIncomplete,
                        Title = content.Title,
                        ActionCount = content.Events.Count,
                        DurationMs = duration,
                        StartedAt = startedAt,
                        EndedAt = startedAt + TimeSpan.FromMilliseconds(duration ?? 0),
                        CreatedAt = now
                    };

                    recordings.Insert(info, Gzip(File.ReadAllBytes(file)));
                    indexBuilder.RebuildRecording(id, content.Events);
                    TryDelete(file);
                    handled++;
                }
                catch (Exception)
                {
                    // a corrupt staging file must not block startup forever; leave it for the next pass
                }
            }

            return handled;
        }

        internal RecordingInfo Commit(RecordingSession session, string title, string scriptPath)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var stagingFile = session.StagingPath;
            var sink = (StagingRecordingSink)session.Sink;

            // 1. force the staging file to disk and release the handle so it can be
            //    read and deleted on Windows
            sink.FlushToDisk();
            sink.Dispose();

            var raw = File.ReadAllBytes(stagingFile);
            var content = ParseStaging(raw);
            var now = DateTimeOffset.Now;

            var info = new RecordingInfo
            {
                Id = session.Id,
                Source = session.Source,
                Status = StatusCompleted,
                Title = title ?? session.Title,
                ScriptPath = scriptPath,
                ActionCount = content.Events.Count,
                DurationMs = DurationOf(content.Events),
                StartedAt = session.StartedAt,
                EndedAt = now,
                CreatedAt = now
            };

            // 2. one transaction worth of work: metadata + gzipped event blob
            recordings.Insert(info, Gzip(raw));

            // 3. refresh the derived target index for this recording
            indexBuilder.RebuildRecording(session.Id, content.Events);

            // 4. only now is the staging file safe to remove
            TryDelete(stagingFile);

            lock (sync)
            {
                active.Remove(session.Id);
            }

            return info;
        }

        internal void Discard(RecordingSession session)
        {
            if (session == null)
            {
                return;
            }

            lock (sync)
            {
                active.Remove(session.Id);
            }

            (session.Sink as IDisposable)?.Dispose();
            TryDelete(session.StagingPath);
        }

        private static long? DurationOf(List<RecordedAction> events)
        {
            if ((events == null) || (events.Count < 2))
            {
                return 0;
            }

            return (long)(events[events.Count - 1].Timestamp - events[0].Timestamp).TotalMilliseconds;
        }

        private static StagingContent ParseStaging(byte[] raw)
        {
            var content = new StagingContent();

            if ((raw == null) || (raw.Length == 0))
            {
                return content;
            }

            // staging files are plain JSONL; only the committed blob is gzipped
            using (var reader = new StreamReader(new MemoryStream(raw), Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        using (var document = JsonDocument.Parse(line))
                        {
                            var root = document.RootElement;
                            if ((root.ValueKind == JsonValueKind.Object) && root.TryGetProperty("schema", out _))
                            {
                                content.Source = ReadString(root, "source");
                                content.Title = ReadString(root, "title");

                                var startedAt = ReadString(root, "startedAt");
                                DateTimeOffset parsed;
                                if (!string.IsNullOrEmpty(startedAt) && DateTimeOffset.TryParse(startedAt, out parsed))
                                {
                                    content.StartedAt = parsed;
                                }

                                continue;
                            }
                        }

                        var action = JsonSerializer.Deserialize<RecordedAction>(line, SerializerOptions);
                        if (action != null)
                        {
                            content.Events.Add(action);
                        }
                    }
                    catch (JsonException)
                    {
                        // a torn last line after a crash is expected; skip it
                    }
                }
            }

            return content;
        }

        private static List<RecordedAction> ParseEvents(byte[] gzipBlob)
        {
            if ((gzipBlob == null) || (gzipBlob.Length == 0))
            {
                return new List<RecordedAction>();
            }

            using (var input = new MemoryStream(gzipBlob))
            using (var gzip = new GZipStream(input, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip, Encoding.UTF8))
            {
                var events = new List<RecordedAction>();
                string line;

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.IndexOf("\"schema\"", StringComparison.Ordinal) >= 0)
                    {
                        continue;
                    }

                    try
                    {
                        var action = JsonSerializer.Deserialize<RecordedAction>(line, SerializerOptions);
                        if (action != null)
                        {
                            events.Add(action);
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }

                return events;
            }
        }

        private static string ReadString(JsonElement root, string property)
        {
            return root.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.String)
                ? value.GetString()
                : null;
        }

        private static byte[] Gzip(byte[] raw)
        {
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                return output.ToArray();
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private sealed class StagingContent
        {
            public string Source { get; set; }
            public string Title { get; set; }
            public DateTimeOffset? StartedAt { get; set; }
            public List<RecordedAction> Events { get; } = new List<RecordedAction>();
        }
    }
}
