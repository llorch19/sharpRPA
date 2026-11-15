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
using System.Windows.Automation;
using sharpRPA.Core;
using sharpRPA.Core.Execution;
using System.IO;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace sharpRPA.Core.AutomationCommands
{
    #region Windows Commands

    [Serializable]
    [Attributes.ClassAttributes.Group("Window Commands")]
    [Attributes.ClassAttributes.Description("This command activates a window and brings it to the front.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'FindWindowNative', 'SetForegroundWindow', 'ShowWindow' from user32.dll to achieve automation.")]
    public class ActivateWindowCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Select or Type a window Name")]
        public string v_WindowName { get; set; }

        public ActivateWindowCommand()
        {
            this.CommandName = "ActivateWindowCommand";
            this.SelectionName = "Activate Window";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            IntPtr hWnd = User32Functions.FindWindow(v_WindowName);
            if (hWnd != IntPtr.Zero)
            {
                User32Functions.SetWindowState(hWnd, User32Functions.WindowState.SW_SHOWNORMAL);
                User32Functions.SetForegroundWindow(hWnd);
            }
            else
            {
                throw new Exception("Window not found");
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Window: " + v_WindowName + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Window Commands")]
    [Attributes.ClassAttributes.Description("This command moves a window to a specified location on screen.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'FindWindowNative', 'SetWindowPos' from user32.dll to achieve automation.")]
    public class MoveWindowCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Select or Type a window Name")]
        public string v_WindowName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select the X position to move the window to.")]
        public int v_XWindowPosition { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select the Y position to move the window to.")]
        public int v_YWindowPosition { get; set; }

        public MoveWindowCommand()
        {
            this.CommandName = "MoveWindowCommand";
            this.SelectionName = "Move Window";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            IntPtr hWnd = User32Functions.FindWindow(v_WindowName);

            if (hWnd != IntPtr.Zero)
            {
                User32Functions.SetWindowPosition(hWnd, v_XWindowPosition, v_YWindowPosition);
            }
            else
            {
                throw new Exception("Window not found");
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Window: " + v_WindowName + ", Target Coordinates (" + v_XWindowPosition + "," + v_YWindowPosition + ")]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Window Commands")]
    [Attributes.ClassAttributes.Description("This command resizes a window to a specified size.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'FindWindowNative', 'SetWindowPos' from user32.dll to achieve automation.")]
    public class ResizeWindowCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Select or Type a window Name")]
        public string v_WindowName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the new window width")]
        public int v_XWindowSize { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the new window height")]
        public int v_YWindowSize { get; set; }

        public ResizeWindowCommand()
        {
            this.CommandName = "ResizeWindowCommand";
            this.SelectionName = "Resize Window";

            //not working
            //this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            IntPtr hWnd = User32Functions.FindWindow(v_WindowName);

            if (hWnd != IntPtr.Zero)
            {
                User32Functions.SetWindowSize(hWnd, v_XWindowSize, v_YWindowSize);
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Window: " + v_WindowName + ", Target Size (" + v_XWindowSize + "," + v_YWindowSize + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Window Commands")]
    [Attributes.ClassAttributes.Description("This command closes an open window.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'FindWindowNative', 'SendMessage' from user32.dll to achieve automation.")]
    public class CloseWindowCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Select or Type a window Name")]
        public string v_WindowName { get; set; }

        public CloseWindowCommand()
        {
            this.CommandName = "CloseWindowCommand";
            this.SelectionName = "Close Window";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            IntPtr hWnd = User32Functions.FindWindow(v_WindowName);

            if (hWnd != IntPtr.Zero)
            {
                User32Functions.CloseWindow(hWnd);
            }
            else
            {
                throw new Exception("Window not found");
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Window: " + v_WindowName + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Window Commands")]
    [Attributes.ClassAttributes.Description("This command sets a target windows state (minimize, maximize, restore)")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'FindWindowNative', 'ShowWindow' from user32.dll to achieve automation.")]
    public class SetWindowStateCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Select or Type a window Name")]
        public string v_WindowName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Select a Window State")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Maximize")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Minimize")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Restore")]
        public string v_WindowState { get; set; }

        public SetWindowStateCommand()
        {
            this.CommandName = "SetWindowStateCommand";
            this.SelectionName = "Set Window State";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            IntPtr hWnd = User32Functions.FindWindow(v_WindowName);

            if (hWnd != IntPtr.Zero) //If found
            {
                User32Functions.WindowState WINDOW_STATE = User32Functions.WindowState.SW_SHOWNORMAL;
                switch (v_WindowState)
                {
                    case "Maximize":
                        WINDOW_STATE = User32Functions.WindowState.SW_MAXIMIZE;
                        break;

                    case "Minimize":
                        WINDOW_STATE = User32Functions.WindowState.SW_MINIMIZE;
                        break;

                    case "Restore":
                        WINDOW_STATE = User32Functions.WindowState.SW_RESTORE;
                        break;

                    default:
                        break;
                }

                User32Functions.SetWindowState(hWnd, WINDOW_STATE);
            }
            else
            {
                throw new Exception("Window not found");
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Window: " + v_WindowName + ", Window State: " + v_WindowState + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Window Commands")]
    [Attributes.ClassAttributes.Description("This command waits for a window to exist")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'FindWindowNative', 'ShowWindow' from user32.dll to achieve automation.")]
    public class WaitForWindowCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Select or Type a window Name")]
        public string v_WindowName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Seconds To Wait")]
        public string v_LengthToWait { get; set; }

        public WaitForWindowCommand()
        {
            this.CommandName = "WaitForWindowCommand";
            this.SelectionName = "Wait For Window To Exist";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {

            var waitUntil = int.Parse(v_LengthToWait.ConvertToUserVariable(context));
            var endDateTime = DateTime.Now.AddSeconds(waitUntil);

            IntPtr hWnd = IntPtr.Zero;

            while (DateTime.Now < endDateTime)
            {
                hWnd = User32Functions.FindWindow(v_WindowName);

                if (hWnd != IntPtr.Zero) //If found
                    break;

                System.Threading.Thread.Sleep(1000);

            }

            if (hWnd == IntPtr.Zero)
            {
                throw new Exception("Window was not found in the allowed time!");
            }




        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Window: '" + v_WindowName + "', Wait Up To " + v_LengthToWait + " seconds]";
        }

    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Clipboard Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to get text from the clipboard.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements actions against the VariableList from the scripting engine using System.Windows.Forms.Clipboard.")]
    public class ClipboardGetTextCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select a variable to set clipboard contents")]
        public string v_userVariableName { get; set; }

        public ClipboardGetTextCommand()
        {
            this.CommandName = "ClipboardCommand";
            this.SelectionName = "Get Text";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            User32Functions.GetClipboardText().StoreInUserVariable(context, v_userVariableName);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Get Text From Clipboard and Apply to Variable: " + v_userVariableName + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Input Commands")]
    [Attributes.ClassAttributes.Description("Use this command to send key strokes to the current or a targeted window.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Windows.Forms.SendKeys' method to achieve automation.")]
    public class SendKeysCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the Window name")]
        public string v_WindowName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter text to send")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_TextToSend { get; set; }

        public SendKeysCommand()
        {
            this.CommandName = "SendKeysCommand";
            this.SelectionName = "Send Keystrokes";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            if (v_WindowName != "Current Window")
            {
                ActivateWindowCommand activateWindow = new ActivateWindowCommand();
                activateWindow.v_WindowName = v_WindowName;
                activateWindow.RunCommand(context);
            }

            v_TextToSend = v_TextToSend.ConvertToUserVariable(context);
            System.Windows.Forms.SendKeys.SendWait(v_TextToSend);

            System.Threading.Thread.Sleep(500);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Send '" + v_TextToSend + "' to '" + v_WindowName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Input Commands")]
    [Attributes.ClassAttributes.Description("Use this command to simulate mouse movement and click the mouse on coordinates.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'SetCursorPos' function from user32.dll to achieve automation.")]
    public class SendMouseMoveCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please enter the X position to move the mouse to")]
        public int v_XMousePosition { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please enter the Y position to move the mouse to")]
        public int v_YMousePosition { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please indicate mouse click type if required")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("None")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Left Click")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Middle Click")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Right Click")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Left Down")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Middle Down")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Right Down")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Left Up")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Middle Up")]
        [Attributes.PropertyAttributes.PropertyUISelectionOption("Right Up")]
        public string v_MouseClick { get; set; }

        public SendMouseMoveCommand()
        {
            this.CommandName = "SendMouseMoveCommand";
            this.SelectionName = "Send Mouse Move";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            User32Functions.SetCursorPosition(v_XMousePosition, v_YMousePosition);
            User32Functions.SendMouseClick(v_MouseClick, v_XMousePosition, v_YMousePosition);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Coordinates (" + v_XMousePosition + "," + v_YMousePosition + ") Click: " + v_MouseClick + "]";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Input Commands")]
    [Attributes.ClassAttributes.Description("This command clicks an item in a Thick Application window.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Windows UI Automation' to find elements and invokes a SendMouseMove Command to click and achieve automation")]
    public class ThickAppClickItemCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select the Window to Automate")]
        public string v_AutomationWindowName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please select the Appropriate Item")]
        public string v_AutomationHandleName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please indicate mouse click type if required")]
        public string v_MouseClick { get; set; }

        public ThickAppClickItemCommand()
        {
            this.CommandName = "ThickAppClickItemCommand";
            this.SelectionName = "Click UI Item";
            this.CommandEnabled = true;
            this.DefaultPause = 3000;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            var searchItem = AutomationElement.RootElement.FindFirst
            (TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty,
            v_AutomationWindowName));

            if (searchItem == null)
            {
                throw new Exception("Window not found");
            }

            var requiredItem = searchItem.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, v_AutomationHandleName));

            var newActivateWindow = new ActivateWindowCommand();
            newActivateWindow.v_WindowName = v_AutomationWindowName;
            newActivateWindow.RunCommand(context);

            //get newpoint for now
            var newPoint = requiredItem.GetClickablePoint();

            //send mousemove command
            var newMouseMove = new SendMouseMoveCommand();
            newMouseMove.v_XMousePosition = (int)newPoint.X;
            newMouseMove.v_YMousePosition = (int)newPoint.Y;
            newMouseMove.v_MouseClick = v_MouseClick;
            newMouseMove.RunCommand(context);
        }

        public List<string> FindHandleObjects(string windowTitle)
        {
            var automationElement = AutomationElement.RootElement.FindFirst
    (TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty,
    windowTitle));

            var searchItems = automationElement.FindAll(TreeScope.Descendants, PropertyCondition.TrueCondition);

            List<String> handleList = new List<String>();
            foreach (AutomationElement item in searchItems)
            {
                if (item.Current.Name.Trim() != string.Empty)
                    handleList.Add(item.Current.Name);
            }
            // handleList = handleList.OrderBy(x => x).ToList();

            return handleList;
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Perform " + v_MouseClick + " on '" + v_AutomationHandleName + "' in Window '" + v_AutomationWindowName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Input Commands")]
    [Attributes.ClassAttributes.Description("This command gets text from a Thick Application window and assigns it to a variable.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Windows UI Automation' to find elements and invokes a Variable Command to assign data and achieve automation")]
    public class ThickAppGetTextCommand : ScriptCommand
    {
        [Attributes.PropertyAttributes.PropertyDescription("Please select the Window to Automate")]
        public string v_AutomationWindowName { get; set; }
        [Attributes.PropertyAttributes.PropertyDescription("Please select the Appropriate Item")]
        public string v_AutomationHandleDisplayName { get; set; }
        [Attributes.PropertyAttributes.PropertyDescription("Automation ID of the Item")]
        public string v_AutomationID { get; set; }
        [Attributes.PropertyAttributes.PropertyDescription("Assign to Variable")]
        public string v_userVariableName { get; set; }

        public ThickAppGetTextCommand()
        {
            this.CommandName = "ThickAppGetTextCommand";
            this.SelectionName = "Get UI Item";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            var searchItem = AutomationElement.RootElement.FindFirst
            (TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty,
            v_AutomationWindowName));

            if (searchItem == null)
            {
                throw new Exception("Window not found");
            }

            var requiredItem = searchItem.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, v_AutomationID));

            var newVariableCommand = new Core.AutomationCommands.VariableCommand();
            newVariableCommand.v_userVariableName = v_userVariableName;
            newVariableCommand.v_Input = requiredItem.Current.Name;
            newVariableCommand.RunCommand(context);
        }

        public string FindHandleID(string windowTitle, string nameProperty)
        {
            var automationElement = AutomationElement.RootElement.FindFirst
    (TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty,
    windowTitle));

            var requiredItem = automationElement.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, nameProperty));

            return requiredItem.Current.AutomationId;
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Set Variable [" + v_userVariableName + "] From ID " + v_AutomationID + " (" + v_AutomationHandleDisplayName + ") in Window '" + v_AutomationWindowName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to open the Excel Application.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Excel Interop to achieve automation.")]
    public class ExcelCreateApplicationCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }

        public ExcelCreateApplicationCommand()
        {
            this.CommandName = "ExcelOpenApplicationCommand";
            this.SelectionName = "Create Excel Application";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            var sendingInstance = (IScriptExecutionContext)context;
            var newExcelSession = new Microsoft.Office.Interop.Excel.Application();
            newExcelSession.Visible = true;
            sendingInstance.AppInstances.Add(v_InstanceName, newExcelSession);
            sendingInstance.RegisterCleanup(() =>
            {
                try
                {
                    newExcelSession.Quit();
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(newExcelSession);
                }
                catch (Exception)
                {
                }
            });
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to open an existing Excel Workbook.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Excel Interop to achieve automation.")]
    public class ExcelOpenWorkbookCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please indicate the workbook file path")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowFileSelectionHelper)]
        public string v_FilePath { get; set; }
        public ExcelOpenWorkbookCommand()
        {
            this.CommandName = "ExcelOpenWorkbookCommand";
            this.SelectionName = "Open Workbook";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            object excelObject;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out excelObject))
            {
                Microsoft.Office.Interop.Excel.Application excelInstance = (Microsoft.Office.Interop.Excel.Application)excelObject;
                excelInstance.Workbooks.Open(v_FilePath);
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Open from '" + v_FilePath + "', Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to add a new Excel Workbook.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Excel Interop to achieve automation.")]
    public class ExcelAddWorkbookCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }

        public ExcelAddWorkbookCommand()
        {
            this.CommandName = "ExcelAddWorkbookCommand";
            this.SelectionName = "Add Workbook";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            object excelObject;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out excelObject))
            {
                Microsoft.Office.Interop.Excel.Application excelInstance = (Microsoft.Office.Interop.Excel.Application)excelObject;
                excelInstance.Workbooks.Add();
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to move to a specific cell.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Excel Interop to achieve automation.")]
    public class ExcelGoToCellCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the Cell Location (ex. A1 or B2)")]
        public string v_CellLocation { get; set; }
        public ExcelGoToCellCommand()
        {
            this.CommandName = "ExcelGoToCellCommand";
            this.SelectionName = "Go To Cell";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            object excelObject;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out excelObject))
            {
                Microsoft.Office.Interop.Excel.Application excelInstance = (Microsoft.Office.Interop.Excel.Application)excelObject;
                Microsoft.Office.Interop.Excel.Worksheet excelSheet = (Microsoft.Office.Interop.Excel.Worksheet)excelInstance.ActiveSheet;
                excelSheet.Range[v_CellLocation].Select();
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Go To: '" + v_CellLocation + "', Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to set the value of a specific cell.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Excel Interop to achieve automation.")]
    public class ExcelSetCellCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter text to set")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_TextToSet { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the Cell Location (ex. A1 or B2)")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_ExcelCellAddress { get; set; }
        public ExcelSetCellCommand()
        {
            this.CommandName = "ExcelSetCellCommand";
            this.SelectionName = "Set Cell";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            object excelObject;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out excelObject))
            {
                v_ExcelCellAddress = v_ExcelCellAddress.ConvertToUserVariable(context);
                v_TextToSet = v_TextToSet.ConvertToUserVariable(context);

                Microsoft.Office.Interop.Excel.Application excelInstance = (Microsoft.Office.Interop.Excel.Application)excelObject;
                Microsoft.Office.Interop.Excel.Worksheet excelSheet = (Microsoft.Office.Interop.Excel.Worksheet)excelInstance.ActiveSheet;
                excelSheet.Range[v_ExcelCellAddress].Value = v_TextToSet;
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Set Cell '" + v_ExcelCellAddress + "' to '" + v_TextToSet + "', Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command gets text from a specified Excel Cell and assigns it to a variable.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements 'Excel Interop' to achieve automation.")]
    public class ExcelGetCellCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the Cell Location (ex. A1 or B2)")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        public string v_ExcelCellAddress { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Assign to Variable")]
        public string v_userVariableName { get; set; }

        public ExcelGetCellCommand()
        {
            this.CommandName = "ExcelGetCellCommand";
            this.SelectionName = "Get Cell";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            object excelObject;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out excelObject))
            {
                v_ExcelCellAddress = v_ExcelCellAddress.ConvertToUserVariable(context);

                Microsoft.Office.Interop.Excel.Application excelInstance = (Microsoft.Office.Interop.Excel.Application)excelObject;
                Microsoft.Office.Interop.Excel.Worksheet excelSheet = (Microsoft.Office.Interop.Excel.Worksheet)excelInstance.ActiveSheet;
                var cellValue = (string)excelSheet.Range[v_ExcelCellAddress].Value;
                cellValue.StoreInUserVariable(context, v_userVariableName);
            }
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Get Value From '" + v_ExcelCellAddress + "' and apply to variable '" + v_userVariableName + "', Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to run a macro in an Excel Workbook.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Excel Interop to achieve automation.")]
    public class ExcelRunMacroCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the macro name")]
        public string v_MacroName { get; set; }
        public ExcelRunMacroCommand()
        {
            this.CommandName = "ExcelAddWorkbookCommand";
            this.SelectionName = "Run Macro";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            object excelObject;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out excelObject))
            {
                Microsoft.Office.Interop.Excel.Application excelInstance = (Microsoft.Office.Interop.Excel.Application)excelObject;
                excelInstance.Run(v_MacroName);
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Excel Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to close Excel.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements Excel Interop to achieve automation.")]
    public class ExcelCloseApplicationCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the instance name")]
        public string v_InstanceName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Indicate if the Workbook should be saved")]
        public bool v_ExcelSaveOnExit { get; set; }
        public ExcelCloseApplicationCommand()
        {
            this.CommandName = "ExcelCloseApplicationCommand";
            this.SelectionName = "Close Application";
            this.CommandEnabled = true;
        }
        public override void RunCommand(IScriptExecutionContext context)
        {
            object excelObject;
            var sendingInstance = (IScriptExecutionContext)context;
            if (sendingInstance.AppInstances.TryGetValue(v_InstanceName, out excelObject))
            {
                Microsoft.Office.Interop.Excel.Application excelInstance = (Microsoft.Office.Interop.Excel.Application)excelObject;
                excelInstance.ActiveWorkbook.Close(v_ExcelSaveOnExit);
                excelInstance.Quit();
            }
        }
        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Save On Close: " + v_ExcelSaveOnExit + ", Instance Name: '" + v_InstanceName + "']";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Image Commands")]
    [Attributes.ClassAttributes.Description("This command allows you to covert an image file into text for parsing.")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements the Windows.Media.Ocr engine to achieve automation.")]
    public class OCRCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Select Image to OCR")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowFileSelectionHelper)]
        public string v_FilePath { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Apply OCR Result To Variable")]
        public string v_userVariableName { get; set; }
        public OCRCommand()
        {
            this.DefaultPause = 0;
            this.CommandName = "OCRCommand";
            this.SelectionName = "Perform OCR";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            string filePath = v_FilePath.ConvertToUserVariable(context);
            string endResult = RunOcrAsync(filePath).GetAwaiter().GetResult();

            endResult.StoreInUserVariable(context, v_userVariableName);
        }

        private static async Task<string> RunOcrAsync(string filePath)
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(filePath));

            using (var stream = await file.OpenAsync(FileAccessMode.Read))
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);

                using (var bitmap = await decoder.GetSoftwareBitmapAsync())
                {
                    var ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages()
                        ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));

                    if (ocrEngine == null)
                    {
                        throw new Exception("No OCR language is available on this system.");
                    }

                    var result = await ocrEngine.RecognizeAsync(bitmap);
                    return string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
                }
            }
        }

        public override string GetDisplayValue()
        {
            return "OCR '" + v_FilePath + "' and apply result to '" + v_userVariableName + "'";
        }
    }
    [Serializable]
    [Attributes.ClassAttributes.Group("Image Commands")]
    [Attributes.ClassAttributes.Description("This command takes a screenshot and saves it to a location")]
    [Attributes.ClassAttributes.ImplementationDescription("This command implements User32 CaptureWindow to achieve automation")]
    public class ScreenshotCommand : ScriptCommand
    {
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please Enter the Window name")]
        public string v_ScreenshotWindowName { get; set; }
        [XmlAttribute]
        [Attributes.PropertyAttributes.PropertyDescription("Please indicate the path to save the image")]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)]
        [Attributes.PropertyAttributes.PropertyUIHelper(Attributes.PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowFileSelectionHelper)]
        public string v_FilePath { get; set; }
        public ScreenshotCommand()
        {
            this.CommandName = "ScreenshotCommand";
            this.SelectionName = "Take Screenshot";
            this.CommandEnabled = true;
        }

        public override void RunCommand(IScriptExecutionContext context)
        {
            var image = User32Functions.CaptureWindow(v_ScreenshotWindowName);
            string ConvertToUserVariabledString = v_FilePath.ConvertToUserVariable(context);
            image.Save(ConvertToUserVariabledString);
        }

        public override string GetDisplayValue()
        {
            return base.GetDisplayValue() + " [Target Window: '" + v_ScreenshotWindowName + "', File Path: '" + v_FilePath + "]";
        }
    }

    #endregion Windows Commands
}
