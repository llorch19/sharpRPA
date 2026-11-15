using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace sharpRPA.Core.Storage
{
    /// <summary>
    /// Owns the workspace SQLite database (recording archive + target index) and its schema.
    /// </summary>
    public sealed class SqliteDatabase
    {
        public const int SchemaVersion = 2;

        private const string Schema = @"
CREATE TABLE IF NOT EXISTS recording (
    id           TEXT PRIMARY KEY,
    source       TEXT NOT NULL,
    status       TEXT NOT NULL,
    title        TEXT,
    script_path  TEXT,
    action_count INTEGER NOT NULL DEFAULT 0,
    duration_ms  INTEGER,
    started_at   TEXT NOT NULL,
    ended_at     TEXT,
    created_at   TEXT NOT NULL,
    events       BLOB
);

CREATE TABLE IF NOT EXISTS target_index (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    kind          TEXT NOT NULL,
    locator       TEXT NOT NULL,
    locator_kind  TEXT,
    app_hint      TEXT,
    source_kind   TEXT NOT NULL,
    source_id     TEXT NOT NULL,
    seq           INTEGER,
    observed_count INTEGER NOT NULL DEFAULT 1,
    last_seen_at  TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS artifact (
    id         TEXT PRIMARY KEY,
    run_id     TEXT NOT NULL,
    kind       TEXT NOT NULL,
    path       TEXT NOT NULL,
    mime       TEXT,
    bytes      INTEGER NOT NULL DEFAULT 0,
    step       INTEGER,
    created_at TEXT NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_target_index
    ON target_index(kind, locator, source_kind, source_id, IFNULL(seq, -1));
CREATE INDEX IF NOT EXISTS ix_target_locator ON target_index(locator);
CREATE INDEX IF NOT EXISTS ix_recording_started ON recording(started_at DESC);
CREATE INDEX IF NOT EXISTS ix_artifact_run ON artifact(run_id, step);
";

        public SqliteDatabase(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentException("A database path is required.", nameof(databasePath));
            }

            DatabasePath = databasePath;
        }

        public string DatabasePath { get; }

        public void Initialize()
        {
            using (var connection = CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = Schema;
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA user_version = " + SchemaVersion + ";";
                    command.ExecuteNonQuery();
                }
            }
        }

        public SqliteConnection CreateConnection()
        {
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ConnectionString;

            var connection = new SqliteConnection(connectionString);
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                // WAL lets the WinForms UI and the MCP server touch the same file
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
                command.ExecuteScalar();
            }

            return connection;
        }
    }
}
