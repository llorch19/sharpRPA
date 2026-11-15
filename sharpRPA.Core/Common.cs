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
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using sharpRPA.Core.Execution;

namespace sharpRPA.Core
{
    public static class Common
    {
        private static readonly List<Assembly> commandAssemblies = new List<Assembly> { typeof(Common).Assembly };

        /// <summary>
        /// Registers an assembly that contains automation commands so they are discoverable by the UI.
        /// </summary>
        public static void RegisterCommandAssembly(Assembly assembly)
        {
            if ((assembly != null) && (!commandAssemblies.Contains(assembly)))
            {
                commandAssemblies.Add(assembly);
            }
        }

        /// <summary>
        /// Returns every assembly that has been registered as containing automation commands.
        /// </summary>
        public static List<Assembly> GetCommandAssemblies()
        {
            return commandAssemblies.ToList();
        }

        /// <summary>
        /// Returns every automation command type known to the application.
        /// </summary>
        public static List<Type> GetCommandTypes()
        {
            return commandAssemblies
                .SelectMany(assembly => assembly.GetTypes())
                .Where(t => t.Namespace == "sharpRPA.Core.AutomationCommands")
                .Where(t => t.Name != "ScriptCommand")
                .Where(t => t.IsAbstract == false)
                .Where(t => t.IsVisible)
                .Where(t => (t.BaseType != null) && (t.BaseType.Name == "ScriptCommand"))
                .ToList();
        }

        /// <summary>
        /// Creates a unique 'clone' of an item. Used to create unique clones of commands when changing/updating new parameters.
        /// </summary>
        public static T Clone<T>(T source)
        {
            if (Object.ReferenceEquals(source, null))
            {
                return default(T);
            }

            if (source is DataTable dataTable)
            {
                return (T)(object)dataTable.Copy();
            }

            var serializer = new XmlSerializer(source.GetType());
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, source);
                stream.Seek(0, SeekOrigin.Begin);
                return (T)serializer.Deserialize(stream);
            }
        }
        /// <summary>
        /// Returns a path to the underlying Script folder where script file objects are loaded and saved. Used when saved or loading files.
        /// </summary>
        public static string GetScriptFolderPath()
        {
            return GetAppFolderPath() + "My Scripts\\";
        }
        /// <summary>
        /// Returns a path to the storage path for sharpRPA objects. Used when accessing the base storage path.
        /// </summary>
        public static string GetAppFolderPath()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\sharpRPA\\";
        }
        /// <summary>
        /// Returns commands from the AutomationCommands.cs file grouped by Custom 'Group' attribute.
        /// </summary>
        public static List<IGrouping<Attribute, Type>> GetGroupedCommands()
        {
            var groupedCommands = GetCommandTypes()
                          .Where(t => CommandEnabled(t))
                          .GroupBy(t => t.GetCustomAttribute(typeof(Core.AutomationCommands.Attributes.ClassAttributes.Group)))
                          .ToList();

            return groupedCommands;
        }
        /// <summary>
        /// Returns boolean indicating if the current command is enabled for use in automation.
        /// </summary>
        private static bool CommandEnabled(Type cmd)
        {
            var scriptCommand = (Core.AutomationCommands.ScriptCommand)Activator.CreateInstance(cmd);
            return scriptCommand.CommandEnabled;
        }
        /// <summary>
        /// Returns a list of system-generated variables for use with automation.
        /// </summary>
        public static List<Core.Script.ScriptVariable> GenerateSystemVariables()
        {
            List<Core.Script.ScriptVariable> systemVariableList = new List<Core.Script.ScriptVariable>();
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "Folder.Desktop", variableValue = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "Folder.Documents", variableValue = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "Folder.AppData", variableValue = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "Folder.ScriptPath", variableValue = Core.Common.GetScriptFolderPath() });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "DateTime.Now", variableValue = DateTime.Now.ToString() });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "DateTime.Now.FileSafe", variableValue = DateTime.Now.ToString("MM-dd-yy hh.mm.ss") });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "PC.MachineName", variableValue = Environment.MachineName });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "PC.UserName", variableValue = Environment.UserName });
            systemVariableList.Add(new Core.Script.ScriptVariable { variableName = "PC.DomainName", variableValue = Environment.UserDomainName });
            return systemVariableList;
        }
    }

    public static class ExtensionMethods
    {
        /// <summary>
        /// Replaces variable placeholders ([variable]) with variable text.
        /// </summary>
        /// <param name="context">The script execution context which contains session variables.</param>
        public static string ConvertToUserVariable(this String str, IScriptExecutionContext context)
        {
            if (str == null)
                return string.Empty;

            var systemVariables = Core.Common.GenerateSystemVariables();

            var searchList = new List<Core.Script.ScriptVariable>();
            searchList.AddRange(context.Variables);
            searchList.AddRange(systemVariables);

            string[] potentialVariables = str.Split('[', ']');

            foreach (var potentialVariable in potentialVariables)
            {
                var varCheck = (from vars in searchList
                                where vars.variableName == potentialVariable
                                select vars).FirstOrDefault();

                if (varCheck != null)
                {
                    var searchVariable = "[" + potentialVariable + "]";

                    if (str.Contains(searchVariable))
                    {
                        str = str.Replace(searchVariable, (string)varCheck.GetDisplayValue());
                    }
                    else if (str.Contains(potentialVariable))
                    {
                        str = str.Replace(potentialVariable, (string)varCheck.GetDisplayValue());
                    }
                }
            }

            //test if math is required
            try
            {
                DataTable dt = new DataTable();
                var v = dt.Compute(str, "");
                return v.ToString();
            }
            catch (Exception)
            {
                return str;
            }
        }
        /// <summary>
        /// Stores value of the string to a user-defined variable.
        /// </summary>
        /// <param name="context">The script execution context which contains session variables.</param>
        /// <param name="targetVariable">the name of the user-defined variable to override with new value</param>
        public static void StoreInUserVariable(this String str, IScriptExecutionContext context, string targetVariable)
        {
            AutomationCommands.VariableCommand newVariableCommand = new AutomationCommands.VariableCommand();
            newVariableCommand.v_userVariableName = targetVariable;
            newVariableCommand.v_Input = str;
            newVariableCommand.RunCommand(context);
        }
    }

    [Serializable]
    public class ApplicationSettings
    {
        public ServerSettings ServerSettings { get; set; } = new ServerSettings();
        public void Save(ApplicationSettings appSettings)
        {
            var savePath = Core.Common.GetAppFolderPath() + "AppSettings.xml";
            var fileStream = System.IO.File.Create(savePath);

            //output to xml file
            XmlSerializer serializer = new XmlSerializer(typeof(ApplicationSettings));
            serializer.Serialize(fileStream, appSettings);
            fileStream.Close();
        }
        public ApplicationSettings GetOrCreateApplicationSettings()
        {
            var savePath = Core.Common.GetAppFolderPath() + "AppSettings.xml";

            ApplicationSettings appSettings;
            if (System.IO.File.Exists(savePath))
            {
                //open file and return it or return new settings on error
                var fileStream = System.IO.File.Open(savePath, FileMode.Open);

                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ApplicationSettings));
                    appSettings = (ApplicationSettings)serializer.Deserialize(fileStream);
                }
                catch (Exception)
                {
                    appSettings = new ApplicationSettings();
                }

                fileStream.Close();
            }
            else
            {
                appSettings = new ApplicationSettings();
            }

            return appSettings;
        }
    }
    [Serializable]
    public class ServerSettings
    {
        public bool ServerConnectionEnabled { get; set; }
        public bool ConnectToServerOnStartup { get; set; }
        public bool RetryServerConnectionOnFail { get; set; }
        public string ServerURL { get; set; }
    }
}