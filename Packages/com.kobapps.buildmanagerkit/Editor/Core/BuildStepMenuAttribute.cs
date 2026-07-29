using System;

namespace BuildManagerKit.Editor
{
    /// <summary>Which action lists a step may be added to.</summary>
    [Flags]
    public enum BuildStepScope
    {
        /// <summary>Pre build lists.</summary>
        PreBuild = 1,

        /// <summary>Post build lists.</summary>
        PostBuild = 2,

        /// <summary>The "on activate" list of an environment.</summary>
        EnvironmentActivate = 4,

        /// <summary>Every list.</summary>
        All = PreBuild | PostBuild | EnvironmentActivate
    }

    /// <summary>
    /// Registers a <see cref="BuildStep"/> in the "Add Action" menu of the Build Manager window.
    /// Without it a step type still works when added by code, it just is not offered in the UI.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class BuildStepMenuAttribute : Attribute
    {
        /// <summary>Creates the attribute.</summary>
        /// <param name="menuPath">Slash separated menu path, e.g. <c>"Files/Copy Files"</c>.</param>
        public BuildStepMenuAttribute(string menuPath)
        {
            MenuPath = menuPath;
        }

        /// <summary>Slash separated path shown in the Add Action menu.</summary>
        public string MenuPath { get; }

        /// <summary>Longer explanation shown as a tooltip and in the action header.</summary>
        public string Tooltip { get; set; } = string.Empty;

        /// <summary>Lists this step is offered in.</summary>
        public BuildStepScope Scope { get; set; } = BuildStepScope.All;

        /// <summary>Lower values sort first within their menu folder.</summary>
        public int Order { get; set; }
    }

    /// <summary>
    /// Marks a static method as a build hook for teams that prefer plain code over configured
    /// actions. The method must be static and take a single <see cref="BuildContext"/> parameter.
    /// Hooks run for every build, before (or after) the configured action lists.
    /// </summary>
    /// <example>
    /// <code>
    /// [BuildHook(BuildStepScope.PreBuild, Order = -100)]
    /// static void StampLicences(BuildContext context) => LicenceBaker.Bake(context.Version);
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class BuildHookAttribute : Attribute
    {
        /// <summary>Creates the attribute.</summary>
        /// <param name="scope">Phase the method runs in.</param>
        public BuildHookAttribute(BuildStepScope scope = BuildStepScope.PreBuild)
        {
            Scope = scope;
        }

        /// <summary>Phase the method runs in. Only a single flag is honoured.</summary>
        public BuildStepScope Scope { get; }

        /// <summary>Lower values run first.</summary>
        public int Order { get; set; }
    }
}
