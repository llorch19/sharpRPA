using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace sharpRPA.Core.Storage
{
    public sealed class ArtifactInfo
    {
        public string Id { get; set; }
        public string RunId { get; set; }
        public string Kind { get; set; }
        public string Path { get; set; }
        public string Mime { get; set; }
        public long Bytes { get; set; }
        public int? Step { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    /// <summary>
    /// Index of run artifacts (log, screenshot, dom, trace) stored under
    /// &lt;workspace&gt;/runs/&lt;runId&gt;/. The files are the source of truth,
    /// the table is the lookup.
    /// </summary>
    public sealed class ArtifactStore
    {
        private readonly SqliteDatabase database;

        public ArtifactStore(SqliteDatabase database, string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("An artifact root directory is required.", nameof(rootDirectory));
            }

            this.database = database;
            RootDirectory = rootDirectory;
        }

        public string RootDirectory { get; }

        public string EnsureRunDirectory(string runId)
        {
            var directory = Path.Combine(RootDirectory, runId);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public ArtifactInfo Register(string runId, string kind, string path, string mime, int? step = null)
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                throw new ArgumentException("A run id is required.", nameof(runId));
            }

            var info = new ArtifactInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                RunId = runId,
                Kind = kind,
                Path = path,
                Mime = mime,
                Bytes = File.Exists(path) ? new FileInfo(path).Length : 0,
                Step = step,
                CreatedAt = DateTimeOffset.Now
            };

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
INSERT INTO artifact (id, run_id, kind, path, mime, bytes, step, created_at)
VALUES ($id, $runId, $kind, $path, $mime, $bytes, $step, $createdAt);";
                command.Parameters.AddWithValue("$id", info.Id);
                command.Parameters.AddWithValue("$runId", info.RunId);
                command.Parameters.AddWithValue("$kind", info.Kind);
                command.Parameters.AddWithValue("$path", info.Path);
                command.Parameters.AddWithValue("$mime", (object)info.Mime ?? DBNull.Value);
                command.Parameters.AddWithValue("$bytes", info.Bytes);
                command.Parameters.AddWithValue("$step", (object)info.Step ?? DBNull.Value);
                command.Parameters.AddWithValue("$createdAt", info.CreatedAt.ToString("O"));
                command.ExecuteNonQuery();
            }

            return info;
        }

        public List<ArtifactInfo> List(string runId)
        {
            var results = new List<ArtifactInfo>();

            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, run_id, kind, path, mime, bytes, step, created_at FROM artifact WHERE run_id = $runId ORDER BY IFNULL(step, 0), created_at;";
                command.Parameters.AddWithValue("$runId", runId ?? string.Empty);

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

        public ArtifactInfo Get(string id)
        {
            using (var connection = database.CreateConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, run_id, kind, path, mime, bytes, step, created_at FROM artifact WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id ?? string.Empty);

                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? Read(reader) : null;
                }
            }
        }

        private static ArtifactInfo Read(SqliteDataReader reader)
        {
            return new ArtifactInfo
            {
                Id = reader.GetString(0),
                RunId = reader.GetString(1),
                Kind = reader.GetString(2),
                Path = reader.GetString(3),
                Mime = reader.IsDBNull(4) ? null : reader.GetString(4),
                Bytes = reader.GetInt64(5),
                Step = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(7))
            };
        }
    }
}
