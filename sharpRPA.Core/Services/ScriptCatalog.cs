using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using sharpRPA.Core.AutomationCommands;
using sharpRPA.Core.Script;
using ScriptModel = sharpRPA.Core.Script.Script;

namespace sharpRPA.Core.Services
{
    public sealed class ScriptSummary
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime LastModified { get; set; }
        public long SizeBytes { get; set; }
    }

    public sealed class ScriptCommandInfo
    {
        public int LineNumber { get; set; }
        public string CommandType { get; set; }
        public string DisplayValue { get; set; }
        public bool IsCommented { get; set; }
        public int Depth { get; set; }
    }

    public sealed class ScriptVariableInfo
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public sealed class ScriptDetail
    {
        public string Path { get; set; }
        public List<ScriptVariableInfo> Variables { get; set; } = new List<ScriptVariableInfo>();
        public List<ScriptCommandInfo> Commands { get; set; } = new List<ScriptCommandInfo>();
    }

    public sealed class ScriptValidationResult
    {
        public bool IsValid { get { return Errors.Count == 0; } }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Reads, flattens and validates sharpRPA script files without any UI dependency.
    /// </summary>
    public sealed class ScriptCatalog
    {
        private static readonly Regex VariableToken = new Regex(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

        private readonly string scriptDirectory;

        public ScriptCatalog(string scriptDirectory = null)
        {
            this.scriptDirectory = scriptDirectory ?? Common.GetScriptFolderPath();
        }

        public string ScriptDirectory
        {
            get { return scriptDirectory; }
        }

        public List<ScriptSummary> ListScripts()
        {
            if (!Directory.Exists(scriptDirectory))
            {
                return new List<ScriptSummary>();
            }

            return Directory.GetFiles(scriptDirectory, "*.xml")
                .Select(file => new FileInfo(file))
                .Select(file => new ScriptSummary
                {
                    Name = file.Name,
                    Path = file.FullName,
                    LastModified = file.LastWriteTime,
                    SizeBytes = file.Length
                })
                .OrderByDescending(summary => summary.LastModified)
                .ToList();
        }

        public string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A script path is required.", nameof(path));
            }

            return Path.IsPathRooted(path) ? path : Path.Combine(scriptDirectory, path);
        }

        public ScriptDetail GetScript(string path)
        {
            var resolvedPath = ResolvePath(path);
            var script = ScriptModel.DeserializeFile(resolvedPath);

            var detail = new ScriptDetail { Path = resolvedPath };

            foreach (var variable in script.Variables)
            {
                detail.Variables.Add(new ScriptVariableInfo
                {
                    Name = variable.variableName,
                    Value = variable.variableValue == null ? string.Empty : variable.variableValue.ToString()
                });
            }

            Flatten(script.Commands, 0, detail.Commands);

            return detail;
        }

        /// <summary>
        /// Loads the raw script model (command objects, not flattened summaries) so callers
        /// such as the target index can inspect command properties.
        /// </summary>
        public ScriptModel LoadScript(string path)
        {
            return ScriptModel.DeserializeFile(ResolvePath(path));
        }

        public ScriptValidationResult ValidateScript(string path)
        {
            var result = new ScriptValidationResult();
            var resolvedPath = ResolvePath(path);

            if (!File.Exists(resolvedPath))
            {
                result.Errors.Add("Script file not found: " + resolvedPath);
                return result;
            }

            ScriptModel script;
            try
            {
                script = ScriptModel.DeserializeFile(resolvedPath);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Script could not be parsed: " + ex.Message);
                return result;
            }

            return ValidateLoaded(script, result);
        }

        /// <summary>
        /// Validates script XML in memory, before it is written to disk.
        /// </summary>
        public ScriptValidationResult ValidateXml(string scriptXml)
        {
            var result = new ScriptValidationResult();

            if (string.IsNullOrWhiteSpace(scriptXml))
            {
                result.Errors.Add("Script XML is empty.");
                return result;
            }

            ScriptModel script;
            try
            {
                script = ScriptModel.DeserializeXML(scriptXml);
            }
            catch (Exception ex)
            {
                result.Errors.Add("Script could not be parsed: " + ex.Message);
                return result;
            }

            return ValidateLoaded(script, result);
        }

        private static ScriptValidationResult ValidateLoaded(ScriptModel script, ScriptValidationResult result)
        {
            if ((script.Commands == null) || (script.Commands.Count == 0))
            {
                result.Errors.Add("Script contains no commands.");
            }
            else
            {
                foreach (var action in script.Commands)
                {
                    ValidateAction(action, true, result);
                }
            }

            ValidateVariables(script, result);

            return result;
        }

        /// <summary>
        /// Creates a new script file. Throws <see cref="IOException"/> when it already exists.
        /// </summary>
        public string CreateScript(string name, string scriptXml = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A script name is required.", nameof(name));
            }

            var fileName = name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? name : name + ".xml";
            var path = ResolvePath(fileName);

            Directory.CreateDirectory(scriptDirectory);

            var payload = string.IsNullOrWhiteSpace(scriptXml)
                ? ScriptModel.SerializeToString(new ScriptModel())
                : scriptXml;

            // CreateNew keeps the existence check atomic
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new System.Text.UTF8Encoding(false).GetBytes(payload);
                stream.Write(bytes, 0, bytes.Length);
            }

            return path;
        }

        /// <summary>
        /// Replaces an existing script atomically (write temp file, then move over).
        /// Throws <see cref="FileNotFoundException"/> when the script does not exist.
        /// </summary>
        public string UpdateScript(string path, string scriptXml)
        {
            if (string.IsNullOrWhiteSpace(scriptXml))
            {
                throw new ArgumentException("Script XML is required.", nameof(scriptXml));
            }

            var resolvedPath = ResolvePath(path);

            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException("Script file not found: " + resolvedPath, resolvedPath);
            }

            WriteAtomic(resolvedPath, scriptXml);
            return resolvedPath;
        }

        private static void WriteAtomic(string path, string content)
        {
            var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                File.WriteAllText(temporaryPath, content, new System.Text.UTF8Encoding(false));
                File.Move(temporaryPath, path, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        private static void Flatten(IEnumerable<ScriptAction> actions, int depth, List<ScriptCommandInfo> target)
        {
            foreach (var action in actions)
            {
                if ((action == null) || (action.ScriptCommand == null))
                {
                    continue;
                }

                var command = action.ScriptCommand;
                target.Add(new ScriptCommandInfo
                {
                    LineNumber = command.LineNumber,
                    CommandType = command.GetType().Name,
                    DisplayValue = command.GetDisplayValue(),
                    IsCommented = command.IsCommented,
                    Depth = depth
                });

                if (action.AdditionalScriptCommands.Count > 0)
                {
                    Flatten(action.AdditionalScriptCommands, depth + 1, target);
                }
            }
        }

        private static void ValidateAction(ScriptAction action, bool isRoot, ScriptValidationResult result)
        {
            if ((action == null) || (action.ScriptCommand == null))
            {
                result.Errors.Add("Encountered an empty command entry.");
                return;
            }

            var command = action.ScriptCommand;
            var label = "Line " + command.LineNumber + ": ";

            if (isRoot && ((command is EndLoopCommand) || (command is EndIfCommand) || (command is ElseCommand)))
            {
                result.Errors.Add(label + "'" + command.SelectionName + "' has no matching begin command.");
            }

            if (command is BeginLoopCommand)
            {
                ValidateBlock(action, typeof(EndLoopCommand), "End Loop", label, result);
            }
            else if (command is BeginIfCommand)
            {
                ValidateBlock(action, typeof(EndIfCommand), "End If", label, result);
            }
            else if (action.AdditionalScriptCommands.Count > 0)
            {
                result.Warnings.Add(label + "'" + command.SelectionName + "' contains nested commands but does not open a block.");
            }

            foreach (var child in action.AdditionalScriptCommands)
            {
                ValidateAction(child, false, result);
            }
        }

        private static void ValidateBlock(ScriptAction action, Type expectedEnd, string expectedEndName, string label, ScriptValidationResult result)
        {
            var children = action.AdditionalScriptCommands;
            var endIndex = children.FindIndex(child => (child != null) && (expectedEnd.IsInstanceOfType(child.ScriptCommand)));

            if (endIndex < 0)
            {
                result.Errors.Add(label + "'" + action.ScriptCommand.SelectionName + "' is missing its '" + expectedEndName + "'.");
                return;
            }

            if (endIndex != children.Count - 1)
            {
                result.Warnings.Add(label + "'" + expectedEndName + "' is not the last entry of its block.");
            }
        }

        private static void ValidateVariables(ScriptModel script, ScriptValidationResult result)
        {
            var knownNames = new HashSet<string>(StringComparer.Ordinal);
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var variable in script.Variables)
            {
                if (string.IsNullOrWhiteSpace(variable.variableName))
                {
                    result.Errors.Add("A script variable has no name.");
                    continue;
                }

                if (!seenNames.Add(variable.variableName))
                {
                    result.Errors.Add("Duplicate variable name: " + variable.variableName);
                }

                knownNames.Add(variable.variableName);
            }

            foreach (var systemVariable in Common.GenerateSystemVariables())
            {
                knownNames.Add(systemVariable.variableName);
            }

            foreach (var action in Walk(script.Commands))
            {
                var command = action.ScriptCommand;
                foreach (var property in command.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || (property.PropertyType != typeof(string)))
                    {
                        continue;
                    }

                    var value = property.GetValue(command) as string;
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    foreach (Match match in Regex.Matches(value, @"\[(.*?)\]"))
                    {
                        var token = match.Groups[1].Value;
                        if (!VariableToken.IsMatch(token))
                        {
                            continue;
                        }

                        if (!knownNames.Contains(token))
                        {
                            result.Warnings.Add("Line " + command.LineNumber + ": reference to unknown variable '[" + token + "]'.");
                        }
                    }
                }
            }
        }

        public static IEnumerable<ScriptAction> Walk(IEnumerable<ScriptAction> actions)
        {
            foreach (var action in actions)
            {
                if ((action == null) || (action.ScriptCommand == null))
                {
                    continue;
                }

                yield return action;

                foreach (var child in Walk(action.AdditionalScriptCommands))
                {
                    yield return child;
                }
            }
        }
    }
}
