using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>One entry of the "Add Action" menu.</summary>
    public sealed class BuildStepDescriptor
    {
        /// <summary>Concrete step type.</summary>
        public Type Type { get; internal set; }

        /// <summary>Slash separated menu path.</summary>
        public string MenuPath { get; internal set; }

        /// <summary>Leaf name of <see cref="MenuPath"/>.</summary>
        public string DisplayName { get; internal set; }

        /// <summary>Menu folder, empty for root level entries.</summary>
        public string Category { get; internal set; }

        /// <summary>Tooltip shown in the menu and the action header.</summary>
        public string Tooltip { get; internal set; }

        /// <summary>Lists this step may be added to.</summary>
        public BuildStepScope Scope { get; internal set; }

        /// <summary>Sort weight within the category.</summary>
        public int Order { get; internal set; }

        /// <summary>Creates a fresh instance of the step.</summary>
        public BuildStep CreateInstance() => (BuildStep)Activator.CreateInstance(Type);
    }

    /// <summary>A static method registered with <see cref="BuildHookAttribute"/>.</summary>
    public sealed class BuildHookDescriptor
    {
        /// <summary>The method to invoke.</summary>
        public MethodInfo Method { get; internal set; }

        /// <summary>Phase the hook belongs to.</summary>
        public BuildStepScope Scope { get; internal set; }

        /// <summary>Sort weight, lower runs first.</summary>
        public int Order { get; internal set; }

        /// <summary>Fully qualified name shown in the log.</summary>
        public string DisplayName => $"{Method.DeclaringType?.Name}.{Method.Name}";

        /// <summary>Invokes the hook.</summary>
        public void Invoke(BuildContext context) => Method.Invoke(null, new object[] { context });
    }

    /// <summary>
    /// Discovers every <see cref="BuildStep"/> implementation and every <see cref="BuildHookAttribute"/>
    /// method in the project. Results are cached and rebuilt on domain reload, so adding a custom
    /// step is just a matter of writing the class.
    /// </summary>
    public static class BuildStepRegistry
    {
        private static readonly Dictionary<Assembly, bool> k_TestAssemblies = new Dictionary<Assembly, bool>();

        private static List<BuildStepDescriptor> s_Descriptors;
        private static List<BuildHookDescriptor> s_Hooks;
        private static Dictionary<Type, BuildStepDescriptor> s_ByType;

        /// <summary>Every registered step type, sorted by category then order then name.</summary>
        public static IReadOnlyList<BuildStepDescriptor> Descriptors
        {
            get
            {
                EnsureBuilt();
                return s_Descriptors;
            }
        }

        /// <summary>Every discovered code hook, sorted by order.</summary>
        public static IReadOnlyList<BuildHookDescriptor> Hooks
        {
            get
            {
                EnsureBuilt();
                return s_Hooks;
            }
        }

        /// <summary>Steps that may be added to a list of the given <paramref name="scope"/>.</summary>
        public static IEnumerable<BuildStepDescriptor> GetDescriptors(BuildStepScope scope) =>
            Descriptors.Where(descriptor => (descriptor.Scope & scope) != 0);

        /// <summary>Code hooks belonging to a single phase, in execution order.</summary>
        public static IEnumerable<BuildHookDescriptor> GetHooks(BuildStepScope scope) =>
            Hooks.Where(hook => (hook.Scope & scope) != 0);

        /// <summary>The descriptor for a step type, or null when the type is not registered.</summary>
        public static BuildStepDescriptor GetDescriptor(Type type)
        {
            EnsureBuilt();
            return type != null && s_ByType.TryGetValue(type, out var descriptor) ? descriptor : null;
        }

        /// <summary>
        /// Friendly name for a step type: the menu leaf when registered, otherwise the class name
        /// split into words with a trailing "Step" removed.
        /// </summary>
        public static string GetDisplayName(Type type)
        {
            if (type == null)
                return "Missing Action";

            var descriptor = GetDescriptor(type);
            if (descriptor != null)
                return descriptor.DisplayName;

            var name = type.Name;
            if (name.EndsWith("Step", StringComparison.Ordinal) && name.Length > 4)
                name = name.Substring(0, name.Length - 4);

            return Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
        }

        /// <summary>Tooltip for a step type, empty when it has none.</summary>
        public static string GetTooltip(Type type) => GetDescriptor(type)?.Tooltip ?? string.Empty;

        /// <summary>Forces a rebuild of the caches.</summary>
        public static void Refresh()
        {
            s_Descriptors = null;
            s_Hooks = null;
            s_ByType = null;
            k_TestAssemblies.Clear();
        }

        [InitializeOnLoadMethod]
        private static void OnDomainReload() => Refresh();

        private static void EnsureBuilt()
        {
            if (s_Descriptors != null)
                return;

            s_Descriptors = new List<BuildStepDescriptor>();
            s_ByType = new Dictionary<Type, BuildStepDescriptor>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<BuildStep>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;

                // Test assemblies are loaded in the Editor, so without this the step types used by
                // the package's own test fixtures show up in the user's Add Action menu — and one
                // picked by mistake ends up serialised into a real profile.
                if (IsTestAssembly(type.Assembly))
                    continue;

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] Build step '{type.FullName}' has no parameterless constructor and is ignored.");
                    continue;
                }

                var attribute = type.GetCustomAttribute<BuildStepMenuAttribute>();
                var menuPath = attribute != null && !string.IsNullOrWhiteSpace(attribute.MenuPath)
                    ? attribute.MenuPath.Trim('/')
                    : "Custom/" + SplitCamelCase(type.Name);

                var separator = menuPath.LastIndexOf('/');

                var descriptor = new BuildStepDescriptor
                {
                    Type = type,
                    MenuPath = menuPath,
                    DisplayName = separator >= 0 ? menuPath.Substring(separator + 1) : menuPath,
                    Category = separator >= 0 ? menuPath.Substring(0, separator) : string.Empty,
                    Tooltip = attribute != null ? attribute.Tooltip : string.Empty,
                    Scope = attribute != null ? attribute.Scope : BuildStepScope.All,
                    Order = attribute != null ? attribute.Order : 0
                };

                s_Descriptors.Add(descriptor);
                s_ByType[type] = descriptor;
            }

            s_Descriptors.Sort((left, right) =>
            {
                var byCategory = string.Compare(left.Category, right.Category, StringComparison.Ordinal);
                if (byCategory != 0)
                    return byCategory;

                var byOrder = left.Order.CompareTo(right.Order);
                return byOrder != 0
                    ? byOrder
                    : string.Compare(left.DisplayName, right.DisplayName, StringComparison.Ordinal);
            });

            BuildHookList();
        }

        private static void BuildHookList()
        {
            s_Hooks = new List<BuildHookDescriptor>();

            foreach (var method in TypeCache.GetMethodsWithAttribute<BuildHookAttribute>())
            {
                var attribute = method.GetCustomAttribute<BuildHookAttribute>();
                if (attribute == null)
                    continue;

                // Same reasoning as the step types: a hook defined by a test fixture must not run
                // during a real build.
                if (IsTestAssembly(method.DeclaringType?.Assembly))
                    continue;

                if (!method.IsStatic)
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] [BuildHook] method '{method.DeclaringType?.FullName}.{method.Name}' must be static.");
                    continue;
                }

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(BuildContext))
                {
                    Debug.LogWarning(
                        $"[BuildManagerKit] [BuildHook] method '{method.DeclaringType?.FullName}.{method.Name}' must take a single BuildContext parameter.");
                    continue;
                }

                s_Hooks.Add(new BuildHookDescriptor
                {
                    Method = method,
                    Scope = attribute.Scope,
                    Order = attribute.Order
                });
            }

            s_Hooks.Sort((left, right) => left.Order.CompareTo(right.Order));
        }

        /// <summary>
        /// True when an assembly is a test assembly. Detected by its reference to NUnit rather
        /// than by name, so it holds for any project's own test assemblies too.
        /// </summary>
        private static bool IsTestAssembly(Assembly assembly)
        {
            if (assembly == null)
                return false;

            if (k_TestAssemblies.TryGetValue(assembly, out var cached))
                return cached;

            var isTest = false;

            try
            {
                isTest = assembly.GetReferencedAssemblies()
                    .Any(reference => reference.Name.IndexOf("nunit", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch (Exception)
            {
                // An assembly whose references cannot be read is treated as production code.
            }

            k_TestAssemblies[assembly] = isTest;
            return isTest;
        }

        private static string SplitCamelCase(string value) =>
            Regex.Replace(value, "(?<=[a-z0-9])(?=[A-Z])", " ");
    }
}
