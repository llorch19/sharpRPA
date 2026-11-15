using System;
using System.Linq;
using sharpRPA.Core.Storage;
using Xunit;

namespace sharpRPA.Tests
{
    public class AuditStoreTests
    {
        private static AuditStore CreateStore(TempWorkspace workspace)
        {
            var database = new SqliteDatabase(workspace.DatabasePath);
            database.Initialize();
            return new AuditStore(database);
        }

        [Fact]
        public void Record_PersistsEveryFieldAndReturnsNewestFirst()
        {
            using (var workspace = new TempWorkspace())
            {
                var store = CreateStore(workspace);

                store.Record("opencode", "run_script", "a.xml", "ok");
                store.Record("opencode", "record_start", "browser", "denied", "PERMISSION_DENIED");
                store.Record(null, "update_script", "b.xml", "error", "IOException: disk full");

                var entries = store.List();

                Assert.Equal(3, entries.Count);
                Assert.Equal("update_script", entries[0].Action);
                Assert.Null(entries[0].Client);
                Assert.Equal("IOException: disk full", entries[0].Detail);
                Assert.Equal("PERMISSION_DENIED", entries[1].Detail);
                Assert.Equal("run_script", entries[2].Action);
                Assert.Equal("opencode", entries[2].Client);
                Assert.Equal("ok", entries[2].Outcome);
                Assert.True(entries[0].At >= entries[2].At);
            }
        }

        [Fact]
        public void List_Paginates()
        {
            using (var workspace = new TempWorkspace())
            {
                var store = CreateStore(workspace);

                for (var i = 1; i <= 5; i++)
                {
                    store.Record("client", "run_script", "s" + i + ".xml", "ok");
                }

                Assert.Equal(5, store.List(10, 0).Count);
                Assert.Equal(2, store.List(2, 0).Count);
                Assert.Equal("s5.xml", store.List(2, 0)[0].Target);
                Assert.Equal("s3.xml", store.List(2, 2)[0].Target);
                Assert.Equal("s1.xml", store.List(1, 4)[0].Target);
            }
        }

        [Fact]
        public void Record_NeverThrowsOnAMissingTable()
        {
            using (var workspace = new TempWorkspace())
            {
                // no Initialize() call: the audit table does not exist yet
                var store = new AuditStore(new SqliteDatabase(workspace.DatabasePath));

                // must not throw even though the table has not been created yet
                store.Record("client", "run_script", "a.xml", "ok");
            }
        }
    }
}
