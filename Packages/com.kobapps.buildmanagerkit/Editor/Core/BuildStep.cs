using System;
using System.Linq;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Base class for every pre/post build action.
    ///
    /// Derive from it, add a <see cref="BuildStepMenuAttribute"/> and the step automatically shows
    /// up in the "Add Action" menu of the Build Manager window. Steps are stored inline on the
    /// owning asset with <c>[SerializeReference]</c>, so no extra asset files are created and
    /// ordinary <c>[SerializeField]</c> members are edited in the inspector as usual.
    /// </summary>
    /// <example>
    /// <code>
    /// [BuildStepMenu("Custom/Print Version", Tooltip = "Logs the version being built.")]
    /// [Serializable]
    /// public sealed class PrintVersionStep : BuildStep
    /// {
    ///     [SerializeField] private string m_Prefix = "Building";
    ///
    ///     public override string Summary => m_Prefix;
    ///
    ///     public override void Execute(BuildContext context)
    ///     {
    ///         context.Log.Info($"{m_Prefix} {context.Version}+{context.BuildNumber}");
    ///     }
    /// }
    /// </code>
    /// </example>
    [Serializable]
    public abstract class BuildStep
    {
        [SerializeField, HideInInspector] private string m_Guid;

        [Tooltip("Disabled actions are skipped but kept in the list.")]
        [SerializeField] private bool m_Enabled = true;

        [Tooltip("Optional name shown instead of the action type.")]
        [SerializeField] private string m_Label = string.Empty;

        [Tooltip("What happens when this action throws.")]
        [SerializeField] private StepFailurePolicy m_OnError = StepFailurePolicy.FailBuild;

        [Tooltip("Post build only: restricts the action to successful or failed builds.")]
        [SerializeField] private StepRunCondition m_RunWhen = StepRunCondition.Always;

        [Tooltip("Comma separated environment ids this action is limited to. Empty means all.")]
        [SerializeField] private string m_EnvironmentFilter = string.Empty;

        [Tooltip("Optional override key. When actions in the global, environment and profile lists share "
                 + "a key, only the most specific one runs: profile beats environment beats global. Use it "
                 + "to replace a global action for one profile or environment. Empty means always run.")]
        [SerializeField] private string m_Key = string.Empty;

        /// <summary>Stable identifier, generated on first access. Survives reordering.</summary>
        public string Guid
        {
            get
            {
                if (string.IsNullOrEmpty(m_Guid))
                    m_Guid = System.Guid.NewGuid().ToString("N");

                return m_Guid;
            }
        }

        /// <summary>Disabled actions are skipped.</summary>
        public bool Enabled
        {
            get => m_Enabled;
            set => m_Enabled = value;
        }

        /// <summary>
        /// What happens when <see cref="Execute"/> throws. Override it when an action should
        /// always be advisory, regardless of how it is configured.
        /// </summary>
        public virtual StepFailurePolicy OnError => m_OnError;

        /// <summary>Success/failure filter honoured during the post build phase.</summary>
        public virtual StepRunCondition RunWhen => m_RunWhen;

        /// <summary>
        /// Override key. Actions sharing a key collapse to the most specific one — profile over
        /// environment over global. Empty when the action always runs.
        /// </summary>
        public string Key => string.IsNullOrWhiteSpace(m_Key) ? string.Empty : m_Key.Trim();

        internal void SetKey(string key) => m_Key = key ?? string.Empty;

        /// <summary>Name shown in the UI: the custom label, otherwise the menu name.</summary>
        public string Title =>
            string.IsNullOrWhiteSpace(m_Label) ? BuildStepRegistry.GetDisplayName(GetType()) : m_Label;

        /// <summary>
        /// Short one-line description of what this instance is configured to do. Shown collapsed
        /// in the action list; override it to make lists readable at a glance.
        /// </summary>
        public virtual string Summary => string.Empty;

        /// <summary>
        /// Runs the action. Throw <see cref="BuildStepException"/> (or any exception) to signal
        /// failure; <see cref="OnError"/> decides whether that aborts the build.
        /// </summary>
        /// <param name="context">State of the run in progress.</param>
        public abstract void Execute(BuildContext context);

        /// <summary>
        /// Optional configuration check run before anything is built, both by the Validate button
        /// and by every real build. Report problems into <paramref name="report"/>.
        /// </summary>
        public virtual void Validate(BuildContext context, BuildValidationReport report)
        {
        }

        /// <summary>
        /// Decides whether this action runs for the current context. The default implementation
        /// honours <see cref="Enabled"/>, the environment filter and, in the post build phase,
        /// <see cref="RunWhen"/>. Override to add your own conditions and call <c>base</c> first.
        /// </summary>
        public virtual bool ShouldRun(BuildContext context)
        {
            if (!m_Enabled)
                return false;

            if (!PassesEnvironmentFilter(context))
                return false;

            if (context.Phase != BuildPhase.PostBuild)
                return true;

            var succeeded = context.Status != BuildRunStatus.Failed && context.Status != BuildRunStatus.Cancelled;

            switch (RunWhen)
            {
                case StepRunCondition.OnSuccess: return succeeded;
                case StepRunCondition.OnFailure: return !succeeded;
                default: return true;
            }
        }

        private bool PassesEnvironmentFilter(BuildContext context)
        {
            if (string.IsNullOrWhiteSpace(m_EnvironmentFilter))
                return true;

            var environmentId = context.Environment != null ? context.Environment.Id : string.Empty;

            return m_EnvironmentFilter
                .Split(',')
                .Select(entry => entry.Trim())
                .Where(entry => entry.Length > 0)
                .Any(entry => string.Equals(entry, environmentId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Thrown by a build step to report a clean, expected failure. The message is shown to the
    /// user without a stack trace, unlike other exception types.
    /// </summary>
    public sealed class BuildStepException : Exception
    {
        /// <summary>Creates the exception with a user facing message.</summary>
        public BuildStepException(string message) : base(message)
        {
        }

        /// <summary>Creates the exception with a user facing message and an inner cause.</summary>
        public BuildStepException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
