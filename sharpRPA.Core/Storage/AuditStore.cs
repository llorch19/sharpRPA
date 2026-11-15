using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace sharpRPA.Core.Storage
{
    public sealed class AuditEntry
    {
        public long Id { get; set; }
        public DateTimeOffset At { get; set; }
        public string Client { get; set; }
        public string Action { get; set; }
        public string Target { get; set; }
        public string Outcome { get; set; }
        public string Detail { get; set; }
    }

    /// <summary>
    /// Append-only audit trail for state changing tool calls. Writes are best effort:
    /// a failed audit row must never break the operation being audited.
    /// </summary>
    public sealed class AuditStore
    {
        private readonly SqliteDatabase database;

        public AuditStore(SqliteDatabase database)
        {
            this.database = database;
        }

        public void Record(string client, string action, string target, string outcome, string detail = null)
        {
            try
            {
                using (var connection = database.CreateConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
INSERT INTO audit (at, client, action, target, outcome, detail)
VALUES ($at, $client, $action, $target, $outcome, $detail);";
                    command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("O"));
                    command.Parameters.AddWithValue("$client", (object)client ?? DBNull.Value);
                    command.Parameters.AddWithValue("$action", action ?? string.Empty);
                    command.Parameters.AddWithValue("$target", (object)target ?? DBNull.Value);
                    command.Parameters.AddWithValue("$outcome", outcome ?? string.Empty);
                    command.Parameters.AddWithValue("$detail", (object)detail ?? DBNull.Value);
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception)
            {
            }
        }

        public List<AuditEntry> List(int limit = 100, int offset = 0)
        {
            var results = new List<AuditEntry>();

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT id, at, client, action, target, outcome, detail FROM audit
ORDER BY id DESC LIMIT $limit OFFSET $offset;";
                command.Parameters.AddWithValue("$limit", limit);
                command.Parameters.AddWithValue("$offset", offset);

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new AuditEntry
                        {
                            Id = reader.GetInt64(0),
                            At = DateTimeOffset.Parse(reader.GetString(1)),
                            Client = reader.IsDBNull(2) ? null : reader.GetString(2),
                            Action = reader.GetString(3),
                            Target = reader.IsDBNull(4) ? null : reader.GetString(4),
                            Outcome = reader.GetString(5),
                            Detail = reader.IsDBNull(6) ? null : reader.GetString(6)
                        });
                    }
                }
            }

            return results;
        }
    }
}
