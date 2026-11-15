using System;
using System.IO;
using sharpRPA.Core.Recording;

namespace sharpRPA.Tests
{
    /// <summary>
    /// Isolated database + staging + scripts folder for storage tests.
    /// </summary>
    public sealed class TempWorkspace : IDisposable
    {
        public TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "sharprpa-ws-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "scripts"));
            Directory.CreateDirectory(Path.Combine(Root, "recording-staging"));
        }

        public string Root { get; }

        public string DatabasePath
        {
            get { return Path.Combine(Root, "sharprpa.db"); }
        }

        public string StagingDirectory
        {
            get { return Path.Combine(Root, "recording-staging"); }
        }

        public string ScriptsDirectory
        {
            get { return Path.Combine(Root, "scripts"); }
        }

        public RecordingService CreateRecordingService()
        {
            return new RecordingService(DatabasePath, StagingDirectory);
        }

        public void Dispose()
        {
            try
            {
                // release pooled sqlite handles before removing the folder
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
