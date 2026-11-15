using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using sharpRPA.Core.AutomationCommands;
using ClassAttributes = sharpRPA.Core.AutomationCommands.Attributes.ClassAttributes;
using PropertyAttributes = sharpRPA.Core.AutomationCommands.Attributes.PropertyAttributes;

namespace sharpRPA.Core.Services
{
    public sealed class CommandPropertyInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Type { get; set; }
        public List<string> Options { get; set; } = new List<string>();
        public bool SupportsVariables { get; set; }
        public bool SupportsFilePicker { get; set; }
    }

    public sealed class CommandInfo
    {
        public string TypeName { get; set; }
        public string Group { get; set; }
        public string SelectionName { get; set; }
        public string Description { get; set; }
        public string Implementation { get; set; }
        public List<CommandPropertyInfo> Properties { get; set; } = new List<CommandPropertyInfo>();
    }

    public sealed class CommandSummary
    {
        public string TypeName { get; set; }
        public string Group { get; set; }
        public string SelectionName { get; set; }
        public string Description { get; set; }
    }

    /// <summary>
    /// Reflects the automation command catalogue so agents can discover available
    /// commands and their parameters without reading the source.
    /// </summary>
    public sealed class CommandCatalog
    {
        public List<CommandInfo> List()
        {
            return Common.GetCommandTypes()
                .Select(CreateInfo)
                .Where(info => info != null)
                .OrderBy(info => info.Group, StringComparer.Ordinal)
                .ThenBy(info => info.SelectionName, StringComparer.Ordinal)
                .ToList();
        }

        public List<CommandSummary> ListSummaries()
        {
            return List()
                .Select(info => new CommandSummary
                {
                    TypeName = info.TypeName,
                    Group = info.Group,
                    SelectionName = info.SelectionName,
                    Description = info.Description
                })
                .ToList();
        }

        public CommandInfo Describe(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                throw new ArgumentException("A command name is required.", nameof(commandName));
            }

            var types = Common.GetCommandTypes();
            var type = types.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, commandName, StringComparison.OrdinalIgnoreCase))
                ?? types.FirstOrDefault(candidate =>
                {
                    var instance = CreateInfo(candidate);
                    return (instance != null) && string.Equals(instance.SelectionName, commandName, StringComparison.OrdinalIgnoreCase);
                });

            if (type == null)
            {
                return null;
            }

            return CreateInfo(type);
        }

        private static CommandInfo CreateInfo(Type type)
        {
            ScriptCommand instance;
            try
            {
                instance = (ScriptCommand)Activator.CreateInstance(type);
            }
            catch (Exception)
            {
                return null;
            }

            var group = type.GetCustomAttribute<ClassAttributes.Group>();
            var description = type.GetCustomAttribute<ClassAttributes.Description>();
            var implementation = type.GetCustomAttribute<ClassAttributes.ImplementationDescription>();

            var info = new CommandInfo
            {
                TypeName = type.Name,
                Group = group == null ? string.Empty : group.groupName,
                SelectionName = instance.SelectionName,
                Description = description == null ? string.Empty : description.commandFunctionalDescription,
                Implementation = implementation == null ? string.Empty : implementation.commandImplementationDescription
            };

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propertyDescription = property.GetCustomAttribute<PropertyAttributes.PropertyDescription>();
                if ((propertyDescription == null) && !property.Name.StartsWith("v_"))
                {
                    continue;
                }

                if (!property.CanRead)
                {
                    continue;
                }

                var propertyInfo = new CommandPropertyInfo
                {
                    Name = property.Name,
                    Description = propertyDescription == null ? string.Empty : propertyDescription.propertyDescription,
                    Type = MapType(property.PropertyType)
                };

                foreach (var option in property.GetCustomAttributes<PropertyAttributes.PropertyUISelectionOption>())
                {
                    propertyInfo.Options.Add(option.uiOption);
                }

                foreach (var helper in property.GetCustomAttributes<PropertyAttributes.PropertyUIHelper>())
                {
                    if (helper.additionalHelper == PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowVariableHelper)
                    {
                        propertyInfo.SupportsVariables = true;
                    }

                    if (helper.additionalHelper == PropertyAttributes.PropertyUIHelper.UIAdditionalHelperType.ShowFileSelectionHelper)
                    {
                        propertyInfo.SupportsFilePicker = true;
                    }
                }

                info.Properties.Add(propertyInfo);
            }

            return info;
        }

        private static string MapType(Type type)
        {
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "integer";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(double) || type == typeof(decimal)) return "number";
            if (type == typeof(System.Data.DataTable)) return "table";
            return type.Name;
        }
    }
}
