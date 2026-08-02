using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Command line entry points that read and edit the configuration itself, as opposed to
    /// <see cref="BuildCLI"/>, which runs builds with it.
    ///
    /// These exist so an automated caller — a provisioning script, or an AI agent following the
    /// shipped skill — can manage environments through a validated API instead of editing the
    /// <c>.asset</c> YAML by hand. Hand editing looks easy and silently breaks: the action lists
    /// are <c>[SerializeReference]</c> arrays whose entries are bound by assembly qualified type
    /// name, and a text edit that gets one of those wrong drops the action with no error.
    ///
    /// Every verb prints a machine readable summary and exits with a <see cref="BuildCLI"/> code.
    /// <code>
    /// Unity -batchmode -nographics -quit=false -projectPath . \
    ///       -executeMethod BuildManagerKit.Editor.ConfigCLI.CreateEnvironment \
    ///       -bmkEnv qa -bmkDisplayName "QA" -bmkDefines QA_BUILD -bmkColor "#E0A030"
    /// </code>
    /// </summary>
    public static class ConfigCLI
    {
        /// <summary>
        /// The arguments that write into a <see cref="VersioningConfig"/>. Listed once so
        /// <see cref="SetEnvironment"/> can tell "the caller changed versioning" from "the caller
        /// changed something else", which is what decides whether the override switch is turned on.
        /// </summary>
        private static readonly string[] k_VersioningArguments =
        {
            "bmkManageVersion", "bmkVersionSource", "bmkVersion", "bmkVersionFile", "bmkNoVersionFile",
            "bmkManageBuildNumber", "bmkBuildNumberPolicy", "bmkBuildNumber"
        };

        /// <summary>
        /// Writes the whole configuration as JSON — environments, profiles, queues, config asset
        /// keys and the current health check. This is the read half of the API: a caller runs it
        /// first to learn what exists rather than guessing at ids.
        ///
        /// Arguments: <c>-bmkResultFile</c> (writes the JSON there as well as to the log),
        /// <c>-bmkNoExit</c>.
        /// </summary>
        public static void Describe()
        {
            var arguments = CommandLineArgs.FromProcess();
            var json = JsonUtility.ToJson(BuildDescription(), true);

            var resultFile = arguments.GetString("bmkResultFile");
            if (!string.IsNullOrWhiteSpace(resultFile))
            {
                try
                {
                    var absolute = ProjectPaths.MakeAbsolute(resultFile);
                    ProjectPaths.EnsureDirectory(Path.GetDirectoryName(absolute));
                    File.WriteAllText(absolute, json);
                    Print($"Description written to {absolute}");
                }
                catch (Exception exception)
                {
                    Print("ERROR: could not write the result file: " + exception.Message);
                    Exit(BuildCLI.ExitUsageError, arguments);
                    return;
                }
            }

            Print("BEGIN_BMK_JSON\n" + json + "\nEND_BMK_JSON");
            Exit(BuildCLI.ExitSuccess, arguments);
        }

        /// <summary>
        /// Creates an environment asset and registers it in the settings.
        ///
        /// Arguments: <c>-bmkEnv</c> (id, required), plus every argument
        /// <see cref="SetEnvironment"/> accepts.
        /// </summary>
        public static void CreateEnvironment()
        {
            var arguments = CommandLineArgs.FromProcess();
            var settings = BuildManagerSettings.Instance;
            var id = (arguments.GetString("bmkEnv") ?? string.Empty).Trim();

            if (!ValidateId(id, out var reason))
            {
                Print($"ERROR: -bmkEnv '{id}' is not usable: {reason}");
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            if (settings.FindEnvironment(id) != null)
            {
                Print($"ERROR: an environment with id '{id}' already exists. "
                      + "Use ConfigCLI.SetEnvironment to change it.");
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            var displayName = arguments.GetString("bmkDisplayName", id);
            var color = ParseColor(arguments.GetString("bmkColor"), DefaultColorFor(settings.Environments.Count));

            var environment = BuildManagerBootstrap.CreateEnvironment(
                id, displayName, color, arguments.GetBool("bmkRequireConfirmation"));

            if (!ApplyEnvironmentArguments(environment, arguments, out var applyError))
            {
                Print("ERROR: " + applyError);
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            Print($"Created environment '{id}' at {AssetDatabase.GetAssetPath(environment)}");
            FinishWithHealthCheck(arguments);
        }

        /// <summary>
        /// Edits an existing environment. Only the arguments that are present are changed, so a
        /// caller can adjust one field without restating the rest.
        ///
        /// Arguments: <c>-bmkEnv</c> (required), <c>-bmkDisplayName</c>, <c>-bmkDescription</c>,
        /// <c>-bmkColor</c>, <c>-bmkRequireConfirmation</c>, <c>-bmkDefines</c>,
        /// <c>-bmkRemoveDefines</c>, <c>-bmkGenerateEnvDefine</c>, <c>-bmkProductName</c>,
        /// <c>-bmkCompanyName</c>, <c>-bmkAppIdentifier</c>, <c>-bmkIcon</c>,
        /// <c>-bmkForceDevelopment</c>,
        /// <c>-bmkVars</c> (<c>key=value;key=value</c>), <c>-bmkClearVars</c> and the versioning
        /// arguments listed by <see cref="Help"/>. Use <see cref="SetCommon"/> for the values every
        /// environment shares.
        /// </summary>
        public static void SetEnvironment()
        {
            var arguments = CommandLineArgs.FromProcess();

            if (!Resolve(arguments, out var environment))
                return;

            if (!ApplyEnvironmentArguments(environment, arguments, out var error))
            {
                Print("ERROR: " + error);
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            Print($"Updated environment '{environment.Id}'.");
            FinishWithHealthCheck(arguments);
        }

        /// <summary>
        /// Deletes an environment asset and unregisters it.
        ///
        /// Arguments: <c>-bmkEnv</c> (required), <c>-bmkNoExit</c>.
        /// </summary>
        public static void DeleteEnvironment()
        {
            var arguments = CommandLineArgs.FromProcess();

            if (!Resolve(arguments, out var environment))
                return;

            var settings = BuildManagerSettings.Instance;

            // Refuse rather than silently leave the project without an active environment.
            if (settings.ActiveEnvironment == environment && settings.Environments.Count(e => e != null) <= 1)
            {
                Print($"ERROR: '{environment.Id}' is the only environment and is active. "
                      + "Create another one before deleting it.");
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            var path = AssetDatabase.GetAssetPath(environment);
            var id = environment.Id;

            // The shared implementation, so a headless delete cleans up the same references the
            // window's Delete does: queue defaults and overrides, and the profiles that allowed it.
            BuildManagerBootstrap.DeleteEnvironment(settings, environment);

            Print($"Deleted environment '{id}' ({path}).");
            FinishWithHealthCheck(arguments);
        }

        /// <summary>
        /// Publishes an asset under a key on an environment, so shipped code can read it through
        /// <c>EnvironmentAssets.Current.Get&lt;T&gt;(key)</c>. Replaces the entry when the key is
        /// already published.
        ///
        /// Arguments: <c>-bmkEnv</c> (required, or <c>-bmkDefaultConfig</c> for the project-wide
        /// default list), <c>-bmkKey</c> (required), <c>-bmkAsset</c> (asset path, required).
        /// </summary>
        public static void SetConfigAsset()
        {
            var arguments = CommandLineArgs.FromProcess();
            var key = (arguments.GetString("bmkKey") ?? string.Empty).Trim();
            var assetPath = arguments.GetString("bmkAsset");

            if (string.IsNullOrWhiteSpace(key))
            {
                Print("ERROR: -bmkKey is required.");
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                Print("ERROR: -bmkAsset is required.");
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
            {
                Print($"ERROR: no asset at '{assetPath}'. The path is relative to the project root "
                      + "and starts with 'Assets/'.");
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            if (arguments.GetBool("bmkDefaultConfig"))
            {
                var settings = BuildManagerSettings.Instance;
                WriteConfigEntry(new SerializedObject(settings), "m_DefaultConfigAssets", key, asset);
                Print($"Published '{key}' -> {assetPath} as a project-wide default.");
            }
            else
            {
                if (!Resolve(arguments, out var environment))
                    return;

                WriteConfigEntry(new SerializedObject(environment), "m_ConfigAssets", key, asset);
                Print($"Published '{key}' -> {assetPath} on environment '{environment.Id}'.");
            }

            FinishWithHealthCheck(arguments);
        }

        /// <summary>
        /// Removes a published key from an environment, or from the project-wide defaults.
        ///
        /// Arguments: <c>-bmkEnv</c> (or <c>-bmkDefaultConfig</c>), <c>-bmkKey</c> (required).
        /// </summary>
        public static void RemoveConfigAsset()
        {
            var arguments = CommandLineArgs.FromProcess();
            var key = (arguments.GetString("bmkKey") ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(key))
            {
                Print("ERROR: -bmkKey is required.");
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            bool removed;

            if (arguments.GetBool("bmkDefaultConfig"))
            {
                removed = RemoveConfigEntry(new SerializedObject(BuildManagerSettings.Instance),
                    "m_DefaultConfigAssets", key);
                Print(removed
                    ? $"Removed default config key '{key}'."
                    : $"Default config key '{key}' was not published; nothing to do.");
            }
            else
            {
                if (!Resolve(arguments, out var environment))
                    return;

                removed = RemoveConfigEntry(new SerializedObject(environment), "m_ConfigAssets", key);
                Print(removed
                    ? $"Removed config key '{key}' from '{environment.Id}'."
                    : $"Config key '{key}' was not published by '{environment.Id}'; nothing to do.");
            }

            FinishWithHealthCheck(arguments);
        }

        /// <summary>Prints the argument reference for every verb in this class.</summary>
        public static void Help()
        {
            Print(@"
Build Manager Kit — configuration command line

  -executeMethod BuildManagerKit.Editor.ConfigCLI.Describe
      -bmkResultFile <path>   Also write the JSON description to this file
      Prints every environment, profile, queue and config key as JSON between
      BEGIN_BMK_JSON / END_BMK_JSON markers, including the common configuration
      and the versioning each level resolves to. Run this before editing anything.

  -executeMethod BuildManagerKit.Editor.ConfigCLI.CreateEnvironment
      -bmkEnv <id>                    Stable id, lower case identifier    (required)
      -bmkDisplayName <name>          Name shown in the UI
      -bmkColor <#RRGGBB>             Accent colour
      -bmkRequireConfirmation <bool>  Ask before activating or building
      ... plus every SetEnvironment argument below.

  -executeMethod BuildManagerKit.Editor.ConfigCLI.SetEnvironment
      -bmkEnv <id>                    Environment to edit                 (required)
      -bmkDisplayName <name>
      -bmkDescription <text>
      -bmkColor <#RRGGBB>
      -bmkRequireConfirmation <bool>
      -bmkDefines <A;B>               Defines added while active (replaces the list)
      -bmkRemoveDefines <A;B>         Defines stripped while active
      -bmkGenerateEnvDefine <bool>    Auto add ENV_<ID>
      -bmkProductName <name>          Empty string takes the common value instead
      -bmkCompanyName <name>
      -bmkAppIdentifier <id>          e.g. com.studio.game.dev
      -bmkIcon <Assets/path.png>      Application icon while active; "" takes the common icon
      -bmkForceDevelopment <Inherit|Enabled|Disabled>
      -bmkVars <k=v;k=v>              Runtime variables, merged by key
      -bmkClearVars                   Drop existing variables before merging
      -bmkOverrideVersioning <bool>   Version this environment differently from the common
                                      configuration (false hands versioning back to it)
      ... plus every versioning argument listed under SetCommon below.
      Only the arguments you pass are changed. Any versioning argument turns the
      override on unless -bmkOverrideVersioning false is passed as well.

  -executeMethod BuildManagerKit.Editor.ConfigCLI.SetCommon
      The values every environment starts from and overrides only where it differs.
      -bmkProductName <name>          Empty string stops managing it at all
      -bmkCompanyName <name>
      -bmkAppIdentifier <id>          e.g. com.studio.game
      -bmkIcon <Assets/path.png>      Shared application icon; "" clears it
      -bmkForceDevelopment <Inherit|Enabled|Disabled>
      -bmkVars <k=v;k=v>              Shared runtime variables, merged by key
      -bmkClearVars                   Drop existing shared variables before merging
      -bmkManageVersion <bool>        Let the kit write PlayerSettings.bundleVersion
      -bmkVersionSource <PlayerSettings|Profile|GitTag>
      -bmkVersion <1.4.2>             Explicit version (implies Profile source)
      -bmkVersionFile <version.txt>   Read the version from a file; "" switches it off
      -bmkNoVersionFile               Switch the version file off, keeping its path
      -bmkManageBuildNumber <bool>    Let the kit write the Android/iOS build number
      -bmkBuildNumberPolicy <Manual|AutoIncrementOnSuccess|GitCommitCount|Timestamp>
      -bmkBuildNumber <int>           The stored counter
      Only the arguments you pass are changed.

  -executeMethod BuildManagerKit.Editor.ConfigCLI.DeleteEnvironment  -bmkEnv <id>

  -executeMethod BuildManagerKit.Editor.ConfigCLI.SetConfigAsset
      -bmkEnv <id>                    Environment to publish on
      -bmkDefaultConfig               ...or publish as a project-wide default instead
      -bmkKey <key>                   Lookup key                          (required)
      -bmkAsset <Assets/path.asset>   Asset to publish                    (required)

  -executeMethod BuildManagerKit.Editor.ConfigCLI.RemoveConfigAsset
      -bmkEnv <id> | -bmkDefaultConfig,  -bmkKey <key>

  Every verb accepts -bmkNoExit and runs the project health check when it finishes,
  so a mistake is reported immediately rather than at the next build.

Exit codes: 0 success · 1 health check failed · 2 usage error");

            Exit(BuildCLI.ExitSuccess, CommandLineArgs.FromProcess());
        }

        /// <summary>
        /// Applies every environment field present in <paramref name="arguments"/>. Split out so
        /// both <see cref="CreateEnvironment"/> and <see cref="SetEnvironment"/> accept exactly
        /// the same vocabulary.
        /// </summary>
        /// <param name="environment">Environment to edit.</param>
        /// <param name="arguments">Parsed command line.</param>
        /// <param name="error">Set when an argument could not be applied.</param>
        internal static bool ApplyEnvironmentArguments(
            BuildEnvironment environment,
            CommandLineArgs arguments,
            out string error)
        {
            error = null;

            var serialized = new SerializedObject(environment);

            if (arguments.Has("bmkDisplayName"))
                serialized.FindProperty("m_DisplayName").stringValue = arguments.GetString("bmkDisplayName", string.Empty);

            if (arguments.Has("bmkDescription"))
                serialized.FindProperty("m_Description").stringValue = arguments.GetString("bmkDescription", string.Empty);

            if (arguments.Has("bmkColor"))
            {
                var raw = arguments.GetString("bmkColor");
                if (!TryParseColor(raw, out var color))
                {
                    error = $"-bmkColor '{raw}' is not an #RRGGBB colour.";
                    return false;
                }

                serialized.FindProperty("m_Color").colorValue = color;
            }

            if (arguments.Has("bmkRequireConfirmation"))
                serialized.FindProperty("m_RequireConfirmation").boolValue = arguments.GetBool("bmkRequireConfirmation");

            if (arguments.Has("bmkGenerateEnvDefine"))
                serialized.FindProperty("m_GenerateEnvironmentDefine").boolValue =
                    arguments.GetBool("bmkGenerateEnvDefine");

            if (arguments.Has("bmkDefines")
                && !WriteDefines(serialized, "m_ScriptingDefines", arguments.GetList("bmkDefines"), out error))
                return false;

            if (arguments.Has("bmkRemoveDefines")
                && !WriteDefines(serialized, "m_RemovedScriptingDefines", arguments.GetList("bmkRemoveDefines"), out error))
                return false;

            WriteOptionalOverride(arguments, "bmkProductName", serialized.FindProperty("m_ProductName"));
            WriteOptionalOverride(arguments, "bmkCompanyName", serialized.FindProperty("m_CompanyName"));
            WriteOptionalOverride(arguments, "bmkAppIdentifier",
                serialized.FindProperty("m_ApplicationIdentifier"));

            if (arguments.Has("bmkIcon") &&
                !WriteIcon(serialized.FindProperty("m_ApplicationIcon"),
                    arguments.GetString("bmkIcon", string.Empty), out error))
                return false;

            if (arguments.Has("bmkForceDevelopment"))
            {
                var raw = arguments.GetString("bmkForceDevelopment");
                if (!Enum.TryParse<OptionalBool>(raw, true, out var value))
                {
                    error = $"-bmkForceDevelopment '{raw}' must be Inherit, Enabled or Disabled.";
                    return false;
                }

                serialized.FindProperty("m_ForceDevelopmentBuild").intValue = (int)value;
            }

            if ((arguments.Has("bmkClearVars") || arguments.Has("bmkVars")) &&
                !WriteVariables(serialized.FindProperty("m_Variables"), arguments, out error))
                return false;

            if (!WriteVersioning(serialized, arguments, out error))
                return false;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(environment);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// Edits the project's common configuration — the values every environment starts from.
        ///
        /// Arguments: <c>-bmkProductName</c>, <c>-bmkCompanyName</c>, <c>-bmkAppIdentifier</c>,
        /// <c>-bmkIcon</c>, <c>-bmkForceDevelopment</c>, <c>-bmkVars</c>, <c>-bmkClearVars</c> and
        /// every versioning argument. Only the arguments present are changed, so a caller can adjust
        /// the version without restating the company name.
        /// </summary>
        public static void SetCommon()
        {
            var arguments = CommandLineArgs.FromProcess();
            var settings = BuildManagerSettings.Instance;
            var serialized = new SerializedObject(settings);
            var common = serialized.FindProperty("m_Common");

            WriteOptionalOverride(arguments, "bmkProductName", common.FindPropertyRelative("productName"));
            WriteOptionalOverride(arguments, "bmkCompanyName", common.FindPropertyRelative("companyName"));
            WriteOptionalOverride(arguments, "bmkAppIdentifier",
                common.FindPropertyRelative("applicationIdentifier"));

            if (arguments.Has("bmkIcon") &&
                !WriteIcon(common.FindPropertyRelative("applicationIcon"),
                    arguments.GetString("bmkIcon", string.Empty), out var iconError))
            {
                Print("ERROR: " + iconError);
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            if (arguments.Has("bmkForceDevelopment"))
            {
                var raw = arguments.GetString("bmkForceDevelopment");
                if (!Enum.TryParse<OptionalBool>(raw, true, out var value))
                {
                    Print($"ERROR: -bmkForceDevelopment '{raw}' must be Inherit, Enabled or Disabled.");
                    Exit(BuildCLI.ExitUsageError, arguments);
                    return;
                }

                common.FindPropertyRelative("forceDevelopmentBuild").intValue = (int)value;
            }

            if ((arguments.Has("bmkClearVars") || arguments.Has("bmkVars")) &&
                !WriteVariables(common.FindPropertyRelative("variables"), arguments, out var variablesError))
            {
                Print("ERROR: " + variablesError);
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            if (!WriteVersioningBlock(common.FindPropertyRelative("versioning"), arguments, out var versioningError))
            {
                Print("ERROR: " + versioningError);
                Exit(BuildCLI.ExitUsageError, arguments);
                return;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            settings.Save();
            AssetDatabase.SaveAssets();

            Print("Updated the common configuration: " + settings.Common.Describe());
            FinishWithHealthCheck(arguments);
        }

        /// <summary>
        /// Applies the versioning arguments to an environment. Passing any of them switches the
        /// environment's versioning override on, since a value nobody reads is not what the caller
        /// asked for; <c>-bmkOverrideVersioning false</c> is how it is switched back off — versioning
        /// then comes from the common configuration again.
        /// </summary>
        private static bool WriteVersioning(SerializedObject serialized, CommandLineArgs arguments, out string error)
        {
            error = null;

            var touched = k_VersioningArguments.Any(arguments.Has);
            if (!touched && !arguments.Has("bmkOverrideVersioning"))
                return true;

            var overrideProperty = serialized.FindProperty("m_OverrideVersioning");

            overrideProperty.boolValue = !arguments.Has("bmkOverrideVersioning")
                                         || arguments.GetBool("bmkOverrideVersioning");

            return WriteVersioningBlock(serialized.FindProperty("m_Versioning"), arguments, out error);
        }

        /// <summary>
        /// Writes the versioning arguments into one <see cref="VersioningConfig"/> property, wherever
        /// it lives — an environment, or the common configuration.
        /// </summary>
        private static bool WriteVersioningBlock(
            SerializedProperty versioning,
            CommandLineArgs arguments,
            out string error)
        {
            error = null;

            if (versioning == null || !k_VersioningArguments.Any(arguments.Has))
                return true;

            if (arguments.Has("bmkManageVersion"))
                versioning.FindPropertyRelative("manageVersion").boolValue = arguments.GetBool("bmkManageVersion");

            if (arguments.Has("bmkVersionSource"))
            {
                var raw = arguments.GetString("bmkVersionSource");

                // VersionFile parses — it is the legacy source value — but it is the -bmkVersionFile
                // toggle now, and accepting it here would produce a state the window cannot show.
                if (!Enum.TryParse<VersionSource>(raw, true, out var source) ||
                    source == VersionSource.VersionFile)
                {
                    error = $"-bmkVersionSource '{raw}' must be PlayerSettings, Profile (an explicit value) "
                            + "or GitTag. Use -bmkVersionFile <path> for a version file.";
                    return false;
                }

                versioning.FindPropertyRelative("source").intValue = (int)source;
            }

            if (arguments.Has("bmkVersion"))
            {
                var value = arguments.GetString("bmkVersion", string.Empty);
                versioning.FindPropertyRelative("version").stringValue = value;
                versioning.FindPropertyRelative("source").intValue = (int)VersionSource.Profile;
                versioning.FindPropertyRelative("manageVersion").boolValue = true;
            }

            if (arguments.Has("bmkVersionFile"))
            {
                var path = arguments.GetString("bmkVersionFile", string.Empty);
                var enabled = !string.IsNullOrWhiteSpace(path);

                versioning.FindPropertyRelative("useVersionFile").boolValue = enabled;
                versioning.FindPropertyRelative("manageVersion").boolValue = true;

                if (enabled)
                    versioning.FindPropertyRelative("versionFilePath").stringValue = path.Trim();
            }

            if (arguments.Has("bmkNoVersionFile"))
                versioning.FindPropertyRelative("useVersionFile").boolValue = false;

            if (arguments.Has("bmkManageBuildNumber"))
                versioning.FindPropertyRelative("manageBuildNumber").boolValue =
                    arguments.GetBool("bmkManageBuildNumber");

            if (arguments.Has("bmkBuildNumberPolicy"))
            {
                var raw = arguments.GetString("bmkBuildNumberPolicy");
                if (!Enum.TryParse<BuildNumberPolicy>(raw, true, out var policy))
                {
                    error = $"-bmkBuildNumberPolicy '{raw}' must be Manual, AutoIncrementOnSuccess, "
                            + "GitCommitCount or Timestamp.";
                    return false;
                }

                versioning.FindPropertyRelative("buildNumberPolicy").intValue = (int)policy;
                versioning.FindPropertyRelative("manageBuildNumber").boolValue = true;
            }

            if (arguments.Has("bmkBuildNumber"))
            {
                var raw = arguments.GetString("bmkBuildNumber");
                if (!int.TryParse(raw, out var number) || number < 0)
                {
                    error = $"-bmkBuildNumber '{raw}' must be a non-negative whole number.";
                    return false;
                }

                versioning.FindPropertyRelative("buildNumber").intValue = number;
                versioning.FindPropertyRelative("manageBuildNumber").boolValue = true;
            }

            return true;
        }

        /// <summary>
        /// Rejects ids that would produce a broken project: an id becomes an <c>ENV_&lt;ID&gt;</c>
        /// preprocessor symbol and part of a file name, so it has to survive both.
        /// </summary>
        /// <param name="id">Candidate id.</param>
        /// <param name="reason">Set when the id is rejected.</param>
        internal static bool ValidateId(string id, out string reason)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                reason = "it is empty";
                return false;
            }

            if (id.Trim() != id)
            {
                reason = "it has leading or trailing whitespace";
                return false;
            }

            var sanitized = BuildTokens.SanitizeIdentifier(id);
            if (!string.Equals(sanitized, id, StringComparison.Ordinal))
            {
                reason = $"it is not a plain identifier — '{sanitized}' would be used instead, "
                         + "which stops -bmkEnv and the generated define from agreeing";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>Builds the serialisable snapshot returned by <see cref="Describe"/>.</summary>
        internal static ProjectDescription BuildDescription()
        {
            var settings = BuildManagerSettings.Instance;
            var report = BuildManagerIntegrity.Check(settings);

            var description = new ProjectDescription
            {
                settingsAssetPath = AssetDatabase.GetAssetPath(settings),
                activeEnvironment = settings.ActiveEnvironment != null ? settings.ActiveEnvironment.Id : string.Empty,
                common = settings.Common.Describe(),
                commonVersioning = settings.Common.versioning != null
                    ? settings.Common.versioning.Describe()
                    : "not managed",
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                healthy = !report.HasErrors,
                healthIssues = report.Issues.Select(issue => issue.ToString()).ToArray(),
                defaultConfigKeys = settings.DefaultConfigAssets.Select(entry => entry.key).ToArray()
            };

            foreach (var environment in settings.GetSortedEnvironments())
            {
                // Resolved rather than declared: an agent editing one environment needs to see the
                // values a build would actually use, inheritance included.
                var versioning = ConfigResolver.ResolveVersioning(settings, environment, null);

                description.environments.Add(new EnvironmentDescription
                {
                    id = environment.Id,
                    displayName = environment.DisplayName,
                    description = environment.Description,
                    assetPath = AssetDatabase.GetAssetPath(environment),
                    active = settings.ActiveEnvironment == environment,
                    requireConfirmation = environment.RequireConfirmation,
                    addedDefines = environment.GetAddedDefines().ToArray(),
                    removedDefines = environment.GetRemovedDefines().ToArray(),
                    productNameOverride = ConfigResolver.ResolveProductName(settings, environment) ?? string.Empty,
                    companyNameOverride = ConfigResolver.ResolveCompanyName(settings, environment) ?? string.Empty,
                    applicationIdentifierOverride =
                        ConfigResolver.ResolveApplicationIdentifier(settings, environment) ?? string.Empty,
                    applicationIconOverride = ConfigResolver.ResolveApplicationIcon(settings, environment) != null
                        ? AssetDatabase.GetAssetPath(ConfigResolver.ResolveApplicationIcon(settings, environment))
                        : string.Empty,
                    overridesVersioning = environment.OverridesVersioning,
                    versioning = versioning.IsOwned
                        ? $"{versioning.Config.Describe()} (from {versioning.OwnerLabel})"
                        : "not managed",
                    variables = ConfigResolver.ResolveVariables(settings, environment)
                        .Select(variable => variable.key + "=" + variable.value).ToArray(),
                    configKeys = environment.ConfigAssets.Select(entry => entry.key).ToArray(),
                    configs = environment.Configs
                        .Where(config => config != null)
                        .Select(config => $"{config.ConfigKey}={config.GetType().Name} ({AssetDatabase.GetAssetPath(config)})")
                        .ToArray(),
                    actionCounts = $"onActivate={environment.OnActivateSteps.Count}, "
                                   + $"preBuild={environment.PreBuildSteps.Count}, "
                                   + $"postBuild={environment.PostBuildSteps.Count}"
                });
            }

            foreach (var profile in settings.Profiles.Where(profile => profile != null))
            {
                var versioning = ConfigResolver.ResolveVersioning(settings, settings.ActiveEnvironment, profile);

                description.profiles.Add(new ProfileDescription
                {
                    id = profile.Id,
                    displayName = profile.DisplayName,
                    assetPath = AssetDatabase.GetAssetPath(profile),
                    target = profile.Target.ToString(),
                    enabled = profile.Enabled,
                    defaultEnvironment = profile.DefaultEnvironment != null ? profile.DefaultEnvironment.Id : string.Empty,
                    overridesVersioning = profile.OverridesVersioning,
                    versioning = versioning.IsOwned
                        ? $"{versioning.Config.Describe()} (from {versioning.OwnerLabel})"
                        : "not managed"
                });
            }

            foreach (var queue in settings.Queues.Where(queue => queue != null))
            {
                description.queues.Add(new QueueDescription
                {
                    id = queue.id,
                    displayName = queue.Title,
                    entryCount = queue.ActiveEntries.Count()
                });
            }

            return description;
        }

        private static bool Resolve(CommandLineArgs arguments, out BuildEnvironment environment)
        {
            var settings = BuildManagerSettings.Instance;
            var id = arguments.GetString("bmkEnv");
            environment = string.IsNullOrWhiteSpace(id) ? null : settings.FindEnvironment(id);

            if (environment != null)
                return true;

            Print(string.IsNullOrWhiteSpace(id)
                ? "ERROR: -bmkEnv is required."
                : $"ERROR: no environment named '{id}'. Known environments: "
                  + string.Join(", ", settings.Environments.Where(e => e != null).Select(e => e.Id)));

            Exit(BuildCLI.ExitUsageError, arguments);
            return false;
        }

        private static bool WriteDefines(
            SerializedObject serialized,
            string propertyName,
            IReadOnlyList<string> defines,
            out string error)
        {
            foreach (var define in defines)
            {
                if (!BuildTokens.IsValidIdentifier(define))
                {
                    error = $"'{define}' is not a valid scripting define; use letters, digits and "
                            + "underscores, not starting with a digit.";
                    return false;
                }
            }

            var array = serialized.FindProperty(propertyName);
            array.arraySize = defines.Count;
            for (var i = 0; i < defines.Count; i++)
                array.GetArrayElementAtIndex(i).stringValue = defines[i];

            error = null;
            return true;
        }

        /// <summary>
        /// Merges <c>-bmkVars</c> into a variable list, wherever it lives — an environment's own list,
        /// or the shared list in the common configuration.
        /// </summary>
        /// <param name="array">The variable array, on an environment or the common configuration.</param>
        /// <param name="arguments">Parsed command line.</param>
        /// <param name="error">Set when an entry is not in <c>key=value</c> form.</param>
        private static bool WriteVariables(SerializedProperty array, CommandLineArgs arguments, out string error)
        {
            if (arguments.GetBool("bmkClearVars"))
                array.arraySize = 0;

            foreach (var pair in arguments.GetList("bmkVars"))
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0)
                {
                    error = $"-bmkVars entry '{pair}' is not in key=value form.";
                    return false;
                }

                var key = pair.Substring(0, separator).Trim();
                var value = pair.Substring(separator + 1).Trim();
                var index = IndexOfVariable(array, key);

                if (index < 0)
                {
                    index = array.arraySize;
                    array.arraySize = index + 1;
                    array.GetArrayElementAtIndex(index).FindPropertyRelative("key").stringValue = key;
                }

                array.GetArrayElementAtIndex(index).FindPropertyRelative("value").stringValue = value;
            }

            error = null;
            return true;
        }

        private static int IndexOfVariable(SerializedProperty array, string key)
        {
            for (var i = 0; i < array.arraySize; i++)
            {
                var candidate = array.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue;
                if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// Points the environment's application icon override at an asset, or clears it when
        /// <paramref name="assetPath"/> is empty.
        ///
        /// A badged or tinted icon per environment is the cheapest way to stop a tester filing a
        /// bug against the wrong build. The icon is applied on activation and on build and
        /// restored with the rest of the player settings afterwards, so it never leaks into a
        /// production player.
        /// </summary>
        /// <param name="reference">The texture reference, on an environment or the common configuration.</param>
        /// <param name="assetPath">Texture asset path, or empty to clear it.</param>
        /// <param name="error">Set when the asset is missing or not a texture.</param>
        private static bool WriteIcon(SerializedProperty reference, string assetPath, out string error)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                reference.objectReferenceValue = null;
                error = null;
                return true;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);

            if (texture == null)
            {
                // A sprite-mode import produces a Sprite, not a Texture2D, and is the usual cause.
                error = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null
                    ? $"-bmkIcon '{assetPath}' is not a Texture2D. Set its importer Texture Type to "
                      + "'Default' rather than 'Sprite'."
                    : $"-bmkIcon '{assetPath}' does not exist. The path starts with 'Assets/'.";

                return false;
            }

            reference.objectReferenceValue = texture;
            error = null;
            return true;
        }

        /// <summary>
        /// Writes a "boolean plus value" override pair. An empty value clears the override, which
        /// is the only way to undo one from the command line.
        /// </summary>
        /// <param name="arguments">Parsed command line.</param>
        /// <param name="argument">Command line name to read.</param>
        /// <param name="value">The field to write, on an environment or the common configuration.</param>
        private static void WriteOptionalOverride(
            CommandLineArgs arguments,
            string argument,
            SerializedProperty value)
        {
            if (!arguments.Has(argument))
                return;

            value.stringValue = arguments.GetString(argument, string.Empty);
        }

        private static void WriteConfigEntry(
            SerializedObject serialized,
            string propertyName,
            string key,
            UnityEngine.Object asset)
        {
            var array = serialized.FindProperty(propertyName);
            var index = -1;

            for (var i = 0; i < array.arraySize; i++)
            {
                var candidate = array.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue;
                if (string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                index = array.arraySize;
                array.arraySize = index + 1;
            }

            var entry = array.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("key").stringValue = key;
            entry.FindPropertyRelative("asset").objectReferenceValue = asset;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(serialized.targetObject);
            AssetDatabase.SaveAssets();
        }

        private static bool RemoveConfigEntry(SerializedObject serialized, string propertyName, string key)
        {
            var array = serialized.FindProperty(propertyName);

            for (var i = 0; i < array.arraySize; i++)
            {
                var candidate = array.GetArrayElementAtIndex(i).FindPropertyRelative("key").stringValue;
                if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase))
                    continue;

                array.DeleteArrayElementAtIndex(i);
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(serialized.targetObject);
                AssetDatabase.SaveAssets();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Runs the health check after a mutation so a caller learns immediately that it produced
        /// a duplicate id or a define clash, rather than at the next build.
        /// </summary>
        private static void FinishWithHealthCheck(CommandLineArgs arguments)
        {
            var report = BuildManagerIntegrity.Check();

            foreach (var issue in report.Issues)
                Print($"{(issue.IsError ? "ERROR  " : "WARNING")} {issue}");

            if (report.HasErrors)
            {
                Print($"Health check failed with {report.ErrorCount} error(s). The change was saved — "
                      + "fix the reported problems before building.");
                Exit(BuildCLI.ExitBuildFailed, arguments);
                return;
            }

            Print("Health check passed.");
            Exit(BuildCLI.ExitSuccess, arguments);
        }

        private static Color DefaultColorFor(int index)
        {
            // Spread new environments around the hue wheel so they stay visually distinct.
            return Color.HSVToRGB((0.08f + index * 0.17f) % 1f, 0.62f, 0.94f);
        }

        private static Color ParseColor(string raw, Color fallback) =>
            TryParseColor(raw, out var color) ? color : fallback;

        internal static bool TryParseColor(string raw, out Color color)
        {
            color = Color.white;

            if (string.IsNullOrWhiteSpace(raw))
                return false;

            var text = raw.Trim();
            if (!text.StartsWith("#", StringComparison.Ordinal))
                text = "#" + text;

            // ColorUtility handles #RGB, #RRGGBB and #RRGGBBAA and rejects everything else.
            return ColorUtility.TryParseHtmlString(text, out color);
        }

        private static void Exit(int code, CommandLineArgs arguments)
        {
            if (arguments.GetBool("bmkNoExit"))
            {
                Print($"Exit code would be {code} (-bmkNoExit was passed).");
                return;
            }

            if (!Application.isBatchMode)
            {
                Print($"Exit code would be {code} (not running in batch mode, so the Editor stays open).");
                return;
            }

            EditorApplication.Exit(code);
        }

        private static void Print(string message)
        {
            Console.WriteLine("[BuildManagerKit] " + message);
            Debug.Log("[BuildManagerKit] " + message);
        }

        /// <summary>JSON shape returned by <see cref="Describe"/>.</summary>
        [Serializable]
        internal sealed class ProjectDescription
        {
            public string settingsAssetPath;
            public string activeEnvironment;

            /// <summary>The values every environment starts from.</summary>
            public string common;

            public string commonVersioning;
            public string activeBuildTarget;
            public bool healthy;
            public string[] healthIssues = Array.Empty<string>();
            public string[] defaultConfigKeys = Array.Empty<string>();
            public List<EnvironmentDescription> environments = new List<EnvironmentDescription>();
            public List<ProfileDescription> profiles = new List<ProfileDescription>();
            public List<QueueDescription> queues = new List<QueueDescription>();
        }

        /// <summary>One environment inside <see cref="ProjectDescription"/>.</summary>
        [Serializable]
        internal sealed class EnvironmentDescription
        {
            public string id;
            public string displayName;
            public string description;
            public string assetPath;
            public bool active;
            public bool requireConfirmation;
            public string[] addedDefines = Array.Empty<string>();
            public string[] removedDefines = Array.Empty<string>();

            /// <summary>The values in effect, which for a non-base environment may be inherited.</summary>
            public string productNameOverride;

            public string companyNameOverride;
            public string applicationIdentifierOverride;
            public string applicationIconOverride;
            public bool overridesVersioning;

            /// <summary>The versioning a build of this environment would use, and where it comes from.</summary>
            public string versioning;

            /// <summary>Resolved variables: the base environment's with this environment's on top.</summary>
            public string[] variables = Array.Empty<string>();

            public string[] configKeys = Array.Empty<string>();

            /// <summary>
            /// Typed configs this environment publishes, as <c>key=Type (path)</c>. An agent reading
            /// this needs the path: sharing a config with another environment means referencing the
            /// same asset, not creating a second one.
            /// </summary>
            public string[] configs = Array.Empty<string>();

            public string actionCounts;
        }

        /// <summary>One profile inside <see cref="ProjectDescription"/>.</summary>
        [Serializable]
        internal sealed class ProfileDescription
        {
            public string id;
            public string displayName;
            public string assetPath;
            public string target;
            public bool enabled;
            public string defaultEnvironment;
            public bool overridesVersioning;
            public string versioning;
        }

        /// <summary>One queue inside <see cref="ProjectDescription"/>.</summary>
        [Serializable]
        internal sealed class QueueDescription
        {
            public string id;
            public string displayName;
            public int entryCount;
        }
    }
}
