using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildManagerKit.Editor
{
    /// <summary>How specific the list an action came from is. Higher wins an override contest.</summary>
    public enum BuildStepScopeLevel
    {
        /// <summary>The project-wide list on the settings asset.</summary>
        Global = 0,

        /// <summary>A list on the environment asset.</summary>
        Environment = 1,

        /// <summary>A list on the build profile asset.</summary>
        Profile = 2
    }

    /// <summary>An action paired with the scope it was configured at.</summary>
    public readonly struct ScopedBuildStep
    {
        /// <summary>The action.</summary>
        public BuildStep Step { get; }

        /// <summary>Where it was configured.</summary>
        public BuildStepScopeLevel Level { get; }

        /// <summary>Creates the pair.</summary>
        public ScopedBuildStep(BuildStep step, BuildStepScopeLevel level)
        {
            Step = step;
            Level = level;
        }
    }

    /// <summary>
    /// Collapses the global, environment and profile action lists into the one sequence a run
    /// actually executes.
    ///
    /// Actions with an override <see cref="BuildStep.Key"/> compete: the most specific wins, so a
    /// profile can replace a global action without the global one being edited or duplicated.
    /// Actions without a key never compete and always run.
    /// </summary>
    public static class BuildStepResolver
    {
        /// <summary>
        /// Filters <paramref name="candidates"/> down to the actions that should run, preserving
        /// the order they were supplied in.
        /// </summary>
        /// <param name="candidates">Actions in execution order, each tagged with its scope.</param>
        public static IEnumerable<BuildStep> Resolve(IEnumerable<ScopedBuildStep> candidates)
        {
            var entries = candidates.Where(entry => entry.Step != null).ToList();

            // Work out the winning scope for every contested key first, because the winner may sit
            // later in the execution order than the actions it suppresses.
            var winners = new Dictionary<string, BuildStepScopeLevel>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var key = entry.Step.Key;
                if (key.Length == 0)
                    continue;

                if (!winners.TryGetValue(key, out var best) || entry.Level > best)
                    winners[key] = entry.Level;
            }

            // Only one action per key survives: the first at the winning scope. Two actions sharing
            // a key inside the same list would otherwise both run and make the override meaningless.
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in entries)
            {
                var key = entry.Step.Key;

                if (key.Length == 0)
                {
                    yield return entry.Step;
                    continue;
                }

                if (winners[key] != entry.Level || !used.Add(key))
                    continue;

                yield return entry.Step;
            }
        }

        /// <summary>Tags every action in a list with the scope it came from.</summary>
        public static IEnumerable<ScopedBuildStep> Tag(IEnumerable<BuildStep> steps, BuildStepScopeLevel level) =>
            (steps ?? Enumerable.Empty<BuildStep>())
            .Where(step => step != null)
            .Select(step => new ScopedBuildStep(step, level));

        /// <summary>
        /// Which actions a key suppresses, for explaining an override in the UI.
        /// </summary>
        /// <param name="candidates">The same set passed to <see cref="Resolve"/>.</param>
        /// <returns>Every action that will be skipped because something more specific won.</returns>
        public static IReadOnlyList<ScopedBuildStep> GetSuppressed(IEnumerable<ScopedBuildStep> candidates)
        {
            var entries = candidates.Where(entry => entry.Step != null).ToList();
            var kept = new HashSet<BuildStep>(Resolve(entries));

            return entries.Where(entry => !kept.Contains(entry.Step)).ToList();
        }
    }
}
