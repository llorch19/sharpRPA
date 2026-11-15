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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Xml;

namespace sharpRPA.Core.Script
{
    #region Script and Variables

    public class Script
    {
        private static readonly List<Type> knownCommandTypes = new List<Type>();
        private static readonly object knownTypesLock = new object();

        static Script()
        {
            RegisterKnownTypes(typeof(Script).Assembly);
        }

        /// <summary>
        /// Registers every command type found in the supplied assembly so scripts can be
        /// serialized/deserialized without compile time [XmlInclude] attributes.
        /// </summary>
        public static void RegisterKnownTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                return;
            }

            lock (knownTypesLock)
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.IsAbstract)
                    {
                        continue;
                    }

                    // XmlSerializer can only process publicly visible types
                    if (!type.IsVisible)
                    {
                        continue;
                    }

                    if (!typeof(Core.AutomationCommands.ScriptCommand).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    if (!knownCommandTypes.Contains(type))
                    {
                        knownCommandTypes.Add(type);
                    }
                }
            }
        }

        private static XmlSerializer CreateSerializer()
        {
            lock (knownTypesLock)
            {
                return new XmlSerializer(typeof(Script), knownCommandTypes.ToArray());
            }
        }

        public List<ScriptVariable> Variables { get; set; }

        public List<ScriptAction> Commands;
        public Script()
        {
            Variables = new List<ScriptVariable>();

            Commands = new List<ScriptAction>();
        }

        public ScriptAction AddNewParentCommand(Core.AutomationCommands.ScriptCommand scriptCommand)
        {
            ScriptAction newExecutionCommand = new ScriptAction() { ScriptCommand = scriptCommand };
            Commands.Add(newExecutionCommand);
            return newExecutionCommand;
        }

        public static Script SerializeScript(string scriptFilePath, IEnumerable<Core.AutomationCommands.ScriptCommand> scriptCommands, List<ScriptVariable> scriptVariables)
        {
            //create fileStream
            var fileStream = System.IO.File.Create(scriptFilePath);

            var script = new Core.Script.Script();

            //save variables to file

            script.Variables = scriptVariables;

            //save commands to command list

            int lineNumber = 1;

            List<Core.Script.ScriptAction> subCommands = new List<Core.Script.ScriptAction>();

            foreach (var command in scriptCommands)
            {
                command.LineNumber = lineNumber;

                if ((command is Core.AutomationCommands.BeginLoopCommand) || (command is Core.AutomationCommands.BeginIfCommand))
                {
                    if (subCommands.Count == 0)  //if this is the first loop
                    {
                        //add command to root node
                        var newCommand = script.AddNewParentCommand(command);
                        //add to tracking for additional commands
                        subCommands.Add(newCommand);
                    }
                    else  //we are already looping so add to sub item
                    {
                        //get reference to previous node
                        var parentCommand = subCommands[subCommands.Count - 1];
                        //add as new node to previous node
                        var nextNodeParent = parentCommand.AddAdditionalAction(command);
                        //add to tracking for additional commands
                        subCommands.Add(nextNodeParent);
                    }
                }
                else if ((command is Core.AutomationCommands.EndLoopCommand) || (command is Core.AutomationCommands.EndIfCommand))  //if current loop scenario is ending
                {
                    //get reference to previous node
                    var parentCommand = subCommands[subCommands.Count - 1];
                    //add to end command // DECIDE WHETHER TO ADD WITHIN LOOP NODE OR PREVIOUS NODE
                    parentCommand.AddAdditionalAction(command);
                    //remove last command since loop is ending
                    subCommands.RemoveAt(subCommands.Count - 1);
                }
                else if (subCommands.Count == 0) //add command as a root item
                {
                    script.AddNewParentCommand(command);
                }
                else //we are within a loop so add to the latest tracked loop item
                {
                    var parentCommand = subCommands[subCommands.Count - 1];
                    parentCommand.AddAdditionalAction(command);
                }

                //increment line number
                lineNumber++;
            }

            //output to xml file
            XmlSerializer serializer = CreateSerializer();
            var settings = new XmlWriterSettings();
            settings.NewLineHandling = NewLineHandling.Entitize;
            settings.Indent = true;

            //write to file
            using (XmlWriter writer = XmlWriter.Create(fileStream, settings))
            {
                serializer.Serialize(writer, script);
            }

            fileStream.Close();

            return script;
        }
        public static Script DeserializeFile(string scriptFilePath)
        {
            XmlSerializer serializer = CreateSerializer();
            System.IO.FileStream fs = new System.IO.FileStream(scriptFilePath, System.IO.FileMode.Open);
            XmlReader reader = XmlReader.Create(fs);
            Script deserializedData = (Script)serializer.Deserialize(reader);
            fs.Close();
            return deserializedData;
        }

        public static Script DeserializeXML(string scriptXML)
        {
            System.IO.StringReader reader = new System.IO.StringReader(scriptXML);
            XmlSerializer serializer = CreateSerializer();
            Script deserializedData = (Script)serializer.Deserialize(reader);
            return deserializedData;
        }
    }

    public class ScriptAction
    {
        [XmlElement(Order = 1)]
        public Core.AutomationCommands.ScriptCommand ScriptCommand { get; set; }
        [XmlElement(Order = 2)]
        public List<ScriptAction> AdditionalScriptCommands = new List<ScriptAction>();

        public ScriptAction AddAdditionalAction(Core.AutomationCommands.ScriptCommand scriptCommand)
        {
            ScriptAction newExecutionCommand = new ScriptAction() { ScriptCommand = scriptCommand };
            AdditionalScriptCommands.Add(newExecutionCommand);
            return newExecutionCommand;
        }
    }

    public class ScriptVariable
    {
        public string variableName { get; set; }
        public int currentPosition = 0;
        public object variableValue { get; set; }

        public string GetDisplayValue()
        {
            if (variableValue is string)
            {
                return (string)variableValue;
            }
            else
            {
                List<string> requiredValue = (List<string>)variableValue;
                return requiredValue[currentPosition];
            }
        }
    }

    #endregion Script and Variables
}