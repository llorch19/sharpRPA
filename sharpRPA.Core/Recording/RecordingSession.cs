using System;
using sharpRPA.Core.Storage;

namespace sharpRPA.Core.Recording
{
    /// <summary>
    /// One in-flight capture. Events go to a staging file; the database is only
    /// touched when <see cref="Stop"/> commits the finished recording.
    /// </summary>
    public sealed class RecordingSession : IDisposable
    {
        private readonly RecordingService owner;
        private bool finished;

        internal RecordingSession(RecordingService owner, string id, string source, string title, string stagingPath, DateTimeOffset startedAt)
        {
            this.owner = owner;
            Id = id;
            Source = source;
            Title = title;
            StagingPath = stagingPath;
            StartedAt = startedAt;
            Sink = new StagingRecordingSink(stagingPath, id, source, title, startedAt);
        }

        public string Id { get; }

        public string Source { get; }

        public string Title { get; }

        public string StagingPath { get; }

        public DateTimeOffset StartedAt { get; }

        public IRecordingSink Sink { get; }

        public bool IsFinished
        {
            get { return finished; }
        }

        public long ActionCount
        {
            get
            {
                var stagingSink = Sink as StagingRecordingSink;
                return stagingSink == null ? 0 : stagingSink.ActionCount;
            }
        }

        /// <summary>
        /// Flushes the staging file and commits it to SQLite as a completed recording.
        /// </summary>
        public RecordingInfo Stop(string title = null, string scriptPath = null)
        {
            if (finished)
            {
                throw new InvalidOperationException("This recording has already been finished.");
            }

            finished = true;
            return owner.Commit(this, title, scriptPath);
        }

        /// <summary>
        /// Drops the capture without writing anything to the database.
        /// </summary>
        public void Abort()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            owner.Discard(this);
        }

        public void Dispose()
        {
            if (!finished)
            {
                Abort();
                return;
            }

            (Sink as IDisposable)?.Dispose();
        }
    }
}
