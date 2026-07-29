using System;
using System.Linq;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Adds or removes scripting defines for the target being built. Defines are restored with the
    /// rest of the player settings when the run finishes, unless that is disabled in the settings.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Player Settings/Set Scripting Defines",
        Tooltip = "Adds or removes scripting define symbols for this build.",
        Scope = BuildStepScope.PreBuild | BuildStepScope.EnvironmentActivate,
        Order = -90)]
    public sealed class SetScriptingDefinesStep : BuildStep
    {
        [Tooltip("Defines to add. Tokens are supported, e.g. BUILD_{ENV}.")]
        [SerializeField] private string[] m_Add = Array.Empty<string>();

        [Tooltip("Defines to remove.")]
        [SerializeField] private string[] m_Remove = Array.Empty<string>();

        /// <inheritdoc />
        public override string Summary
        {
            get
            {
                var added = m_Add.Length > 0 ? "+" + string.Join(" +", m_Add) : string.Empty;
                var removed = m_Remove.Length > 0 ? " -" + string.Join(" -", m_Remove) : string.Empty;
                return (added + removed).Trim();
            }
        }

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            var current = ScriptingDefineUtility.Get(context.NamedTarget).ToList();

            foreach (var define in m_Add.Select(context.Resolve).Where(d => !string.IsNullOrWhiteSpace(d)))
            {
                if (!current.Contains(define.Trim()))
                    current.Add(define.Trim());
            }

            foreach (var define in m_Remove.Select(context.Resolve).Where(d => !string.IsNullOrWhiteSpace(d)))
                current.RemoveAll(entry => string.Equals(entry, define.Trim(), StringComparison.Ordinal));

            var normalized = ScriptingDefineUtility.Normalize(current);

            if (context.DryRun)
            {
                context.Log.Info($"[dry run] Defines would become: {string.Join(";", normalized)}");
                return;
            }

            if (ScriptingDefineUtility.Set(context.NamedTarget, normalized))
                context.Log.Info($"Defines: {string.Join(";", normalized)}");
            else
                context.Log.Info("Defines already up to date.");

            context.ScriptingDefines = normalized;
        }
    }
}
