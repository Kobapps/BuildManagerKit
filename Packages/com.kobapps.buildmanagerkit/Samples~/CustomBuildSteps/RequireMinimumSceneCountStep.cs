using System;
using UnityEngine;
using BuildManagerKit.Editor;

namespace BuildManagerKit.Samples
{
    /// <summary>
    /// Guards against the classic "shipped a build with only the boot scene in it" mistake.
    ///
    /// Demonstrates the smallest useful action: a single field, a throw, no dry run branch needed
    /// because the check has no side effects.
    /// </summary>
    [Serializable]
    [BuildStepMenu("Custom/Require Minimum Scene Count",
        Tooltip = "Fails the build when fewer than N scenes were resolved.",
        Scope = BuildStepScope.PreBuild,
        Order = -150)]
    public sealed class RequireMinimumSceneCountStep : BuildStep
    {
        [SerializeField, Min(1)] private int m_Minimum = 2;

        /// <inheritdoc />
        public override string Summary => $"at least {m_Minimum} scenes";

        /// <inheritdoc />
        public override void Execute(BuildContext context)
        {
            if (context.Scenes.Length < m_Minimum)
                throw new BuildStepException(
                    $"Only {context.Scenes.Length} scene(s) resolved, expected at least {m_Minimum}. "
                    + "Check the profile's scene source.");

            context.Log.Info($"{context.Scenes.Length} scene(s) will be built.");
        }
    }
}
