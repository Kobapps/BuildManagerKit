using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Project-wide consistency checks.
    ///
    /// These catch the failure modes that only appear once a project is large: two profiles
    /// sharing an id so CI silently builds the wrong one, two environments whose generated defines
    /// collide, several settings assets after a bad merge, or profiles quietly overwriting each
    /// other's output. Every check is cheap and side-effect free, so it is safe to run from the
    /// window, a menu item, or as a pull request gate via <c>BuildCLI.Doctor</c>.
    /// </summary>
    public static class BuildManagerIntegrity
    {
        /// <summary>Runs every check against the project's settings asset.</summary>
        public static BuildValidationReport Check() => Check(BuildManagerSettings.Instance);

        /// <summary>Runs every check against a specific settings asset.</summary>
        /// <param name="settings">Settings to inspect.</param>
        public static BuildValidationReport Check(BuildManagerSettings settings)
        {
            var report = new BuildValidationReport();

            if (settings == null)
            {
                report.AddError("No Build Manager Kit settings asset was found.");
                return report;
            }

            CheckForMultipleSettingsAssets(report);
            CheckNullEntries(settings, report);
            CheckDuplicateIds(settings, report);
            CheckEnvironmentDefines(settings, report);
            CheckOutputCollisions(settings, report);
            CheckConfigAssets(settings, report);
            CheckQueues(settings, report);
            CheckReferences(settings, report);
            CheckLogFolder(settings, report);

            return report;
        }

        private static void CheckForMultipleSettingsAssets(BuildValidationReport report)
        {
            var paths = AssetDatabase.FindAssets("t:" + nameof(BuildManagerSettings))
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (paths.Length <= 1)
                return;

            // Which one wins depends on GUID ordering, so a build machine and a workstation can
            // silently disagree about the whole configuration.
            report.AddError(
                $"{paths.Length} settings assets exist; which one is used is not deterministic. "
                + "Keep exactly one: " + string.Join(", ", paths));
        }

        private static void CheckNullEntries(BuildManagerSettings settings, BuildValidationReport report)
        {
            var missingProfiles = settings.Profiles.Count(profile => profile == null);
            if (missingProfiles > 0)
                report.AddWarning($"{missingProfiles} profile slot(s) point at a deleted asset. Use Rescan to tidy up.");

            var missingEnvironments = settings.Environments.Count(environment => environment == null);
            if (missingEnvironments > 0)
                report.AddWarning(
                    $"{missingEnvironments} environment slot(s) point at a deleted asset. Use Rescan to tidy up.");
        }

        private static void CheckDuplicateIds(BuildManagerSettings settings, BuildValidationReport report)
        {
            foreach (var group in Duplicates(settings.Profiles.Where(p => p != null).Select(p => p.Id)))
                report.AddError(
                    $"{group.Count} profiles share the id '{group.Key}'. The command line resolves ids to a single "
                    + "profile, so -bmkProfile would silently build only one of them.");

            foreach (var group in Duplicates(settings.Environments.Where(e => e != null).Select(e => e.Id)))
                report.AddError(
                    $"{group.Count} environments share the id '{group.Key}'. -bmkEnv would resolve to only one.");

            foreach (var group in Duplicates(settings.Queues.Where(q => q != null).Select(q => q.id)))
                report.AddError($"{group.Count} queues share the id '{group.Key}'.");

            foreach (var profile in settings.Profiles.Where(p => p != null && string.IsNullOrWhiteSpace(p.Id)))
                report.AddWarning($"Profile '{profile.name}' has no id and falls back to its asset name.");

            foreach (var environment in settings.Environments.Where(e => e != null &&
                                                                        string.IsNullOrWhiteSpace(e.Id)))
                report.AddWarning($"Environment '{environment.name}' has no id and falls back to its asset name.");
        }

        private static void CheckEnvironmentDefines(BuildManagerSettings settings, BuildValidationReport report)
        {
            var byDefine = new Dictionary<string, List<BuildEnvironment>>(StringComparer.Ordinal);

            foreach (var environment in settings.Environments.Where(e => e != null))
            {
                var define = environment.EnvironmentDefine;
                if (string.IsNullOrEmpty(define))
                    continue;

                if (!byDefine.TryGetValue(define, out var list))
                    byDefine[define] = list = new List<BuildEnvironment>();

                list.Add(environment);
            }

            // Hand-written defines go straight into PlayerSettings; an illegal symbol there makes
            // the whole project fail to compile as soon as the environment is applied.
            foreach (var environment in settings.Environments.Where(e => e != null))
            {
                foreach (var define in environment.GetAddedDefines()
                             .Concat(environment.GetRemovedDefines())
                             .Where(define => !BuildTokens.IsValidIdentifier(define)))
                    report.AddError(
                        $"Environment '{environment.Id}' declares the define '{define}', which is not a valid "
                        + "scripting symbol. Use letters, digits and underscores only.");
            }

            foreach (var profile in settings.Profiles.Where(p => p != null))
            {
                foreach (var define in profile.ExtraScriptingDefines
                             .Where(define => !BuildTokens.IsValidIdentifier(define)))
                    report.AddError(
                        $"Profile '{profile.Id}' declares the define '{define}', which is not a valid scripting "
                        + "symbol. Use letters, digits and underscores only.");
            }

            foreach (var pair in byDefine.Where(pair => pair.Value.Count > 1))
            {
                // "my env" and "my-env" both sanitise to ENV_MY_ENV, so #if ENV_MY_ENV would be
                // true for both and the two environments become indistinguishable in code.
                report.AddError(
                    $"Environments {string.Join(", ", pair.Value.Select(e => "'" + e.Id + "'"))} all generate the "
                    + $"define {pair.Key}, so runtime code cannot tell them apart.");
            }
        }

        private static void CheckOutputCollisions(BuildManagerSettings settings, BuildValidationReport report)
        {
            var enabled = settings.Profiles.Where(profile => profile != null && profile.Enabled).ToList();
            if (enabled.Count < 2)
                return;

            foreach (var group in enabled
                         .GroupBy(profile => profile.OutputDirectoryTemplate ?? string.Empty, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                var template = group.Key;

                var discriminated =
                    template.IndexOf("{target", StringComparison.OrdinalIgnoreCase) >= 0
                    || template.IndexOf("{platform", StringComparison.OrdinalIgnoreCase) >= 0
                    || template.IndexOf("{profile", StringComparison.OrdinalIgnoreCase) >= 0;

                if (discriminated)
                    continue;

                report.AddError(
                    $"Profiles {string.Join(", ", group.Select(p => "'" + p.Id + "'"))} share the output template "
                    + $"'{template}' with no {{target}}, {{platform}} or {{profile}} token, so they overwrite each "
                    + "other. A queue building them in sequence would keep only the last one.");
            }
        }

        /// <summary>
        /// Config assets are looked up by key at runtime, so a key that exists in one environment
        /// and not another is a null reference that only appears in that flavour's build — the
        /// classic "works in dev, crashes in prod" bug.
        /// </summary>
        private static void CheckConfigAssets(BuildManagerSettings settings, BuildValidationReport report)
        {
            foreach (var environment in settings.Environments.Where(e => e != null))
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in environment.ConfigAssets)
                {
                    if (string.IsNullOrWhiteSpace(entry.key))
                    {
                        report.AddError($"Environment '{environment.Id}' has a config asset with no key.");
                        continue;
                    }

                    if (!seen.Add(entry.key.Trim()))
                        report.AddError(
                            $"Environment '{environment.Id}' declares the config key '{entry.key.Trim()}' twice; "
                            + "only one of them would be published.");

                    if (entry.asset == null)
                        report.AddWarning(
                            $"Environment '{environment.Id}' config key '{entry.key.Trim()}' has no asset assigned "
                            + "and will not be published.");
                }
            }

            foreach (var entry in settings.DefaultConfigAssets)
            {
                if (string.IsNullOrWhiteSpace(entry.key))
                    report.AddError("A default config asset has no key.");
                else if (entry.asset == null)
                    report.AddWarning($"Default config key '{entry.key.Trim()}' has no asset assigned.");
            }

            // Compare the published sets: every environment should offer the same keys, otherwise
            // Get<T> returns null in some flavours and not others.
            var environments = settings.Environments.Where(e => e != null).ToList();
            if (environments.Count < 2)
                return;

            var published = environments.ToDictionary(
                environment => environment,
                environment => new HashSet<string>(
                    EnvironmentAssetsWriter.Resolve(environment, settings).Select(entry => entry.key),
                    StringComparer.OrdinalIgnoreCase));

            var allKeys = new HashSet<string>(published.Values.SelectMany(keys => keys), StringComparer.OrdinalIgnoreCase);

            foreach (var key in allKeys)
            {
                var missing = published.Where(pair => !pair.Value.Contains(key)).Select(pair => pair.Key.Id).ToArray();

                if (missing.Length > 0 && missing.Length < environments.Count)
                    report.AddWarning(
                        $"Config key '{key}' is published by some environments but not by "
                        + $"{string.Join(", ", missing.Select(id => "'" + id + "'"))}. "
                        + "EnvironmentAssets lookups will return null there.");
            }
        }

        private static void CheckQueues(BuildManagerSettings settings, BuildValidationReport report)
        {
            foreach (var queue in settings.Queues.Where(queue => queue != null))
            {
                if (queue.entries == null || queue.entries.Count == 0)
                {
                    report.AddWarning($"Queue '{queue.Title}' has no entries.");
                    continue;
                }

                var broken = queue.entries.Count(entry => entry == null || entry.profile == null);
                if (broken > 0)
                    report.AddWarning($"Queue '{queue.Title}' has {broken} entr(y/ies) with no profile.");

                if (!queue.ActiveEntries.Any())
                    report.AddWarning($"Queue '{queue.Title}' has no enabled entries and would do nothing.");

                foreach (var entry in queue.ActiveEntries.Where(entry =>
                             entry.environmentOverride != null &&
                             !entry.profile.SupportsEnvironment(entry.environmentOverride)))
                    report.AddError(
                        $"Queue '{queue.Title}' pairs profile '{entry.profile.Id}' with environment "
                        + $"'{entry.environmentOverride.Id}', which that profile does not allow.");
            }
        }

        private static void CheckReferences(BuildManagerSettings settings, BuildValidationReport report)
        {
            var registered = new HashSet<BuildEnvironment>(settings.Environments.Where(e => e != null));

            foreach (var profile in settings.Profiles.Where(p => p != null))
            {
                foreach (var allowed in profile.AllowedEnvironments.Where(e => e != null && !registered.Contains(e)))
                    report.AddWarning(
                        $"Profile '{profile.Id}' allows environment '{allowed.Id}', which is not registered in the "
                        + "settings asset. Use Rescan so it appears in the UI and on the command line.");

                if (profile.DefaultEnvironment != null && !profile.SupportsEnvironment(profile.DefaultEnvironment))
                    report.AddError(
                        $"Profile '{profile.Id}' has default environment '{profile.DefaultEnvironment.Id}' but does "
                        + "not allow it, so every build without an explicit -bmkEnv fails.");
            }
        }

        private static void CheckLogFolder(BuildManagerSettings settings, BuildValidationReport report)
        {
            var folder = ProjectPaths.MakeAbsolute(settings.LogFolder);

            if (ProjectPaths.IsSameOrUnder(folder, ProjectPaths.MakeAbsolute("Assets")))
                report.AddError(
                    "The build log folder is inside Assets, so every build would import its own logs as assets. "
                    + "Use a folder outside Assets, such as Logs/BuildManagerKit.");
        }

        private static IEnumerable<(string Key, int Count)> Duplicates(IEnumerable<string> values) =>
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value.Trim(), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => (group.Key, group.Count()));
    }
}
