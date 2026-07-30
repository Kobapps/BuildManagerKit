# Command line reference

Two classes. `ConfigCLI` reads and edits the configuration; `BuildCLI` runs builds with it.

Both live in `BuildManagerKit.Editor` and are invoked as `-executeMethod`:

```bash
"$UNITY" -batchmode -nographics -quit=false -projectPath "$PROJECT" \
         -executeMethod BuildManagerKit.Editor.BuildCLI.Build \
         -bmkProfile android -bmkEnv prod -logFile -
```

Argument spellings are interchangeable — `-bmkProfile android`, `--bmk-profile=android` and
`-bmk.profile android` all work. Lists accept `;` or `,`. Booleans accept a trailing flag
(`-bmkDryRun`), `=true`/`=false`, or `1`/`0`.

Exit codes: `0` success · `1` build or health check failed · `2` usage error · `3` cancelled.
Pass `-bmkNoExit` to print the code instead of exiting, which is useful when chaining verbs.

## ConfigCLI — reading and editing the configuration

### `ConfigCLI.Describe`

Prints the whole configuration as JSON between `BEGIN_BMK_JSON` / `END_BMK_JSON` markers.

| argument | meaning |
| --- | --- |
| `-bmkResultFile <path>` | also write the JSON to this file |

Fields: `settingsAssetPath`, `activeEnvironment`, `common`, `commonVersioning`, `activeBuildTarget`,
`healthy`, `healthIssues`, `defaultConfigKeys`, `environments[]`, `profiles[]`, `queues[]`. Each
environment carries `id`, `displayName`, `assetPath`, `active`, `addedDefines`, `removedDefines`, the
player setting values in effect (`productNameOverride`, `companyNameOverride`,
`applicationIdentifierOverride`, `applicationIconOverride` as an asset path), `overridesVersioning`,
`versioning`, `variables` (`key=value`), `configKeys` and `actionCounts`. Each profile carries `id`,
`displayName`, `assetPath`, `target`, `enabled`, `defaultEnvironment`, `overridesVersioning` and
`versioning`.

The player setting values and `variables` are **resolved**, so they include anything taken from the
common configuration; `versioning` names the asset the block came from. `overridesVersioning` is the
raw switch, which is what you set to change one level without disturbing the others.

### `ConfigCLI.CreateEnvironment`

Creates the asset under `Assets/BuildManagerKit/Environments/` and registers it.

| argument | meaning |
| --- | --- |
| `-bmkEnv <id>` | **required.** Plain identifier — becomes `ENV_<ID>` and part of the filename |
| `-bmkDisplayName <name>` | UI name; defaults to the id |
| `-bmkColor <#RRGGBB>` | accent colour; defaults to an unused hue |
| `-bmkRequireConfirmation <bool>` | ask before activating or building. Use it for `prod` |

Plus every `SetEnvironment` argument below.

### `ConfigCLI.SetEnvironment`

Only the arguments present are changed.

| argument | meaning |
| --- | --- |
| `-bmkEnv <id>` | **required** |
| `-bmkDisplayName <name>` | |
| `-bmkDescription <text>` | |
| `-bmkColor <#RRGGBB>` | |
| `-bmkRequireConfirmation <bool>` | |
| `-bmkDefines <A;B>` | defines added while active. **Replaces the list** |
| `-bmkRemoveDefines <A;B>` | defines stripped while active |
| `-bmkGenerateEnvDefine <bool>` | auto-add `ENV_<ID>`; on by default |
| `-bmkProductName <name>` | `""` takes the common value instead |
| `-bmkCompanyName <name>` | `""` takes the common value instead |
| `-bmkAppIdentifier <id>` | e.g. `com.studio.game.dev`; `""` takes the common value |
| `-bmkIcon <Assets/path.png>` | application icon while this environment is active; `""` takes the common icon. Must import as a Texture2D, not a Sprite |
| `-bmkForceDevelopment <Inherit\|Enabled\|Disabled>` | forces development builds regardless of the profile |
| `-bmkVars <k=v;k=v>` | runtime variables, **merged** by key |
| `-bmkClearVars` | drop existing variables before merging |
| `-bmkOverrideVersioning <bool>` | version this environment differently from the common configuration; `false` hands it back |
| versioning arguments | as listed under `SetCommon`; passing any of them turns the override on |

### `ConfigCLI.SetCommon`

The values every environment starts from. Only the arguments present are changed.

| argument | meaning |
| --- | --- |
| `-bmkProductName <name>` | `""` stops managing it at all |
| `-bmkCompanyName <name>` | |
| `-bmkAppIdentifier <id>` | e.g. `com.studio.game` |
| `-bmkIcon <Assets/path.png>` | shared application icon; `""` stops managing it |
| `-bmkForceDevelopment <Inherit\|Enabled\|Disabled>` | |
| `-bmkVars <k=v;k=v>` | shared runtime variables, **merged** by key |
| `-bmkClearVars` | drop existing shared variables before merging |
| `-bmkManageVersion <bool>` | let the kit write `PlayerSettings.bundleVersion` |
| `-bmkVersionSource <PlayerSettings\|Profile\|GitTag>` | `Profile` means an explicit value; `VersionFile` is rejected — use `-bmkVersionFile` |
| `-bmkVersion <1.4.2>` | explicit version (implies the `Profile` source) |
| `-bmkVersionFile <version.txt>` | read the version from a file and write bumps back; `""` switches it off |
| `-bmkNoVersionFile` | switch the version file off, keeping its path |
| `-bmkManageBuildNumber <bool>` | let the kit write the Android/iOS build number |
| `-bmkBuildNumberPolicy <Manual\|AutoIncrementOnSuccess\|GitCommitCount\|Timestamp>` | |
| `-bmkBuildNumber <int>` | the stored counter |

### `ConfigCLI.DeleteEnvironment`

`-bmkEnv <id>`. Refuses to delete the last remaining environment while it is active. Also clears the
references to it: queue defaults and overrides, and the profiles that allowed or defaulted to it.

### `ConfigCLI.SetConfigAsset` / `ConfigCLI.RemoveConfigAsset`

| argument | meaning |
| --- | --- |
| `-bmkEnv <id>` | environment to publish on |
| `-bmkDefaultConfig` | …or publish as a project-wide default instead |
| `-bmkKey <key>` | **required.** Lookup key for `EnvironmentAssets.Current.Get<T>(key)` |
| `-bmkAsset <Assets/path>` | **required** for `SetConfigAsset` |

`ConfigCLI.Help` prints all of this from the installed version.

## BuildCLI — running builds

### `BuildCLI.Build`

| argument | meaning |
| --- | --- |
| `-bmkProfile <id>` | **required** |
| `-bmkEnv <id>` | environment to build with |
| `-bmkOutput <path>` | override the output directory |
| `-bmkExecutable <name>` | override the player file name; tokens allowed |
| `-bmkVersion <x.y.z>` | override the version string |
| `-bmkBuildNumber <n>` | override the build number |
| `-bmkDefines <A;B>` | extra scripting defines for this run |
| `-bmkScenes <a;b>` | replace the scene list |
| `-bmkResultFile <path>` | write the JSON result here |
| `-bmkDryRun` | validate and log without building |
| `-bmkRun` | launch the player once it is built (Build And Run) — local use only |
| `-bmkNoPlatformSwitch` | fail instead of switching the active platform |

Per-run build option overrides — omit to keep the profile's value:

| argument | values |
| --- | --- |
| `-bmkDevelopment` | bool |
| `-bmkAutoConnectProfiler` | bool |
| `-bmkDeepProfiling` | bool |
| `-bmkScriptDebugging` | bool |
| `-bmkStrictMode` | bool — fail the build on any error |
| `-bmkCleanBuild` | bool — clean the build cache first |
| `-bmkDetailedReport` | bool |
| `-bmkCompression` | `Default` \| `Lz4` \| `Lz4HC` |
| `-bmkScriptingBackend` | `Mono2x` \| `IL2CPP` \| `WinRTDotNET` |
| `-bmkIl2CppConfig` | `Debug` \| `Release` \| `Master` |
| `-bmkStripping` | `Disabled` \| `Minimal` \| `Low` \| `Medium` \| `High` |
| `-bmkSubtarget` | `Player` \| `Server` (or just `-bmkServer`) |

Android: `-bmkAppBundle` (bool), `-bmkSplitBinary` (bool), `-bmkAndroidArchitectures`
(`ARM64`, `ARMv7`, or a comma-separated set), `-bmkKeystore <path>`, `-bmkKeyalias <name>`.
Passwords come from the environment variables named on the profile — `ANDROID_KEYSTORE_PASS` and
`ANDROID_KEYALIAS_PASS` by default. **Never pass a password on the command line.**

iOS: `-bmkAppleTeamId <id>`.

### Other verbs

| verb | arguments |
| --- | --- |
| `BuildCLI.BuildQueue` | `-bmkQueue <id>` (required), `-bmkEnv`, `-bmkStopOnFailure <bool>`, `-bmkResultFile`, plus every build override |
| `BuildCLI.SwitchEnvironment` | `-bmkEnv <id>` |
| `BuildCLI.SwitchPlatform` | `-bmkTarget <BuildTarget>`, `-bmkServer` |
| `BuildCLI.List` | — |
| `BuildCLI.ValidateAll` | validates every enabled profile without building |
| `BuildCLI.Doctor` | `-bmkStrict` treats warnings as failures |
| `BuildCLI.Help` | prints the full reference from the installed version |

## Tokens

Output paths and executable names expand tokens. The full set:

| group | tokens |
| --- | --- |
| project | `{projectRoot}` `{projectName}` `{productName}` `{companyName}` `{bundleId}` |
| profile | `{profile}` `{profileName}` `{target}` `{targetShort}` `{platform}` |
| environment | `{env}` `{envName}` `{ENV}` |
| version | `{version}` `{versionDots}` `{buildNumber}` `{buildType}` |
| output | `{executable}` `{extension}` |
| git | `{branch}` `{commit}` `{commitLong}` `{dirty}` |
| machine | `{user}` `{machine}` |
| time | `{date}` `{time}` `{datetime}` `{timestamp}` |

Case matters for `{env}` vs `{ENV}` — the second is upper-cased. The time tokens accept a format
argument: `{date:yyMMdd}`, `{time:HH-mm}`.

## Result files

`-bmkResultFile` writes JSON — parse this rather than scraping the log:

```json
{
  "statusText": "Succeeded",
  "profileId": "android", "environmentId": "qa", "target": "Android",
  "version": "1.4.2", "buildNumber": 118,
  "outputPath": "Builds/Android/MyGame.aab", "outputSizeBytes": 84213760,
  "durationSeconds": 214.7, "errors": 0, "warnings": 3,
  "artifacts": ["Builds/Android/MyGame.aab", "Builds/Android/build_manifest.json"],
  "logFile": "Logs/BuildManagerKit/android-qa-….log",
  "gitBranch": "main", "gitCommit": "202dd28", "startedAtUtc": "…", "message": ""
}
```

`BuildQueue` writes the same shape per entry under an aggregate wrapper.
