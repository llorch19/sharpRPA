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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Data;
using Microsoft.Playwright;
using System.Net.Mail;
using sharpRPA.Core;
using sharpRPA.Core.Execution;
using System.Net;
using System.IO;

namespace sharpRPA.Core.AutomationCommands
{
    #region Base Command

    // Command types are registered at runtime through Script.RegisterKnownTypes so that
    // platform specific commands can live in a separate assembly.

    [Serializable]
    public abstract class ScriptCommand
    {
        [XmlAttribute]
        public string CommandName { get; set; }
        [XmlAttribute]
        public bool IsCommented { get; set; }
        [XmlAttribute]
        public string SelectionName { get; set; }
        [XmlAttribute]
        public int DefaultPause { get; set; }
        [XmlAttribute]
        public int LineNumber { get; set; }
        [XmlAttribute]
        public bool PauseBeforeExeucution { get; set; }
        [XmlIgnore]
        public System.Drawing.Color DisplayForeColor { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Comment Field (Optional)")]
        public string v_Comment { get; set; }
        [XmlAttribute]
        public bool CommandEnabled { get; set; }

        public ScriptCommand()
        {
            this.DisplayForeColor = System.Drawing.Color.Black;
            this.CommandEnabled = false;
            this.DefaultPause = 250;
            this.IsCommented = false;
        }

        public virtual void RunCommand(IScriptExecutionContext context)
        {
            System.Threading.Thread.Sleep(DefaultPause);
        }
        public virtual void RunCommand(IScriptExecutionContext context, Core.Script.ScriptAction command)
        {
            System.Threading.Thread.Sleep(DefaultPause);
        }

        public virtual string GetDisplayValue()
        {
            return SelectionName;
        }
    }

    #endregion Base Command

    #region Web Browser

    [Serializable]
    [Attributes.ClassAttributes.Group("Web Browser Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to create a new Playwright web browser session.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Microsoft Playwright to achieve automation.")]
    public class SeleniumBrowserCreateCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }

        public SeleniumBrowserCreateCommand()
        {
            this.CommandName = "SeleniumBrowserCreateCommand";
            this.SelectionName = "Create Browser";
            this.v_InstanceName = "default";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            var sendingInstance = (IScriptExecutionContext)context;
            var newBrowserSession = BrowserSession.LaunchAsync().GetAwaiter().GetResult();

            if (sendingInstance.AppInstances.ContainsKey(v_InstanceName))
            {
                if (sendingInstance.AppInstances[v_InstanceName] is BrowserSession existingSession)
                {
                    existingSession.Dispose();
                }

                sendingInstance.AppInstances.Remove(v_InstanceName);
            }

            sendingInstance.AppInstances.Add(v_InstanceName, newBrowserSession);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }

    [Serializable]
    [Attributes.ClassAttributes.Group("Web Browser Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to navigate a Playwright web browser session.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Microsoft Playwright to achieve automation.")]
    public class SeleniumBrowserNavigateURLCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the URL to navigate to")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_URL { get; set; }

        public SeleniumBrowserNavigateURLCommand()
        {
            this.CommandName = "SeleniumBrowserNavigateURLCommand";
            this.SelectionName = "Navigate to URL";
            this.v_InstanceName = "default";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            object browserObject = null;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out browserObject))
            {
                var browserInstance = (BrowserSession)browserObject;
                browserInstance.Page.GotoAsync(v_URL.ConvertToUserVariable(context)).GetAwaiter().GetResult();
            }
            else
            {
                throw new Exception("Session Instance was not found");
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [URL: '" + v_URL + "', Instance Name: '" + v_InstanceName + "']";
        }
    }

    [Serializable]
    [Attributes.ClassAttributes.Group("Web Browser Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to navigate forward a Playwright web browser session.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Microsoft Playwright to achieve automation.")]
    public class SeleniumBrowserNavigateForwardCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }

        public SeleniumBrowserNavigateForwardCommand()
        {
            this.CommandName = "SeleniumBrowserNavigateForwardCommand";
            this.SelectionName = "Navigate Forward";
            this.v_InstanceName = "default";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            object browserObject = null;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out browserObject))
            {
                var browserInstance = (BrowserSession)browserObject;
                browserInstance.Page.GoForwardAsync().GetAwaiter().GetResult();
            }
            else
            {
                throw new Exception("Session Instance was not found");
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }

    [Serializable]
    [Attributes.ClassAttributes.Group("Web Browser Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to navigate backwards in a Playwright web browser session.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Microsoft Playwright to achieve automation.")]
    public class SeleniumBrowserNavigateBackCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }

        public SeleniumBrowserNavigateBackCommand()
        {
            this.CommandName = "SeleniumBrowserNavigateBackCommand";
            this.SelectionName = "Navigate Back";
            this.v_InstanceName = "default";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            object browserObject = null;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out browserObject))
            {
                var browserInstance = (BrowserSession)browserObject;
                browserInstance.Page.GoBackAsync().GetAwaiter().GetResult();
            }
            else
            {
                throw new Exception("Session Instance was not found");
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Web Browser Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to refresh a Playwright web browser session.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Microsoft Playwright to achieve automation.")]
    public class SeleniumBrowserRefreshCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }

        public SeleniumBrowserRefreshCommand()
        {
            this.CommandName = "SeleniumBrowserRefreshCommand";
            this.SelectionName = "Refresh";
            this.v_InstanceName = "default";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            object browserObject = null;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out browserObject))
            {
                var browserInstance = (BrowserSession)browserObject;
                browserInstance.Page.ReloadAsync().GetAwaiter().GetResult();
            }
            else
            {
                throw new Exception("Session Instance was not found");
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Web Browser Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to close a Playwright web browser session.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Microsoft Playwright to achieve automation.")]
    public class SeleniumBrowserCloseCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }

        public SeleniumBrowserCloseCommand()
        {
            this.CommandName = "SeleniumBrowserCloseCommand";
            this.SelectionName = "Close Browser";
            this.v_InstanceName = "default";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            object browserObject = null;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out browserObject))
            {
                var browserInstance = (BrowserSession)browserObject;
                browserInstance.CloseAsync().GetAwaiter().GetResult();
                sendingInstance.AppInstances.Remove(v_InstanceName);
            }
            else
            {
                throw new Exception("Session Instance was not found");
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Web Browser Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to manipulate elements within a Playwright web browser session.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Microsoft Playwright to achieve automation.")]
    public class SeleniumBrowserElementActionCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Element Search Method")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Find Element By XPath")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Find Element By ID")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Find Element By Name")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Find Element By Tag Name")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Find Element By Class Name")]
        public string v_SeleniumSearchType { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Element Search Parameter")]
        public string v_SeleniumSearchParameter { get; set; }
        [XmlElement]
        [Attributes.PropertyAttributes.PropertyDescription("Element Action")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Invoke Click")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Left Click")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Right Click")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Middle Click")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Set Text")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Get Text")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Get Attribute")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Wait For Element To Exist")]
        public string v_SeleniumElementAction { get; set; }
        [XmlElement]
        [Attributes.PropertyAttributes.PropertyDescription("Additional Parameters")]
        public DataTable v_WebActionParameterTable { get; set; }
        public SeleniumBrowserElementActionCommand()
        {
            this.CommandName = "SeleniumBrowserElementActionCommand";
            this.SelectionName = "Element Action";
            this.v_InstanceName = "default";
            this.CommandEnabled = true;

            this.v_WebActionParameterTable = new System.Data.DataTable();
            this.v_WebActionParameterTable.TableName = DateTime.Now.ToString("WebActionParamTable" + DateTime.Now.ToString("MMddyy.hhmmss"));
            this.v_WebActionParameterTable.Columns.Add("Parameter Name");
            this.v_WebActionParameterTable.Columns.Add("Parameter Value");
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            object browserObject = null;
            var sendingInstance = (IScriptExecutionContext)context;

            if (!sendingInstance.AppInstances.TryGetValue(v_InstanceName, out browserObject))
            {
                throw new Exception("Session Instance was not found");
            }

            var browserInstance = (BrowserSession)browserObject;
            var element = browserInstance.FindElement(v_SeleniumSearchType, v_SeleniumSearchParameter);

            if (v_SeleniumElementAction == "Wait For Element To Exist")
            {
                int timeOut = ParseInt(GetParameterValue("Timeout (Seconds)"), 30);
                sendingInstance.ReportProgress("Waiting for element to exist...");

                try
                {
                    element.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Attached,
                        Timeout = timeOut * 1000
                    }).GetAwaiter().GetResult();
                }
                catch (PlaywrightException ex)
                {
                    throw new Exception("Element Not Found", ex);
                }

                return;
            }

            switch (v_SeleniumElementAction)
            {
                case "Invoke Click":
                    element.ClickAsync().GetAwaiter().GetResult();
                    break;

                case "Left Click":
                case "Right Click":
                case "Middle Click":
                    var clickOptions = new LocatorClickOptions
                    {
                        Button = v_SeleniumElementAction == "Right Click" ? MouseButton.Right : v_SeleniumElementAction == "Middle Click" ? MouseButton.Middle : MouseButton.Left
                    };

                    int userXAdjust = ParseInt(GetParameterValue("X Adjustment"), 0);
                    int userYAdjust = ParseInt(GetParameterValue("Y Adjustment"), 0);
                    if (userXAdjust != 0 || userYAdjust != 0)
                    {
                        clickOptions.Position = new Position { X = userXAdjust, Y = userYAdjust };
                    }

                    element.ClickAsync(clickOptions).GetAwaiter().GetResult();
                    break;

                case "Set Text":
                    PressText(element, GetParameterValue("Text To Set"));
                    break;

                case "Get Text":
                case "Get Attribute":
                    string variableName = GetParameterValue("Variable Name");
                    string attributeName = GetParameterValue("Attribute Name");

                    string elementValue;
                    if (v_SeleniumElementAction == "Get Text")
                    {
                        elementValue = element.InnerTextAsync().GetAwaiter().GetResult();
                    }
                    else
                    {
                        elementValue = element.GetAttributeAsync(attributeName).GetAwaiter().GetResult();
                    }

                    elementValue.StoreInUserVariable(context, variableName);
                    break;

                default:
                    throw new Exception("Element Action was not found");
            }
        }

        private string GetParameterValue(string parameterName)
        {
            if (v_WebActionParameterTable == null)
            {
                return string.Empty;
            }

            return (from rw in v_WebActionParameterTable.AsEnumerable()
                    where rw.Field<string>("Parameter Name") == parameterName
                    select rw.Field<string>("Parameter Value")).FirstOrDefault();
        }

        private static int ParseInt(string value, int defaultValue)
        {
            int result;
            return int.TryParse(value, out result) ? result : defaultValue;
        }

        private static void PressText(ILocator element, string textToSet)
        {
            if (string.IsNullOrEmpty(textToSet))
            {
                return;
            }

            string[] potentialKeyPresses = textToSet.Split('{', '}');

            foreach (string chunkedString in potentialKeyPresses)
            {
                if (chunkedString == "")
                {
                    continue;
                }

                string keyPress = GetPlaywrightKey(chunkedString);

                if (keyPress != null)
                {
                    element.PressAsync(keyPress).GetAwaiter().GetResult();
                }
                else
                {
                    element.PressSequentiallyAsync(chunkedString).GetAwaiter().GetResult();
                }
            }
        }

        private static string GetPlaywrightKey(string keyName)
        {
            switch (keyName.ToUpperInvariant())
            {
                case "ENTER":
                case "RETURN":
                    return "Enter";
                case "TAB":
                    return "Tab";
                case "ESCAPE":
                case "ESC":
                    return "Escape";
                case "BACKSPACE":
                case "BACK_SPACE":
                    return "Backspace";
                case "DELETE":
                case "DEL":
                    return "Delete";
                case "SPACE":
                    return "Space";
                case "INSERT":
                    return "Insert";
                case "HOME":
                    return "Home";
                case "END":
                    return "End";
                case "PAGEUP":
                case "PAGE_UP":
                    return "PageUp";
                case "PAGEDOWN":
                case "PAGE_DOWN":
                    return "PageDown";
                case "UP":
                case "ARROWUP":
                    return "ArrowUp";
                case "DOWN":
                case "ARROWDOWN":
                    return "ArrowDown";
                case "LEFT":
                case "ARROWLEFT":
                    return "ArrowLeft";
                case "RIGHT":
                case "ARROWRIGHT":
                    return "ArrowRight";
                case "SHIFT":
                    return "Shift";
                case "CONTROL":
                case "CTRL":
                    return "Control";
                case "ALT":
                    return "Alt";
                case "META":
                case "COMMAND":
                case "WIN":
                case "WINDOWS":
                    return "Meta";
                default:
                    int functionKey;
                    if (keyName.Length >= 2 && keyName[0] == 'F' && int.TryParse(keyName.Substring(1), out functionKey) && functionKey >= 1 && functionKey <= 12)
                    {
                        return "F" + functionKey;
                    }

                    return null;
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [" + v_SeleniumSearchType + " and " + v_SeleniumElementAction + ", Instance Name: '" + v_InstanceName + "']";
        }
    }

    #endregion Web Browser

    #region Misc Commands

    [Serializable]
    [Attributes.ClassAttributes.Group("Misc Commands")]
    [Attributes.ClassAttributes.Description("This command pauses the script for a set amount of time in milliseconds.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Thread.Sleep' to achieve automation.")]
    public class PauseCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Amount of time to pause for (in milliseconds).")]
        public int v_PauseLength { get; set; }

        public PauseCommand()
        {
            this.CommandName = "PauseCommand";
            this.SelectionName = "Pause Script";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            // wait interruptibly so a cancelled run does not have to finish the whole pause
            if (context.CancellationToken.WaitHandle.WaitOne(v_PauseLength))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Wait for " + v_PauseLength + "ms]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Misc Commands")]
    [Attributes.ClassAttributes.Description("This command pauses the script for a set amount of time in milliseconds.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Thread.Sleep' to achieve automation.")]
    public class ErrorHandlingCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Action On Error")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Stop Processing")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Continue Processing")]
        public string v_ErrorHandlingAction { get; set; }

        public ErrorHandlingCommand()
        {
            this.CommandName = "ErrorHandlingCommand";
            this.SelectionName = "Error Handling";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            context.ErrorHandling = this;
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Action: " + v_ErrorHandlingAction + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Misc Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to add an in-line comment to the configuration.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command is for visual purposes only")]
    public class CommentCommand : ScriptCommand
    {
        public CommentCommand()
        {
            this.CommandName = "CommentCommand";
            this.SelectionName = "Add Code Comment";
            this.DisplayForeColor = System.Drawing.Color.ForestGreen;
            this.CommandEnabled = true;
        }

        public override string GetDisplayValue()
        {
            return "// Comment: " + this.v_Comment;
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Misc Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to show a MessageBox and supports variables.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'MessageBox' and invokes VariableCommand to find variable data.")]
    public class MessageBoxCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the message to be displayed.")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_Message { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Close After X (Seconds) - 0 to bypass")]
        public int v_AutoCloseAfter { get; set; }
        public MessageBoxCommand()
        {
            this.CommandName = "MessageBoxCommand";
            this.SelectionName = "Show Message";
            this.CommandEnabled = true;
            this.v_AutoCloseAfter = 0;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            string variableMessage = v_Message.ConvertToUserVariable(context);
            context.ShowMessage(variableMessage, "MessageBox Command", v_AutoCloseAfter);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Message: " + v_Message + "]";
        }
    }

    [Serializable]
    [Attributes.ClassAttributes.Group("Programs/Process Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to start a program or a process. You can use short names 'chrome.exe' or fully qualified names 'c:/some.exe'")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Process.Start'.")]
    public class StartProcessCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please enter the name or path to the program (ex. notepad, calc)")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowFileSelectionHelper)]
        public string v_ProgramName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please enter any arguments (if applicable)")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_ProgramArgs { get; set; }

        public StartProcessCommand()
        {
            this.CommandName = "StartProcessCommand";
            this.SelectionName = "Start Process";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            v_ProgramName = v_ProgramName.ConvertToUserVariable(context);
            v_ProgramArgs = v_ProgramArgs.ConvertToUserVariable(context);

            if (v_ProgramArgs == "")
            {
                System.Diagnostics.Process.Start(v_ProgramName);
            }
            else
            {
                System.Diagnostics.Process.Start(v_ProgramName, v_ProgramArgs);
            }

            System.Threading.Thread.Sleep(2000);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Process: " + v_ProgramName + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Programs/Process Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to stop a program or a process. You can use the name of the process 'chrome'. Alternatively, you may use the Close Window or Thick App Command instead.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Process.CloseMainWindow'.")]
    public class StopProcessCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Enter the process name to be stopped (calc, notepad)")]
        public string v_ProgramShortName { get; set; }

        public StopProcessCommand()
        {
            this.CommandName = "StopProgramCommand";
            this.SelectionName = "Stop Process";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            var processes = System.Diagnostics.Process.GetProcessesByName(v_ProgramShortName);

            foreach (var prc in processes)
                prc.CloseMainWindow();
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Process: " + v_ProgramShortName + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Programs/Process Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to run a script or program and wait for it to exit before proceeding.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Process.Start' and waits for the script/program to exit before proceeding.")]
    public class RunScriptCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Enter the path to the script")]
        public string v_ScriptPath { get; set; }

        public RunScriptCommand()
        {
            this.CommandName = "RunScriptCommand";
            this.SelectionName = "Run Script";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            {
                System.Diagnostics.Process scriptProc = new System.Diagnostics.Process();
                scriptProc.StartInfo.FileName = v_ScriptPath;
                scriptProc.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                scriptProc.Start();
                scriptProc.WaitForExit();

                scriptProc.Close();
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Script Path: " + v_ScriptPath + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Variable Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to modify variables.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements actions against VariableList from the scripting engine.")]
    public class VariableCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select a variable to modify")]
        public string v_userVariableName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please define the input to be set to above variable")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_Input { get; set; }
        public VariableCommand()
        {
            this.CommandName = "VariableCommand";
            this.SelectionName = "Set Variable";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            var sendingInstance = (IScriptExecutionContext)context;
            var requiredVariable = sendingInstance.Variables.Where(var => var.variableName == v_userVariableName).FirstOrDefault();

            if (requiredVariable != null)
            {
                requiredVariable.variableValue = v_Input.ConvertToUserVariable(context);
            }
            else
            {
                throw new Exception("Variable was not found. Enclose variables within brackets, ex. [vVariable]");
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Apply '" + v_Input + "' to Variable '" + v_userVariableName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Misc Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to send email using SMTP.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements the System.Net Namespace to achieve automation")]
    public class SMTPSendEmailCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Host Name")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_SMTPHost { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Port")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public int v_SMTPPort { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Username")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_SMTPUserName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Password")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_SMTPPassword { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("From Email")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_SMTPFromEmail { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("To Email")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_SMTPToEmail { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Subject")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_SMTPSubject { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Body")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_SMTPBody { get; set; }
        public SMTPSendEmailCommand()
        {
            this.CommandName = "SMTPCommand";
            this.SelectionName = "Send SMTP Email";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            string varSMTPHost = v_SMTPHost.ConvertToUserVariable(context);
            string varSMTPPort = v_SMTPPort.ToString().ConvertToUserVariable(context);
            string varSMTPUserName = v_SMTPUserName.ConvertToUserVariable(context);
            string varSMTPPassword = v_SMTPPassword.ConvertToUserVariable(context);

            string varSMTPFromEmail = v_SMTPFromEmail.ConvertToUserVariable(context);
            string varSMTPToEmail = v_SMTPToEmail.ConvertToUserVariable(context);
            string varSMTPSubject = v_SMTPSubject.ConvertToUserVariable(context);
            string varSMTPBody = v_SMTPBody.ConvertToUserVariable(context);

            var client = new SmtpClient(varSMTPHost, int.Parse(varSMTPPort));
            client.Credentials = new System.Net.NetworkCredential(varSMTPUserName, varSMTPPassword);
            client.EnableSsl = true;

            client.Send(varSMTPFromEmail, varSMTPToEmail, varSMTPSubject, varSMTPBody);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [To Address: '" + v_SMTPToEmail + "']";
        }
    }

    #endregion Misc Commands

    #region Loop Commands

    [Serializable]
    [Attributes.ClassAttributes.Group("Loop Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to repeat actions several times (loop).  Any 'Begin Loop' command must have a following 'End Loop' command.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command recursively calls the underlying 'BeginLoop' Command to achieve automation.")]
    public class BeginLoopCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select style of loop to perform")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Loop Number Of Times")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Loop Through List")]
        public string v_LoopType { get; set; }

        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Loop Parameter (Number or Variable List Name)")]
        public string v_LoopParameter { get; set; }

        public BeginLoopCommand()
        {
            this.CommandName = "BeginLoopCommand";
            this.SelectionName = "Begin Loop";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context, Core.Script.ScriptAction parentCommand)
        {
            Core.AutomationCommands.BeginLoopCommand loopCommand = (Core.AutomationCommands.BeginLoopCommand)parentCommand.ScriptCommand;

            int loopTimes;
            Script.ScriptVariable complexVarible = null;
            if (v_LoopType == "Loop Number Of Times")
            {
                loopTimes = int.Parse(loopCommand.v_LoopParameter);
            }
            else
            {
                complexVarible = context.Variables.Where(x => x.variableName == v_LoopParameter).FirstOrDefault();
                var listToLoop = (List<string>)complexVarible.variableValue;
                loopTimes = listToLoop.Count();
            }

            for (int i = 0; i < loopTimes; i++)
            {
                if (complexVarible != null)
                    complexVarible.currentPosition = i;

                context.ReportProgress(loopCommand.LineNumber, "Starting Loop Number " + (i + 1) + "/" + loopTimes + " From Line " + loopCommand.LineNumber);

                foreach (var cmd in parentCommand.AdditionalScriptCommands)
                {
                    if (context.IsCancellationPending)
                        return;
                    context.ExecuteCommand(cmd);
                }

                context.ReportProgress(loopCommand.LineNumber, "Finished Loop From Line " + loopCommand.LineNumber);
            }
        }

        public override string GetDisplayValue()
        {
            if (v_LoopType == "Loop Number Of Times")
            {
                return "Loop " + v_LoopParameter + " Times";
            }
            else
            {
                return "Loop List Variable '" + v_LoopParameter + "'";
            }
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Loop Commands")]
    [Attributes.ClassAttributes.Description("This command signifies the exit point of looped (repeated) actions.  Required for all loops.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command is used by the serializer to signify the end point of a loop.")]
    public class EndLoopCommand : ScriptCommand
    {
        public EndLoopCommand()
        {
            this.DefaultPause = 0;
            this.CommandName = "EndLoopCommand";
            this.SelectionName = "End Loop";
            this.CommandEnabled = true;
        }

        public override string GetDisplayValue()
        {
            return "End Loop";
        }
    }

    #endregion Loop Commands

    #region String Commands

    [Serializable]
    [Attributes.ClassAttributes.Group("String Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to trim a string")]
    [Attributes.ClassAttributes.ImplementationDescription("This command uses the String.Substring method to achieve automation.")]
    public class StringSubstringCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select a variable to modify")]
        public string v_userVariableName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Start from Position")]
        public int v_startIndex { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Optional - Length (-1 to keep remainder)")]
        public int v_stringLength { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select the variable to receive the changes")]
        public string v_applyToVariableName { get; set; }
        public StringSubstringCommand()
        {
            this.CommandName = "StringSubstringCommand";
            this.SelectionName = "Substring";
            this.CommandEnabled = true;
            v_stringLength = -1;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            v_userVariableName = v_userVariableName.ConvertToUserVariable(context);

            //apply substring
            if (v_stringLength >= 0)
            {
                v_userVariableName = v_userVariableName.Substring(v_startIndex, v_stringLength);
            }
            else
            {
                v_userVariableName = v_userVariableName.Substring(v_startIndex);
            }

            v_userVariableName.StoreInUserVariable(context, v_applyToVariableName);
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Apply Substring to '" + v_userVariableName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("String Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to split a string")]
    [Attributes.ClassAttributes.ImplementationDescription("This command uses the String.Split method to achieve automation.")]
    public class StringSplitCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select a variable to split")]
        public string v_userVariableName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Input Delimiter")]
        public string v_splitCharacter { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select the list variable which will contain the results")]
        public string v_applyConvertToUserVariableName { get; set; }
        public StringSplitCommand()
        {
            this.CommandName = "StringSplitCommand";
            this.SelectionName = "Split";
            this.v_applyConvertToUserVariableName = "default";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            var stringVariable = v_userVariableName.ConvertToUserVariable(context);

            List<string> splitString;
            if (v_splitCharacter == "[crLF]")
            {
                splitString = stringVariable.Split(new[] { Environment.NewLine }, StringSplitOptions.None).ToList();
            }
            else
            {
                splitString = stringVariable.Split(new string[] { v_splitCharacter }, StringSplitOptions.None).ToList();
            }

            var sendingInstance = (IScriptExecutionContext)context;

            //get complex variable from engine and assign
            var requiredComplexVariable = sendingInstance.Variables.Where(x => x.variableName == v_applyConvertToUserVariableName).FirstOrDefault();
            requiredComplexVariable.variableValue = splitString;
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Split '" + v_userVariableName + "' by '" + v_splitCharacter + "' and apply to '" + v_applyConvertToUserVariableName + "']";
        }
    }

    #endregion String Commands

    #region If Commands

    [Serializable]
    [Attributes.ClassAttributes.Group("If Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to evaluate a logical statement to determine if the statement is true.  Any 'BeginIf' command must have a following 'EndIf' command.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command evaluates supplied arguments and if evaluated to true runs sub elements")]
    public class BeginIfCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select type of If Command")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Value")]
        public string v_IfActionType { get; set; }

        [XmlElement]
        [Attributes.PropertyAttributes.PropertyDescription("Additional Parameters")]
        public DataTable v_IfActionParameterTable { get; set; }

        public BeginIfCommand()
        {
            this.CommandName = "BeginIfCommand";
            this.SelectionName = "Begin If";
            this.CommandEnabled = true;

            //define parameter table
            this.v_IfActionParameterTable = new System.Data.DataTable();
            this.v_IfActionParameterTable.TableName = DateTime.Now.ToString("IfActionParamTable" + DateTime.Now.ToString("MMddyy.hhmmss"));
            this.v_IfActionParameterTable.Columns.Add("Parameter Name");
            this.v_IfActionParameterTable.Columns.Add("Parameter Value");
        }

        public override void RunCommand(IScriptExecutionContext context, Core.Script.ScriptAction parentCommand)
        {
            if (v_IfActionType == "Value")
            {
                string value1 = ((from rw in v_IfActionParameterTable.AsEnumerable()
                                  where rw.Field<string>("Parameter Name") == "Value1"
                                  select rw.Field<string>("Parameter Value")).FirstOrDefault());
                string operand = ((from rw in v_IfActionParameterTable.AsEnumerable()
                                   where rw.Field<string>("Parameter Name") == "Operand"
                                   select rw.Field<string>("Parameter Value")).FirstOrDefault());
                string value2 = ((from rw in v_IfActionParameterTable.AsEnumerable()
                                  where rw.Field<string>("Parameter Name") == "Value2"
                                  select rw.Field<string>("Parameter Value")).FirstOrDefault());

                value1 = value1.ConvertToUserVariable(context);
                value2 = value2.ConvertToUserVariable(context);

                bool ifResult = false;

                decimal cdecValue1, cdecValue2;

                switch (operand)
                {
                    case "is equal to":
                        ifResult = (value1 == value2);
                        break;

                    case "is not equal to":
                        ifResult = (value1 != value2);
                        break;

                    case "is greater than":
                        cdecValue1 = Convert.ToDecimal(value1);
                        cdecValue2 = Convert.ToDecimal(value2);
                        ifResult = (cdecValue1 > cdecValue2);
                        break;

                    case "is greater than or equal to":
                        cdecValue1 = Convert.ToDecimal(value1);
                        cdecValue2 = Convert.ToDecimal(value2);
                        ifResult = (cdecValue1 >= cdecValue2);
                        break;

                    case "is less than":
                        cdecValue1 = Convert.ToDecimal(value1);
                        cdecValue2 = Convert.ToDecimal(value2);
                        ifResult = (cdecValue1 < cdecValue2);
                        break;

                    case "is less than or equal to":
                        cdecValue1 = Convert.ToDecimal(value1);
                        cdecValue2 = Convert.ToDecimal(value2);
                        ifResult = (cdecValue1 <= cdecValue2);
                        break;
                }

                int startIndex, endIndex, elseIndex;
                if (parentCommand.AdditionalScriptCommands.Any(item => item.ScriptCommand is Core.AutomationCommands.ElseCommand))
                {
                    elseIndex = parentCommand.AdditionalScriptCommands.FindIndex(a => a.ScriptCommand is Core.AutomationCommands.ElseCommand);

                    if (ifResult)
                    {
                        startIndex = 0;
                        endIndex = elseIndex;
                    }
                    else
                    {
                        startIndex = elseIndex + 1;
                        endIndex = parentCommand.AdditionalScriptCommands.Count;
                    }
                }
                else if (ifResult)
                {
                    startIndex = 0;
                    endIndex = parentCommand.AdditionalScriptCommands.Count;
                }
                else
                {
                    return;
                }

                for (int i = startIndex; i < endIndex; i++)
                {
                    if (context.IsCancellationPending)
                        return;
                    context.ExecuteCommand(parentCommand.AdditionalScriptCommands[i]);
                }
            }
        }

        public override string GetDisplayValue()
        {
            switch (v_IfActionType)
            {
                case "Value":

                    string value1 = ((from rw in v_IfActionParameterTable.AsEnumerable()
                                      where rw.Field<string>("Parameter Name") == "Value1"
                                      select rw.Field<string>("Parameter Value")).FirstOrDefault());
                    string operand = ((from rw in v_IfActionParameterTable.AsEnumerable()
                                       where rw.Field<string>("Parameter Name") == "Operand"
                                       select rw.Field<string>("Parameter Value")).FirstOrDefault());
                    string value2 = ((from rw in v_IfActionParameterTable.AsEnumerable()
                                      where rw.Field<string>("Parameter Name") == "Value2"
                                      select rw.Field<string>("Parameter Value")).FirstOrDefault());

                    return "If (" + value1 + " " + operand + " " + value2 + ")";

                default:
                    break;
            }

            return "";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("If Commands")]
    [Attributes.ClassAttributes.Description("This command signifies the exit point of If actions.  Required for all Begin Ifs.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command is used by the serializer to signify the end point of an if.")]
    public class EndIfCommand : ScriptCommand
    {
        public EndIfCommand()
        {
            this.DefaultPause = 0;
            this.CommandName = "EndIfCommand";
            this.SelectionName = "End If";
            this.CommandEnabled = true;
        }

        public override string GetDisplayValue()
        {
            return "End If";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("If Commands")]
    [Attributes.ClassAttributes.Description("TBD")]
    [Attributes.ClassAttributes.ImplementationDescription("TBD")]
    public class ElseCommand : ScriptCommand
    {
        public ElseCommand()
        {
            this.DefaultPause = 0;
            this.CommandName = "ElseCommand";
            this.SelectionName = "Else";
            this.CommandEnabled = true;
        }

        public override string GetDisplayValue()
        {
            return "Else";
        }
    }

    #endregion If Commands

    #region HTTP Commands
    [Serializable]
    [Attributes.ClassAttributes.Group("WebAPI Commands")]
    [Attributes.ClassAttributes.Description("This command downloads the HTML source of a web page for parsing")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements System.Web to achieve automation")]
    public class HTTPRequestCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the URL")]
        public string v_WebRequestURL { get; set; }

        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Apply Result To Variable")]
        public string v_userVariableName { get; set; }

        public HTTPRequestCommand()
        {
            this.CommandName = "HTTPRequestCommand";
            this.SelectionName = "HTTP Request";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/535.2 (KHTML, like Gecko) Chrome/15.0.874.121 Safari/535.2");
                string strResponse = client.GetStringAsync(v_WebRequestURL).GetAwaiter().GetResult();
                strResponse.StoreInUserVariable(context, v_userVariableName);
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target URL: '" + v_WebRequestURL + "' and apply result to '" + v_userVariableName + "']";
        }

    }

        [Serializable]
        [Attributes.ClassAttributes.Group("WebAPI Commands")]
        [Attributes.ClassAttributes.Description("This command processes an HTML source object")]
        [Attributes.ClassAttributes.ImplementationDescription("TBD")]
        public class HTTPQueryResultCommand : ScriptCommand
        {
            [XmlAttribute]
            [Attributes.PropertyAttributes.PropertyDescription("Select variable containing HTML")]
            public string v_userVariableName { get; set; }

            [XmlAttribute]
            [Attributes.PropertyAttributes.PropertyDescription("XPath Query")]
            public string v_xPathQuery { get; set; }

            [XmlAttribute]
            [Attributes.PropertyAttributes.PropertyDescription("Apply Query Result To Variable")]
            public string v_applyToVariableName { get; set; }

            public HTTPQueryResultCommand()
            {
                this.CommandName = "HTTPRequestQueryCommand";
                this.SelectionName = "HTTP Result Query";
                this.CommandEnabled = true;
            }

            public override void RunCommand(IScriptExecutionContext context)
            {
            HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(v_userVariableName.ConvertToUserVariable(context));

            var div = doc.DocumentNode.SelectSingleNode(v_xPathQuery);
            if (div == null)
            {
                throw new Exception("XPath query matched no node: " + v_xPathQuery);
            }

            string divString = div.InnerText;

            divString.StoreInUserVariable(context, v_applyToVariableName);

        }

            public override string GetDisplayValue()
            {
                return base.GetDisplayValue() + " [Query Variable '" + v_userVariableName + "' and apply result to '" + v_applyToVariableName + "']";
            }
        }

        #endregion
    }

