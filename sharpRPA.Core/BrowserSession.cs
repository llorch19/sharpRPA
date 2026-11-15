//Copyright (c) 2017 Jason Bayldon
//
//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at
//
//   http://www.apache.org/licenses/LICENSE-2.0
//
//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.
using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace sharpRPA.Core
{
    public sealed class BrowserSession : IDisposable
    {
        private readonly IPlaywright playwright;

        public IBrowser Browser { get; }
        public IPage Page { get; }

        private BrowserSession(IPlaywright playwright, IBrowser browser, IPage page)
        {
            this.playwright = playwright;
            Browser = browser;
            Page = page;
        }

        public static async Task<BrowserSession> LaunchAsync()
        {
            var playwright = await Microsoft.Playwright.Playwright.CreateAsync();

            try
            {
                var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = false });
                var page = await browser.NewPageAsync();
                return new BrowserSession(playwright, browser, page);
            }
            catch (PlaywrightException ex)
            {
                playwright.Dispose();
                throw new Exception("Unable to launch the Playwright browser. Run 'playwright.ps1 install chromium' from the application folder and try again.", ex);
            }
        }

        public ILocator FindElement(string searchType, string searchParameter)
        {
            switch (searchType)
            {
                case "Find Element By XPath":
                    return Page.Locator("xpath=" + searchParameter);

                case "Find Element By ID":
                    return Page.Locator("[id=\"" + EscapeCssString(searchParameter) + "\"]");

                case "Find Element By Name":
                    return Page.Locator("[name=\"" + EscapeCssString(searchParameter) + "\"]");

                case "Find Element By Tag Name":
                    return Page.Locator(searchParameter);

                case "Find Element By Class Name":
                    return Page.Locator("[class~=\"" + EscapeCssString(searchParameter) + "\"]");

                default:
                    throw new Exception("Search Type was not found");
            }
        }

        public async Task CloseAsync()
        {
            try
            {
                if (Page != null && !Page.IsClosed)
                {
                    await Page.CloseAsync();
                }
            }
            catch
            {
            }

            try
            {
                if (Browser != null)
                {
                    await Browser.CloseAsync();
                }
            }
            catch
            {
            }

            playwright.Dispose();
        }

        public void Dispose()
        {
            Task.Run(CloseAsync).GetAwaiter().GetResult();
        }

        private static string EscapeCssString(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
