using System;
using System.Collections.Generic;
using System.Linq;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Recording;
using sharpRPA.Core.Script;
using sharpRPA.Core.Services;
using sharpRPA.Core.Storage;
using Xunit;
using ScriptModel = sharpRPA.Core.Script.Script;

namespace sharpRPA.Tests
{
    public class TargetIndexTests
    {
        public TargetIndexTests()
        {
            // makes WindowProbeCommand serializable for these tests
            ScriptModel.RegisterKnownTypes(typeof(WindowProbeCommand).Assembly);
        }

        private static ScriptCatalog CreateCatalog(TempScriptDirectory folder)
        {
            return new ScriptCatalog(folder.Directory);
        }

        private static TargetIndexBuilder CreateBuilder(TempWorkspace workspace, out TargetIndexStore store)
        {
            var database = new SqliteDatabase(workspace.DatabasePath);
            database.Initialize();
            store = new TargetIndexStore(database);
            return new TargetIndexBuilder(store);
        }

        [Fact]
        public void RebuildScripts_IndexesUrlsPathsSelectorsAndWindows()
        {
            using (var folder = new TempScriptDirectory())
            using (var workspace = new TempWorkspace())
            {
                folder.Write("targets.xml", new List<ScriptCommand>
                {
                    new HTTPRequestCommand { v_WebRequestURL = "https://api.example.com/v1/orders", v_userVariableName = "Response" },
                    new RunScriptCommand { v_ScriptPath = @"C:\tools\nightly.cmd" },
                    new SeleniumBrowserElementActionCommand
                    {
                        v_InstanceName = "default",
                        v_SeleniumSearchType = "XPath",
                        v_SeleniumSearchParameter = "//div[@id='submit']",
                        v_SeleniumElementAction = "Invoke Click"
                    },
                    new WindowProbeCommand { v_WindowName = "Calculator" }
                });

                TargetIndexStore store;
                var builder = CreateBuilder(workspace, out store);
                var indexed = builder.RebuildScripts(CreateCatalog(folder));

                Assert.True(indexed >= 4, "expected at least 4 targets, got " + indexed);

                var all = store.Find(null);
                Assert.Contains(all, hit => hit.Kind == "url" && hit.Locator == "https://api.example.com/v1/orders" && hit.LocatorKind == "url");
                Assert.Contains(all, hit => hit.Kind == "file" && hit.Locator == @"C:\tools\nightly.cmd");
                Assert.Contains(all, hit => hit.Kind == "ui_element" && hit.Locator == "//div[@id='submit']");
                Assert.Contains(all, hit => hit.Kind == "window" && hit.Locator == "Calculator" && hit.AppHint == "Calculator");

                // command metadata such as the search *type* must not be indexed as a locator
                Assert.DoesNotContain(all, hit => hit.Locator == "XPath");
                Assert.DoesNotContain(all, hit => hit.Locator == "Invoke Click");
            }
        }

        [Fact]
        public void RebuildScripts_IndexesDataTableParameters()
        {
            using (var folder = new TempScriptDirectory())
            using (var workspace = new TempWorkspace())
            {
                var elementAction = new SeleniumBrowserElementActionCommand
                {
                    v_InstanceName = "default",
                    v_SeleniumSearchType = "XPath",
                    v_SeleniumSearchParameter = "//body",
                    v_SeleniumElementAction = "Set Text"
                };
                elementAction.v_WebActionParameterTable.Rows.Add("Search Parameter", "//button[@aria-label='Save']");
                elementAction.v_WebActionParameterTable.Rows.Add("Text To Set", "hello");

                folder.Write("table.xml", new List<ScriptCommand> { elementAction });

                TargetIndexStore store;
                var builder = CreateBuilder(workspace, out store);
                builder.RebuildScripts(CreateCatalog(folder));

                var all = store.Find(null);
                Assert.Contains(all, hit => hit.Locator == "//button[@aria-label='Save']" && hit.Kind == "ui_element");
                Assert.DoesNotContain(all, hit => hit.Locator == "hello");
            }
        }

        [Fact]
        public void RebuildScripts_IsIdempotent()
        {
            using (var folder = new TempScriptDirectory())
            using (var workspace = new TempWorkspace())
            {
                folder.Write("once.xml", new List<ScriptCommand>
                {
                    new HTTPRequestCommand { v_WebRequestURL = "https://example.com", v_userVariableName = "R" }
                });

                TargetIndexStore store;
                var builder = CreateBuilder(workspace, out store);

                builder.RebuildScripts(CreateCatalog(folder));
                var first = store.Count("script");
                builder.RebuildScripts(CreateCatalog(folder));
                var second = store.Count("script");

                Assert.True(first > 0);
                Assert.Equal(first, second);
            }
        }

        [Fact]
        public void Find_FiltersByKindAndQuery()
        {
            using (var folder = new TempScriptDirectory())
            using (var workspace = new TempWorkspace())
            {
                folder.Write("find.xml", new List<ScriptCommand>
                {
                    new HTTPRequestCommand { v_WebRequestURL = "https://api.example.com/orders", v_userVariableName = "R" },
                    new RunScriptCommand { v_ScriptPath = @"D:\jobs\export.cmd" }
                });

                TargetIndexStore store;
                var builder = CreateBuilder(workspace, out store);
                builder.RebuildScripts(CreateCatalog(folder));

                Assert.Single(store.Find("export", "file"));
                Assert.Single(store.Find("orders", "url"));
                Assert.Empty(store.Find("nothing-matches"));
                Assert.Empty(store.Find("export", "url"));
                Assert.Equal(2, store.Find(null).Count);
            }
        }

        [Fact]
        public void RebuildRecording_IndexesRecordedTargets()
        {
            using (var workspace = new TempWorkspace())
            {
                var service = workspace.CreateRecordingService();
                var session = service.Start("browser", "checkout");
                session.Sink.Record(new RecordedAction
                {
                    ActionType = "click",
                    Target = "//button[@id='pay']",
                    Metadata = new Dictionary<string, string>
                    {
                        ["kind"] = "ui_element",
                        ["selector"] = "//button[@id='pay']",
                        ["selectorKind"] = "xpath",
                        ["pageUrl"] = "https://shop.example.com/cart"
                    }
                });
                session.Sink.Record(new RecordedAction
                {
                    ActionType = "open",
                    Target = @"C:\data\orders.csv",
                    Metadata = new Dictionary<string, string> { ["kind"] = "file" }
                });
                var info = session.Stop();

                var hits = service.FindTargets("pay");
                Assert.Single(hits);
                Assert.Equal("recording", hits[0].SourceKind);
                Assert.Equal(info.Id, hits[0].SourceId);
                Assert.Equal("ui_element", hits[0].Kind);
                Assert.Equal("xpath", hits[0].LocatorKind);

                var files = service.FindTargets("orders.csv", "file");
                Assert.Single(files);
            }
        }

        [Fact]
        public void RebuildTargetIndex_CoversScriptsAndRecordings()
        {
            using (var folder = new TempScriptDirectory())
            using (var workspace = new TempWorkspace())
            {
                folder.Write("mixed.xml", new List<ScriptCommand>
                {
                    new HTTPRequestCommand { v_WebRequestURL = "https://example.com/a", v_userVariableName = "R" }
                });

                var service = workspace.CreateRecordingService();
                var session = service.Start("windows-desktop");
                session.Sink.Record(new RecordedAction
                {
                    ActionType = "click",
                    Target = "OK",
                    Metadata = new Dictionary<string, string> { ["kind"] = "ui_element", ["window"] = "Notepad" }
                });
                session.Stop();

                var indexed = service.RebuildTargetIndex(CreateCatalog(folder));

                Assert.True(indexed >= 2, "expected script + recording targets, got " + indexed);
                Assert.Single(service.FindTargets("example.com", "url"));
                Assert.Single(service.FindTargets("OK", "ui_element"));
                Assert.Equal("Notepad", service.FindTargets("OK", "ui_element").Single().AppHint);
            }
        }

        [Fact]
        public void ExtractFromEvent_UsesTargetWhenNoMetadataIsPresent()
        {
            var records = TargetIndexBuilder.ExtractFromEvent(
                new RecordedAction { ActionType = "navigate", Target = "https://example.com/landing" },
                "rec-1",
                0).ToList();

            var record = Assert.Single(records);
            Assert.Equal("url", record.Kind);
            Assert.Equal("recording", record.SourceKind);
            Assert.True(record.Seq == 0);
        }
    }
}
