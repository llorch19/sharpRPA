using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using sharpRPA.Core.Recording;

namespace sharpRPA.Core.Recording
{
    /// <summary>
    /// Appends recorded events to a staging file while a recording is in progress.
    /// Nothing is written to the database until the recording is stopped, so an
    /// interrupted capture never leaves a partial row behind.
    /// </summary>
    public sealed class StagingRecordingSink : IRecordingSink, IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly object sync = new object();
        private readonly FileStream stream;
        private readonly StreamWriter writer;
        private bool disposed;

        public StagingRecordingSink(string filePath, string recordingId, string source, string title, DateTimeOffset startedAt)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A staging file path is required.", nameof(filePath));
            }

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            FilePath = filePath;
            stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false)) { AutoFlush = false };

            // first line carries the capture header so an orphaned file can still be imported
            var header = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["schema"] = 1,
                ["id"] = recordingId,
                ["source"] = source,
                ["title"] = title,
                ["startedAt"] = startedAt.ToString("O")
            }, SerializerOptions);
            writer.WriteLine(header);
        }

        public string FilePath { get; }

        public long ActionCount { get; private set; }

        public DateTimeOffset? FirstTimestamp { get; private set; }

        public DateTimeOffset? LastTimestamp { get; private set; }

        public void Record(RecordedAction action)
        {
            if (action == null)
            {
                return;
            }

            lock (sync)
            {
                ThrowIfDisposed();
                writer.WriteLine(JsonSerializer.Serialize(action, SerializerOptions));
                ActionCount++;

                if (!FirstTimestamp.HasValue)
                {
                    FirstTimestamp = action.Timestamp;
                }

                LastTimestamp = action.Timestamp;
            }
        }

        /// <summary>
        /// Flushes the buffer and forces the file to disk so a crash right after this
        /// call still leaves a readable staging file.
        /// </summary>
        public void FlushToDisk()
        {
            lock (sync)
            {
                ThrowIfDisposed();
                writer.Flush();
                stream.Flush(true);
            }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;

                try
                {
                    writer.Flush();
                    stream.Flush(true);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (IOException)
                {
                }

                writer.Dispose();
                stream.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(StagingRecordingSink));
            }
        }
    }
}
