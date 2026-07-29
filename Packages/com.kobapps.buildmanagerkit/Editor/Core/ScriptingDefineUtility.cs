using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Reads and writes scripting define symbols without stomping on defines the kit does not own.
    ///
    /// The core idea: before applying an environment every define that <em>any</em> known
    /// environment contributes is stripped, then the incoming environment's defines are added.
    /// Switching from <c>dev</c> to <c>prod</c> therefore never leaves <c>ENV_DEV</c> behind, and
    /// defines that came from elsewhere are preserved.
    /// </summary>
    public static class ScriptingDefineUtility
    {
        private static readonly char[] k_Separators = { ';', ',' };

        /// <summary>Current defines for a target, already split and trimmed.</summary>
        public static string[] Get(NamedBuildTarget namedTarget)
        {
            try
            {
                var raw = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                return Split(raw);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Writes defines for a target. The list is de-duplicated and sorted so the resulting
        /// ProjectSettings diff stays stable in version control.
        /// </summary>
        /// <returns>True when the value actually changed.</returns>
        public static bool Set(NamedBuildTarget namedTarget, IEnumerable<string> defines)
        {
            var ordered = Normalize(defines);
            var joined = string.Join(";", ordered);

            string current;
            try
            {
                current = PlayerSettings.GetScriptingDefineSymbols(namedTarget) ?? string.Empty;
            }
            catch (Exception)
            {
                current = string.Empty;
            }

            if (string.Equals(current, joined, StringComparison.Ordinal))
                return false;

            PlayerSettings.SetScriptingDefineSymbols(namedTarget, joined);
            return true;
        }

        /// <summary>Splits a raw <c>;</c> separated define string.</summary>
        public static string[] Split(string raw) =>
            string.IsNullOrWhiteSpace(raw)
                ? Array.Empty<string>()
                : raw.Split(k_Separators, StringSplitOptions.RemoveEmptyEntries)
                    .Select(entry => entry.Trim())
                    .Where(entry => entry.Length > 0)
                    .ToArray();

        /// <summary>De-duplicates and sorts a define collection.</summary>
        public static string[] Normalize(IEnumerable<string> defines) =>
            (defines ?? Enumerable.Empty<string>())
            .Where(define => !string.IsNullOrWhiteSpace(define))
            .Select(define => define.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(define => define, StringComparer.Ordinal)
            .ToArray();

        /// <summary>
        /// Computes the define set for an environment: start from <paramref name="current"/>,
        /// strip everything owned by any environment in <paramref name="allEnvironments"/>, then
        /// apply the additions and removals of <paramref name="environment"/> and
        /// <paramref name="extraDefines"/>.
        /// </summary>
        public static string[] Compose(
            IEnumerable<string> current,
            BuildEnvironment environment,
            IEnumerable<BuildEnvironment> allEnvironments,
            IEnumerable<string> extraDefines = null)
        {
            var result = new HashSet<string>(
                (current ?? Enumerable.Empty<string>()).Where(define => !string.IsNullOrWhiteSpace(define))
                .Select(define => define.Trim()),
                StringComparer.Ordinal);

            if (allEnvironments != null)
            {
                foreach (var candidate in allEnvironments)
                {
                    if (candidate == null)
                        continue;

                    foreach (var define in candidate.GetAddedDefines())
                        result.Remove(define);
                }
            }

            if (environment != null)
            {
                foreach (var define in environment.GetAddedDefines())
                    result.Add(define);
            }

            if (extraDefines != null)
            {
                foreach (var define in extraDefines)
                {
                    if (!string.IsNullOrWhiteSpace(define))
                        result.Add(define.Trim());
                }
            }

            // Removals run last so an environment can subtract a define a profile added.
            if (environment != null)
            {
                foreach (var define in environment.GetRemovedDefines())
                    result.Remove(define);
            }

            return Normalize(result);
        }
    }
}
