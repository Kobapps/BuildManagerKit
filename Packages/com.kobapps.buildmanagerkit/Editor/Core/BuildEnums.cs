namespace BuildManagerKit.Editor
{
    /// <summary>Tri-state override used where a profile value may be inherited.</summary>
    public enum OptionalBool
    {
        Inherit = 0,
        Enabled = 1,
        Disabled = 2
    }

    /// <summary>Where the scene list for a build comes from.</summary>
    public enum SceneSource
    {
        /// <summary>Use the enabled scenes of File &gt; Build Settings.</summary>
        EditorBuildSettings = 0,

        /// <summary>Use the explicit scene list configured on the profile.</summary>
        Custom = 1
    }

    /// <summary>Player compression applied to the build.</summary>
    public enum BuildCompression
    {
        Default = 0,
        Lz4 = 1,
        Lz4HC = 2
    }

    /// <summary>Where the version string of a build comes from.</summary>
    public enum VersionSource
    {
        /// <summary>Use <c>PlayerSettings.bundleVersion</c> as-is.</summary>
        PlayerSettings = 0,

        /// <summary>Use the explicit version configured on the profile.</summary>
        Profile = 1,

        /// <summary>Read the first line of a text file relative to the project root.</summary>
        VersionFile = 2,

        /// <summary>Use <c>git describe --tags</c> output.</summary>
        GitTag = 3
    }

    /// <summary>How the numeric build number is produced.</summary>
    public enum BuildNumberPolicy
    {
        /// <summary>Never touched by Build Manager Kit.</summary>
        Manual = 0,

        /// <summary>Increment the stored counter after every successful build.</summary>
        AutoIncrementOnSuccess = 1,

        /// <summary>Use the total number of git commits on the current branch.</summary>
        GitCommitCount = 2,

        /// <summary>Use a <c>yyMMddHHmm</c> style timestamp.</summary>
        Timestamp = 3
    }

    /// <summary>Which build phase a step belongs to.</summary>
    public enum BuildPhase
    {
        /// <summary>Nothing is running.</summary>
        Idle = 0,

        /// <summary>Configuration is being resolved and validated.</summary>
        Setup = 1,

        /// <summary>Pre build steps are running.</summary>
        PreBuild = 2,

        /// <summary>Unity's player build is running.</summary>
        Building = 3,

        /// <summary>Post build steps are running.</summary>
        PostBuild = 4,

        /// <summary>The run has finished.</summary>
        Finished = 5
    }

    /// <summary>What happens when a step throws.</summary>
    public enum StepFailurePolicy
    {
        /// <summary>Abort the run and mark the build as failed.</summary>
        FailBuild = 0,

        /// <summary>Log a warning and keep going.</summary>
        WarnAndContinue = 1
    }

    /// <summary>Filters when a post build step is allowed to run.</summary>
    public enum StepRunCondition
    {
        /// <summary>Run whether the player build succeeded or failed.</summary>
        Always = 0,

        /// <summary>Only run when the player build succeeded.</summary>
        OnSuccess = 1,

        /// <summary>Only run when the player build failed or was cancelled.</summary>
        OnFailure = 2
    }

    /// <summary>Severity of a log line emitted during a build run.</summary>
    public enum BuildLogLevel
    {
        Debug = 0,
        Info = 1,
        Success = 2,
        Warning = 3,
        Error = 4
    }

    /// <summary>Final state of a build run.</summary>
    public enum BuildRunStatus
    {
        Unknown = 0,
        Succeeded = 1,
        Failed = 2,
        Cancelled = 3
    }
}
