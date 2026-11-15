using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace sharpRPA.Core.Storage
{
    public sealed class RunRecord
    {
        public string Id { get; set; }
        public string ScriptPath { get; set; }
        public string Status { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public int? CurrentLine { get; set; }
        public string Error { get; set; }
    }

    public sealed class RunLogEntry
    {
        public long Seq { get; set; }
        public int? LineNumber { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Durable run history. The run row is written when a run starts, and the log is
    /// flushed in a single transaction when it finishes, so a long script does not pay
    /// one commit per command.
    /// </summary>
    public sealed class RunStore
    {
        private readonly SqliteDatabase database;

        public RunStore(SqliteDatabase database)
        {
            this.database = database;
        }

        public void Insert(RunRecord run)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO run (id, script_path, status, started_at, ended_at, current_line, error)
VALUES ($id, $scriptPath, $status, $startedAt, $endedAt, $currentLine, $error);";
                command.Parameters.AddWithValue("$id", run.Id);
                command.Parameters.AddWithValue("$scriptPath", run.ScriptPath);
                command.Parameters.AddWithValue("$status", run.Status);
                command.Parameters.AddWithValue("$startedAt", run.StartedAt.ToString("O"));
                command.Parameters.AddWithValue("$endedAt", run.EndedAt.HasValue ? (object)run.EndedAt.Value.ToString("O") : DBNull.Value);
                command.Parameters.AddWithValue("$currentLine", (object)run.CurrentLine ?? DBNull.Value);
                command.Parameters.AddWithValue("$error", (object)run.Error ?? DBNull.Value);
                command.ExecuteNonQuery();
            }
        }

        /// <summary>Completes a run and flushes its whole log in one transaction.</summary>
        public void Complete(RunRecord run, IReadOnlyList<RunLogEntry> log)
        {
            if (run == null)
            {
                throw new ArgumentNullException(nameof(run));
            }

            using (var connection = database.CreateConnection())
            using (var transaction = connection.BeginTransaction())
            {
                using (var update = connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandText = @"
UPDATE run SET status = $status, ended_at = $endedAt, current_line = $currentLine, error = $error
WHERE id = $id;";
                    update.Parameters.AddWithValue("$id", run.Id);
                    update.Parameters.AddWithValue("$status", run.Status);
                    update.Parameters.AddWithValue("$endedAt", run.EndedAt.HasValue ? (object)run.EndedAt.Value.ToString("O") : DBNull.Value);
                    update.Parameters.AddWithValue("$currentLine", (object)run.CurrentLine ?? DBNull.Value);
                    update.Parameters.AddWithValue("$error", (object)run.Error ?? DBNull.Value);
                    update.ExecuteNonQuery();
                }

                if (log != null)
                {
                    using (var insert = connection.CreateCommand())
                    {
                        insert.Transaction = transaction;
                        insert.CommandText = @"
INSERT OR REPLACE INTO run_log (run_id, seq, line_number, message)
VALUES ($runId, $seq, $lineNumber, $message);";

                        var runId = insert.Parameters.Add("$runId", SqliteType.Text);
                        var seq = insert.Parameters.Add("$seq", SqliteType.Integer);
                        var lineNumber = insert.Parameters.Add("$lineNumber", SqliteType.Integer);
                        var message = insert.Parameters.Add("$message", SqliteType.Text);

                        for (var i = 0; i < log.Count; i++)
                        {
                            var entry = log[i];
                            runId.Value = run.Id;
                            seq.Value = i;
                            lineNumber.Value = entry.LineNumber.HasValue ? (object)entry.LineNumber.Value : DBNull.Value;
                            message.Value = entry.Message ?? string.Empty;
                            insert.ExecuteNonQuery();
                        }
                    }
                }

                transaction.Commit();
            }
        }

        public RunRecord Get(string id)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = SelectColumns + " WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id ?? string.Empty);

                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? Read(reader) : null;
                }
            }
        }

        public List<RunRecord> List(string status, int limit, int offset)
        {
            var results = new List<RunRecord>();

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                var filter = BuildStatusFilter(command, status);
                command.CommandText = SelectColumns + filter + " ORDER BY started_at DESC, id LIMIT $limit OFFSET $offset;";
                command.Parameters.AddWithValue("$limit", limit);
                command.Parameters.AddWithValue("$offset", offset);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(Read(reader));
                    }
                }
            }

            return results;
        }

        public int Count(string status)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                var filter = BuildStatusFilter(command, status);
                command.CommandText = "SELECT COUNT(*) FROM run" + filter + ";";
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public int CountLog(string runId)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM run_log WHERE run_id = $runId;";
                command.Parameters.AddWithValue("$runId", runId ?? string.Empty);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        /// <summary>Returns the newest <paramref name="limit"/> log entries, oldest first.</summary>
        public List<RunLogEntry> TailLog(string runId, int limit)
        {
            var results = new List<RunLogEntry>();

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT seq, line_number, message FROM (
    SELECT seq, line_number, message FROM run_log WHERE run_id = $runId ORDER BY seq DESC LIMIT $limit
) ORDER BY seq;";
                command.Parameters.AddWithValue("$runId", runId ?? string.Empty);
                command.Parameters.AddWithValue("$limit", limit);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new RunLogEntry
                        {
                            Seq = reader.GetInt64(0),
                            LineNumber = reader.IsDBNull(1) ? (int?)null : reader.GetInt32(1),
                            Message = reader.GetString(2)
                        });
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Marks runs left in 'running' by a crashed/restarted server as failed. The server
        /// owns one workspace, so no other process should be running a script on it.
        /// </summary>
        public int MarkInterruptedRuns()
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
UPDATE run SET status = 'failed', ended_at = $now, error = 'Interrupted by server restart'
WHERE status = 'running';";
                command.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("O"));
                return command.ExecuteNonQuery();
            }
        }

        private const string SelectColumns =
            "SELECT id, script_path, status, started_at, ended_at, current_line, error FROM run";

        private static string BuildStatusFilter(SqliteCommand command, string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return string.Empty;
            }

            // rows are written lowercase, but compare case-insensitively so any
            // pre-existing PascalCase rows still match
            command.Parameters.AddWithValue("$status", status.Trim().ToLowerInvariant());
            return " WHERE LOWER(status) = $status";
        }

        private static RunRecord Read(SqliteDataReader reader)
        {
            return new RunRecord
            {
                Id = reader.GetString(0),
                ScriptPath = reader.GetString(1),
                Status = reader.GetString(2),
                StartedAt = DateTimeOffset.Parse(reader.GetString(3)),
                EndedAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(4)),
                CurrentLine = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5),
                Error = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }
    }
}
