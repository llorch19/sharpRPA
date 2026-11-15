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
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using sharpRPA.Core.Execution;

namespace sharpRPA.UI.Forms
{
    public partial class frmScriptEngine : UIForm
    {
        #region Form Events

        public string filePath { get; set; }
        public string xmlInfo { get; set; }
        private System.Diagnostics.Stopwatch sw;
        public frmScriptBuilder callBackForm { get; set; }
        private bool isPaused { get; set; }
        private CancellationTokenSource runCancellation;
        private ScriptRunner runner;

        public frmScriptEngine(string pathToFile, frmScriptBuilder builderForm)
        {
            filePath = pathToFile;
            callBackForm = builderForm;
            InitializeComponent();
        }

        private void frmProcessingStatus_Load(object sender, EventArgs e)
        {
            // callBackForm.websocket.Send("Running");
            this.BringToFront();
            MoveFormToBottomRight(this);
            runCancellation = new CancellationTokenSource();
            bgwRunScript.RunWorkerAsync();
        }

        public bool ShowMessage(string message, string title, UI.Forms.Supplemental.frmDialog.DialogType dialogType, int closeAfter)
        {
            var confirmationForm = new UI.Forms.Supplemental.frmDialog(message, title, dialogType, closeAfter);
            return confirmationForm.ShowDialog() == DialogResult.OK;
        }

        #endregion Form Events

        #region BackgroundWorker

        private void bgwRunScript_DoWork(object sender, DoWorkEventArgs e)
        {
            sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            bgwRunScript.ReportProgress(0, new object[] { 0, "Bot Engine Started: " + DateTime.Now.ToString() });

            Core.Script.Script automationScript;

            //parse file or streamed XML from sharpRPAServer
            if (filePath != "")
            {
                automationScript = Core.Script.Script.DeserializeFile(filePath);
            }
            else
            {
                automationScript = Core.Script.Script.DeserializeXML(xmlInfo);
            }

            runner = new ScriptRunner
            {
                CancellationToken = runCancellation.Token
            };
            runner.ProgressChanged += (lineNumber, message) =>
                bgwRunScript.ReportProgress(0, new object[] { lineNumber, message });
            runner.MessageHandler = (message, title, closeAfter) =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return true;
                }

                return (bool)Invoke(new Func<bool>(() =>
                    ShowMessage(message, title, UI.Forms.Supplemental.frmDialog.DialogType.OkOnly, closeAfter)));
            };
            runner.PauseHandler = () =>
            {
                while (isPaused && !runner.CancellationToken.IsCancellationRequested)
                {
                    System.Threading.Thread.Sleep(200);
                }
            };

            try
            {
                runner.Run(automationScript);
            }
            catch (OperationCanceledException)
            {
                e.Cancel = true;
            }
        }

        private void bgwRunScript_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            object[] progressUpdate = (object[])e.UserState;

            if ((callBackForm != null) && (progressUpdate[0] != null))
                callBackForm.DebugLine = (int)progressUpdate[0];

            lstSteppingCommands.Items.Add((string)progressUpdate[1] + "..");
            lstSteppingCommands.SelectedIndex = lstSteppingCommands.Items.Count - 1;
        }

        private void bgwRunScript_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            sw.Stop();

            //dispose of any unnecessary application instances
            if (runner != null)
            {
                runner.CleanUp();
            }

            //reset debug line
            if (callBackForm != null)
                callBackForm.DebugLine = 0;
            uiBtnPause.Visible = false;
            uiBtnCancel.DisplayText = "Close";
            uiBtnCancel.Visible = true;

            if ((e.Error == null) && (!e.Cancelled))
            {
                lblMainLogo.Text = "debug info (success)";
                lstSteppingCommands.Items.Add("Bot Finished Task Successfully");
                if (callBackForm != null)
                    callBackForm.Notify("Script Execution Completed Successfully!");
                tmrNotify.Enabled = true;
            }
            else if ((e.Error == null) && (e.Cancelled))
            {
                lblMainLogo.Text = "debug info (cancelled)";
                lstSteppingCommands.Items.Add("Operation Cancelled by User");
                if (callBackForm != null)
                    callBackForm.Notify("Script Execution Cancelled!");
            }
            else
            {
                lblMainLogo.Text = "debug info (error)";
                lstSteppingCommands.Items.Add("Bot Had A Problem: " + e.Error.Message);
                if (callBackForm != null)
                    callBackForm.Notify("Script Execution Encountered an Error!");
            }

            lstSteppingCommands.Items.Add("Bot Engine Finished: " + DateTime.Now.ToString());
            lstSteppingCommands.Items.Add("Total Run Time: " + sw.Elapsed.ToString());
            lstSteppingCommands.SelectedIndex = lstSteppingCommands.Items.Count - 1;

            //callBackForm.websocket.Send("Available");
        }

        #endregion BackgroundWorker

        #region UI Elements

        private void lblClose_MouseEnter(object sender, EventArgs e)
        {
            Cursor = Cursors.Hand;
        }

        private void lblClose_MouseLeave(object sender, EventArgs e)
        {
            Cursor = Cursors.Arrow;
        }

        private void autoCloseTimer_Tick(object sender, EventArgs e)
        {
            this.Close();
        }

        private void uiBtnCancel_Click(object sender, EventArgs e)
        {
            if (uiBtnCancel.DisplayText == "Close")
            {
                this.Close();
                return;
            }

            uiBtnPause.Visible = false;
            uiBtnCancel.Visible = false;
            isPaused = false;
            lstSteppingCommands.Items.Add("[User Requested Cancellation]");
            lstSteppingCommands.SelectedIndex = lstSteppingCommands.Items.Count - 1;
            lblMainLogo.Text = "debug info (cancelling)";
            runCancellation?.Cancel();
            bgwRunScript.CancelAsync();
        }

        private void uiBtnPause_Click(object sender, EventArgs e)
        {
            isPaused = !isPaused;

            if (isPaused)
            {
                lstSteppingCommands.Items.Add("[User Requested Pause]");
                uiBtnPause.Image = Properties.Resources.restart;
                uiBtnPause.DisplayText = "Resume";
            }
            else
            {
                lstSteppingCommands.Items.Add("[User Requested Resume]");
                uiBtnPause.Image = Properties.Resources.pause;
                uiBtnPause.DisplayText = "Pause";
            }

            lstSteppingCommands.SelectedIndex = lstSteppingCommands.Items.Count - 1;
        }

        #endregion UI Elements

        private void frmScriptEngine_FormClosing(object sender, FormClosingEventArgs e)
        {
            runCancellation?.Cancel();

            //dispose of any unnecessary application instances
            if (runner != null)
            {
                runner.CleanUp();
            }
        }
    }
}