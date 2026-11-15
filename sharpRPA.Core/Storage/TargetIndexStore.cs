using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace sharpRPA.Core.Storage
{
    /// <summary>
    /// One extracted operation target (a control, file, url or window) together with
    /// where it was observed. Never authored by hand - always derived from scripts
    /// or recordings, so the whole table can be rebuilt at any time.
    /// </summary>
    public sealed class TargetRecord
    {
        public string Kind { get; set; }
        public string Locator { get; set; }
        public string LocatorKind { get; set; }
        public string AppHint { get; set; }
        public string SourceKind { get; set; }
        public string SourceId { get; set; }
        public long? Seq { get; set; }
        public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.Now;
    }

    public sealed class TargetHit
    {
        public string Kind { get; set; }
        public string Locator { get; set; }
        public string LocatorKind { get; set; }
        public string AppHint { get; set; }
        public string SourceKind { get; set; }
        public string SourceId { get; set; }
        public long? Seq { get; set; }
        public int ObservedCount { get; set; }
        public DateTimeOffset LastSeenAt { get; set; }
    }

    public sealed class TargetIndexStore
    {
        private readonly SqliteDatabase database;

        public TargetIndexStore(SqliteDatabase database)
        {
            this.database = database;
        }

        public void Clear(string sourceKind = null, string sourceId = null)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                var sql = "DELETE FROM target_index WHERE 1 = 1";
                if (!string.IsNullOrEmpty(sourceKind))
                {
                    sql += " AND source_kind = $sourceKind";
                    command.Parameters.AddWithValue("$sourceKind", sourceKind);
                }

                if (!string.IsNullOrEmpty(sourceId))
                {
                    sql += " AND source_id = $sourceId";
                    command.Parameters.AddWithValue("$sourceId", sourceId);
                }

                command.CommandText = sql + ";";
                command.ExecuteNonQuery();
            }
        }

        public void Upsert(TargetRecord record)
        {
            if ((record == null) || string.IsNullOrWhiteSpace(record.Locator))
            {
                return;
            }

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO target_index (kind, locator, locator_kind, app_hint, source_kind, source_id, seq, observed_count, last_seen_at)
VALUES ($kind, $locator, $locatorKind, $appHint, $sourceKind, $sourceId, $seq, 1, $lastSeenAt)
ON CONFLICT(kind, locator, source_kind, source_id, IFNULL(seq, -1)) DO UPDATE
SET observed_count = observed_count + 1, last_seen_at = $lastSeenAt;";
                command.Parameters.AddWithValue("$kind", record.Kind ?? "unknown");
                command.Parameters.AddWithValue("$locator", record.Locator);
                command.Parameters.AddWithValue("$locatorKind", (object)record.LocatorKind ?? DBNull.Value);
                command.Parameters.AddWithValue("$appHint", (object)record.AppHint ?? DBNull.Value);
                command.Parameters.AddWithValue("$sourceKind", record.SourceKind);
                command.Parameters.AddWithValue("$sourceId", record.SourceId);
                command.Parameters.AddWithValue("$seq", record.Seq.HasValue ? (object)record.Seq.Value : DBNull.Value);
                command.Parameters.AddWithValue("$lastSeenAt", record.LastSeenAt.ToString("O"));
                command.ExecuteNonQuery();
            }
        }

        public List<TargetHit> Find(string query, string kind = null, int limit = 100)
        {
            var results = new List<TargetHit>();

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                var sql = "SELECT kind, locator, locator_kind, app_hint, source_kind, source_id, seq, observed_count, last_seen_at FROM target_index WHERE 1 = 1";

                if (!string.IsNullOrWhiteSpace(query))
                {
                    sql += " AND (locator LIKE $query OR IFNULL(app_hint, '') LIKE $query)";
                    command.Parameters.AddWithValue("$query", "%" + query + "%");
                }

                if (!string.IsNullOrWhiteSpace(kind))
                {
                    sql += " AND kind = $kind";
                    command.Parameters.AddWithValue("$kind", kind);
                }

                sql += " ORDER BY locator, source_kind, source_id, IFNULL(seq, 0) LIMIT $limit;";
                command.CommandText = sql;
                command.Parameters.AddWithValue("$limit", limit);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new TargetHit
                        {
                            Kind = reader.GetString(0),
                            Locator = reader.GetString(1),
                            LocatorKind = reader.IsDBNull(2) ? null : reader.GetString(2),
                            AppHint = reader.IsDBNull(3) ? null : reader.GetString(3),
                            SourceKind = reader.GetString(4),
                            SourceId = reader.GetString(5),
                            Seq = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                            ObservedCount = reader.GetInt32(7),
                            LastSeenAt = DateTimeOffset.Parse(reader.GetString(8))
                        });
                    }
                }
            }

            return results;
        }

        public int Count(string sourceKind = null)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                if (string.IsNullOrEmpty(sourceKind))
                {
                    command.CommandText = "SELECT COUNT(*) FROM target_index;";
                }
                else
                {
                    command.CommandText = "SELECT COUNT(*) FROM target_index WHERE source_kind = $sourceKind;";
                    command.Parameters.AddWithValue("$sourceKind", sourceKind);
                }

                return Convert.ToInt32(command.ExecuteScalar());
            }
        }
    }
}
