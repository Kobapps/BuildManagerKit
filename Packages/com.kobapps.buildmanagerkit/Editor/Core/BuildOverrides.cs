using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// Per-run overrides applied on top of a profile.
    ///
    /// Every field is optional: null means "use whatever the profile says". This is what makes a
    /// build server able to reach every setting the Editor exposes without editing — and therefore
    /// dirtying — the profile assets. A nightly job can flip on development builds and deep
    /// profiling, a release job can force IL2CPP and an App Bundle, and the profile in version
    /// control stays exactly as the team authored it.
    /// </summary>
    public sealed class BuildOverrides
    {
        // Build options
        public bool? DevelopmentBuild;
        public bool? AutoConnectProfiler;
        public bool? DeepProfiling;
        public bool? ScriptDebugging;
        public bool? StrictMode;
        public bool? CleanBuildCache;
        public bool? DetailedBuildReport;
        public BuildCompression? Compression;

        // Player settings
        public ScriptingImplementation? ScriptingBackend;
        public Il2CppCompilerConfiguration? Il2CppConfiguration;
        public ManagedStrippingLevel? StrippingLevel;

        // Android
        public bool? AndroidAppBundle;
        public bool? AndroidSplitBinary;
        public AndroidArchitecture? AndroidArchitectures;
        public string AndroidKeystorePath;
        public string AndroidKeyaliasName;

        // iOS
        public string AppleTeamId;

        // Target and content
        public StandaloneBuildSubtarget? StandaloneSubtarget;
        public string ExecutableName;
        public string[] Scenes;

        /// <summary>True when at least one override is set.</summary>
        public bool HasAny =>
            DevelopmentBuild.HasValue || AutoConnectProfiler.HasValue || DeepProfiling.HasValue ||
            ScriptDebugging.HasValue || StrictMode.HasValue || CleanBuildCache.HasValue ||
            DetailedBuildReport.HasValue || Compression.HasValue || ScriptingBackend.HasValue ||
            Il2CppConfiguration.HasValue || StrippingLevel.HasValue || AndroidAppBundle.HasValue ||
            AndroidSplitBinary.HasValue || AndroidArchitectures.HasValue ||
            !string.IsNullOrEmpty(AndroidKeystorePath) || !string.IsNullOrEmpty(AndroidKeyaliasName) ||
            !string.IsNullOrEmpty(AppleTeamId) || StandaloneSubtarget.HasValue ||
            !string.IsNullOrEmpty(ExecutableName) || (Scenes != null && Scenes.Length > 0);

        /// <summary>
        /// The overrides in play, for the build log — so a CI run records what it was actually
        /// asked to do rather than just what the profile said.
        /// </summary>
        public string Describe()
        {
            var parts = new List<string>();

            void Add(string name, object value)
            {
                if (value != null)
                    parts.Add($"{name}={value}");
            }

            Add("development", DevelopmentBuild);
            Add("autoConnectProfiler", AutoConnectProfiler);
            Add("deepProfiling", DeepProfiling);
            Add("scriptDebugging", ScriptDebugging);
            Add("strictMode", StrictMode);
            Add("cleanBuildCache", CleanBuildCache);
            Add("detailedReport", DetailedBuildReport);
            Add("compression", Compression);
            Add("scriptingBackend", ScriptingBackend);
            Add("il2cppConfiguration", Il2CppConfiguration);
            Add("strippingLevel", StrippingLevel);
            Add("androidAppBundle", AndroidAppBundle);
            Add("androidSplitBinary", AndroidSplitBinary);
            Add("androidArchitectures", AndroidArchitectures);
            Add("subtarget", StandaloneSubtarget);

            if (!string.IsNullOrEmpty(AndroidKeystorePath))
                parts.Add("keystore=" + AndroidKeystorePath);

            if (!string.IsNullOrEmpty(AndroidKeyaliasName))
                parts.Add("keyalias=" + AndroidKeyaliasName);

            if (!string.IsNullOrEmpty(AppleTeamId))
                parts.Add("appleTeamId=" + AppleTeamId);

            if (!string.IsNullOrEmpty(ExecutableName))
                parts.Add("executable=" + ExecutableName);

            if (Scenes != null && Scenes.Length > 0)
                parts.Add($"scenes={Scenes.Length}");

            return parts.Count == 0 ? "none" : string.Join(", ", parts);
        }

        /// <summary>
        /// Folds these overrides into the <see cref="BuildOptions"/> a profile produced.
        /// </summary>
        /// <param name="options">Flags resolved from the profile.</param>
        /// <param name="developmentBuild">The effective development flag for this run.</param>
        public BuildOptions Apply(BuildOptions options, bool developmentBuild)
        {
            // The profiler and debugging flags are only meaningful on a development player, and
            // Unity ignores (or errors on) them otherwise.
            if (developmentBuild)
            {
                options = Toggle(options, BuildOptions.ConnectWithProfiler, AutoConnectProfiler);
                options = Toggle(options, BuildOptions.EnableDeepProfilingSupport, DeepProfiling);
                options = Toggle(options, BuildOptions.AllowDebugging, ScriptDebugging);
            }
            else
            {
                options &= ~(BuildOptions.ConnectWithProfiler
                             | BuildOptions.EnableDeepProfilingSupport
                             | BuildOptions.AllowDebugging);
            }

            options = Toggle(options, BuildOptions.Development, DevelopmentBuild);
            options = Toggle(options, BuildOptions.StrictMode, StrictMode);
            options = Toggle(options, BuildOptions.CleanBuildCache, CleanBuildCache);
            options = Toggle(options, BuildOptions.DetailedBuildReport, DetailedBuildReport);

            if (Compression.HasValue)
            {
                options &= ~(BuildOptions.CompressWithLz4 | BuildOptions.CompressWithLz4HC);

                switch (Compression.Value)
                {
                    case BuildCompression.Lz4:
                        options |= BuildOptions.CompressWithLz4;
                        break;
                    case BuildCompression.Lz4HC:
                        options |= BuildOptions.CompressWithLz4HC;
                        break;
                }
            }

            return options;
        }

        private static BuildOptions Toggle(BuildOptions options, BuildOptions flag, bool? value)
        {
            if (!value.HasValue)
                return options;

            return value.Value ? options | flag : options & ~flag;
        }
    }
}
