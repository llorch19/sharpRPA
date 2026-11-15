using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace sharpRPA.Core.Security
{
    /// <summary>
    /// Execution policy loaded from sharprpa.policy.json. Defaults are secure: when no
    /// roots are configured the caller adds the script folder, so run_script cannot be
    /// pointed at an arbitrary file.
    /// </summary>
    public sealed class ScriptPolicy
    {
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public List<string> ScriptRoots { get; set; } = new List<string>();

        public List<string> DeniedCommandTypes { get; set; } = new List<string>();

        public int RunTimeoutSeconds { get; set; } = 300;

        public int MaxRecordingEvents { get; set; } = 200000;

        /// <summary>
        /// Loads the policy. Resolution order: explicit path, SHARPRPA_POLICY,
        /// &lt;workspace&gt;/sharprpa.policy.json, built-in defaults.
        /// </summary>
        public static ScriptPolicy Load(string path = null, string workspaceDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Environment.GetEnvironmentVariable("SHARPRPA_POLICY");
            }

            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(workspaceDirectory))
            {
                var candidate = Path.Combine(workspaceDirectory, "sharprpa.policy.json");
                if (File.Exists(candidate))
                {
                    path = candidate;
                }
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new ScriptPolicy();
            }

            ScriptPolicy policy;
            try
            {
                policy = JsonSerializer.Deserialize<ScriptPolicy>(File.ReadAllText(path), SerializerOptions);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Policy file could not be parsed: " + path + " (" + ex.Message + ")", ex);
            }

            return policy ?? new ScriptPolicy();
        }

        /// <summary>Locks the policy to a directory when the author did not declare roots.</summary>
        public void UseDefaultRoot(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || (ScriptRoots.Count > 0))
            {
                return;
            }

            ScriptRoots.Add(Path.GetFullPath(directory));
        }

        public bool IsPathAllowed(string resolvedPath)
        {
            if ((ScriptRoots.Count == 0) || string.IsNullOrWhiteSpace(resolvedPath))
            {
                return true;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(resolvedPath);
            }
            catch (Exception)
            {
                return false;
            }

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            return ScriptRoots.Any(root =>
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    return false;
                }

                string fullRoot;
                try
                {
                    fullRoot = Path.GetFullPath(root);
                }
                catch (Exception)
                {
                    return false;
                }

                var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;

                return fullPath.StartsWith(prefix, comparison)
                    || string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), comparison);
            });
        }

        public bool IsCommandAllowed(string commandTypeName)
        {
            if (string.IsNullOrWhiteSpace(commandTypeName) || (DeniedCommandTypes.Count == 0))
            {
                return true;
            }

            return !DeniedCommandTypes.Any(denied =>
                string.Equals((denied ?? string.Empty).Trim(), commandTypeName, StringComparison.OrdinalIgnoreCase));
        }

        public string DescribeRoots()
        {
            return ScriptRoots.Count == 0 ? "(unrestricted)" : string.Join(", ", ScriptRoots);
        }
    }
}
