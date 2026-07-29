using System;
using UnityEditor;
using UnityEngine;

namespace BuildManagerKit.Editor
{
    /// <summary>
    /// How a build works out its version string and its numeric build counter.
    ///
    /// Both halves are optional. A project that versions from somewhere else entirely — a release
    /// script, a monorepo tool, Unity Cloud Build — turns <see cref="manageVersion"/> or
    /// <see cref="manageBuildNumber"/> off and the kit leaves those player settings exactly as it
    /// found them, rather than writing a value that looks authoritative and is not.
    ///
    /// The same block is used by the common configuration, by an environment that versions
    /// differently and by a profile that versions differently, so the fields mean the same thing
    /// wherever they are edited. <see cref="ConfigResolver.ResolveVersioning"/> picks which one wins.
    /// </summary>
    [Serializable]
    public sealed class VersioningConfig
    {
        [Tooltip("Let Build Manager Kit set PlayerSettings.bundleVersion. Off leaves the version alone, "
                 + "for projects that stamp it from somewhere else.")]
        public bool manageVersion = true;

        [Tooltip("Where the version string comes from.")]
        public VersionSource source = VersionSource.PlayerSettings;

        [Tooltip("Used when the source is an explicit value.")]
        public string version = "1.0.0";

        [Tooltip("Read the version from a text file, and write increments back to it so a bump survives "
                 + "the build.")]
        public bool useVersionFile;

        [Tooltip("Version file, relative to the project root. Only its first non-empty line is read.")]
        public string versionFilePath = "version.txt";

        [Tooltip("Let Build Manager Kit set the Android versionCode and the iOS/macOS build number.")]
        public bool manageBuildNumber = true;

        [Tooltip("How the build number is produced.")]
        public BuildNumberPolicy buildNumberPolicy = BuildNumberPolicy.AutoIncrementOnSuccess;

        [Tooltip("The stored counter, used by the Manual and Auto Increment policies.")]
        public int buildNumber = 1;

        /// <summary>A block that manages nothing, used when no level of the project claims versioning.</summary>
        public static VersioningConfig Unmanaged =>
            new VersioningConfig { manageVersion = false, manageBuildNumber = false };

        /// <summary>
        /// True when the version is read from (and written back to) <see cref="versionFilePath"/>.
        ///
        /// Also true for the legacy <see cref="VersionSource.VersionFile"/> source, so an asset
        /// authored before the toggle existed keeps working even if it was never migrated.
        /// </summary>
        public bool ReadsVersionFile => useVersionFile || source == VersionSource.VersionFile;

        /// <summary>True when the counter is bumped by the kit after a successful build.</summary>
        public bool IncrementsBuildNumber =>
            manageBuildNumber && buildNumberPolicy == BuildNumberPolicy.AutoIncrementOnSuccess;

        /// <summary>A short human readable summary used by the cards and the CLI description.</summary>
        public string Describe()
        {
            if (!manageVersion && !manageBuildNumber)
                return "not managed";

            var versionPart = !manageVersion
                ? "version not managed"
                : ReadsVersionFile
                    ? $"version from {versionFilePath}"
                    : DescribeSource();

            var numberPart = manageBuildNumber
                ? $"build number {buildNumberPolicy}"
                : "build number not managed";

            return versionPart + " · " + numberPart;
        }

        private string DescribeSource()
        {
            switch (source)
            {
                case VersionSource.Profile: return $"version {version}";
                case VersionSource.GitTag: return "version from the git tag";
                case VersionSource.VersionFile: return $"version from {versionFilePath}";
                default: return "version from PlayerSettings";
            }
        }

        /// <summary>Copies every value out of <paramref name="other"/>, or does nothing when it is null.</summary>
        public void CopyFrom(VersioningConfig other)
        {
            if (other == null)
                return;

            manageVersion = other.manageVersion;
            source = other.source;
            version = other.version;
            useVersionFile = other.useVersionFile;
            versionFilePath = other.versionFilePath;
            manageBuildNumber = other.manageBuildNumber;
            buildNumberPolicy = other.buildNumberPolicy;
            buildNumber = other.buildNumber;
        }
    }

    /// <summary>
    /// The versioning block a run actually uses, together with the asset it came from.
    ///
    /// The owner matters: the auto-increment policy stores its counter, so the bump has to be
    /// written back to whichever asset supplied the block rather than assumed to be the profile.
    /// </summary>
    public readonly struct ResolvedVersioning
    {
        internal ResolvedVersioning(VersioningConfig config, ScriptableObject owner, string ownerLabel)
        {
            Config = config ?? VersioningConfig.Unmanaged;
            Owner = owner;
            OwnerLabel = ownerLabel ?? "nothing";
        }

        /// <summary>The winning versioning block. Never null.</summary>
        public VersioningConfig Config { get; }

        /// <summary>Asset holding <see cref="Config"/>, or null when no level claims versioning.</summary>
        public ScriptableObject Owner { get; }

        /// <summary>Where the block came from, e.g. <c>profile 'android'</c>. Shown in logs and cards.</summary>
        public string OwnerLabel { get; }

        /// <summary>True when some asset claims versioning, so a counter can be written back.</summary>
        public bool IsOwned => Owner != null;

        /// <summary>
        /// Persists the owning asset after its counter changed. An owner that is not an asset — a
        /// block built in memory by a test or a step — is left alone rather than reported as an
        /// invalid save target.
        /// </summary>
        internal void SaveOwner()
        {
            if (Owner == null || !AssetDatabase.Contains(Owner))
                return;

            EditorUtility.SetDirty(Owner);
            AssetDatabase.SaveAssetIfDirty(Owner);
        }
    }
}
