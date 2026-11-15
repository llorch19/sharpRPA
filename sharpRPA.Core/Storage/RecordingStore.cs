using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using sharpRPA.Core.Recording;

namespace sharpRPA.Core.Storage
{
    public class RecordingInfo
    {
        public string Id { get; set; }
        public string Source { get; set; }
        public string Status { get; set; }
        public string Title { get; set; }
        public string ScriptPath { get; set; }
        public int ActionCount { get; set; }
        public long? DurationMs { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    public sealed class RecordingDetail : RecordingInfo
    {
        public RecordingDetail()
        {
        }

        public RecordingDetail(RecordingInfo info)
        {
            if (info == null)
            {
                return;
            }

            Id = info.Id;
            Source = info.Source;
            Status = info.Status;
            Title = info.Title;
            ScriptPath = info.ScriptPath;
            ActionCount = info.ActionCount;
            DurationMs = info.DurationMs;
            StartedAt = info.StartedAt;
            EndedAt = info.EndedAt;
            CreatedAt = info.CreatedAt;
        }

        public List<RecordedAction> Events { get; set; } = new List<RecordedAction>();
    }

    /// <summary>
    /// Persists finished recordings. A recording only reaches this store after the
    /// staging file has been flushed, so the database never holds a half-written capture.
    /// </summary>
    public sealed class RecordingStore
    {
        private readonly SqliteDatabase database;

        public RecordingStore(SqliteDatabase database)
        {
            this.database = database;
        }

        public void Insert(RecordingInfo info, byte[] events)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO recording (id, source, status, title, script_path, action_count, duration_ms, started_at, ended_at, created_at, events)
VALUES ($id, $source, $status, $title, $scriptPath, $actionCount, $durationMs, $startedAt, $endedAt, $createdAt, $events);";
                command.Parameters.AddWithValue("$id", info.Id);
                command.Parameters.AddWithValue("$source", (object)info.Source ?? DBNull.Value);
                command.Parameters.AddWithValue("$status", info.Status);
                command.Parameters.AddWithValue("$title", (object)info.Title ?? DBNull.Value);
                command.Parameters.AddWithValue("$scriptPath", (object)info.ScriptPath ?? DBNull.Value);
                command.Parameters.AddWithValue("$actionCount", info.ActionCount);
                command.Parameters.AddWithValue("$durationMs", info.DurationMs.HasValue ? (object)info.DurationMs.Value : DBNull.Value);
                command.Parameters.AddWithValue("$startedAt", info.StartedAt.ToString("O"));
                command.Parameters.AddWithValue("$endedAt", info.EndedAt.HasValue ? (object)info.EndedAt.Value.ToString("O") : DBNull.Value);
                command.Parameters.AddWithValue("$createdAt", info.CreatedAt.ToString("O"));
                command.Parameters.AddWithValue("$events", (object)events ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        public bool Exists(string id)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM recording WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id ?? string.Empty);
                return Convert.ToInt64(command.ExecuteScalar()) > 0;
            }
        }

        public RecordingInfo Get(string id)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, source, status, title, script_path, action_count, duration_ms, started_at, ended_at, created_at FROM recording WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id ?? string.Empty);

                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? ReadInfo(reader) : null;
                }
            }
        }

        public List<RecordingInfo> List(int limit = 200)
        {
            var results = new List<RecordingInfo>();

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, source, status, title, script_path, action_count, duration_ms, started_at, ended_at, created_at FROM recording ORDER BY started_at DESC LIMIT $limit;";
                command.Parameters.AddWithValue("$limit", limit);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(ReadInfo(reader));
                    }
                }
            }

            return results;
        }

        public byte[] GetEvents(string id)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT events FROM recording WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id ?? string.Empty);

                var result = command.ExecuteScalar();
                return (result == null) || (result == DBNull.Value) ? null : (byte[])result;
            }
        }

        private static RecordingInfo ReadInfo(SqliteDataReader reader)
        {
            return new RecordingInfo
            {
                Id = reader.GetString(0),
                Source = reader.IsDBNull(1) ? null : reader.GetString(1),
                Status = reader.GetString(2),
                Title = reader.IsDBNull(3) ? null : reader.GetString(3),
                ScriptPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                ActionCount = reader.GetInt32(5),
                DurationMs = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                StartedAt = DateTimeOffset.Parse(reader.GetString(7)),
                EndedAt = reader.IsDBNull(8) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(8)),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(9))
            };
        }
    }
}
