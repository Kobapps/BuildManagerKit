---
name: buildmanagerkit
description: Manage Unity build environments, per-environment config assets, build profiles and builds in a project using BuildManagerKit. Use when asked to add or change an environment (dev/stage/prod/qa), set scripting defines or bundle identifiers per environment, publish config assets or JSON per environment, switch the active environment, run or debug a build, or wire BuildManagerKit into CI.
---

# BuildManagerKit

BuildManagerKit stores its configuration as Unity assets: one `BuildManagerSettings` asset, one
`BuildEnvironment` asset per flavour (`dev`, `stage`, `prod`, …), and one `BuildTargetProfile` per
platform. Everything is driven from the command line, so you never need the Editor UI.

## The one rule: never hand-edit the `.asset` files

They are Unity YAML. The action lists are `[SerializeReference]` arrays whose entries are bound by
assembly-qualified type name, and object references are GUID+fileID pairs. A text edit that gets
one of those wrong **drops the data with no error and no warning** — you find out at the next
build, or in production.

Use `ConfigCLI` instead. It writes through Unity's serialisation and runs the project health check
after every change.

## Running a verb

Every verb is a `-executeMethod` target. The general shape:

```bash
"$UNITY" -batchmode -nographics -quit=false -projectPath "$PROJECT" \
         -executeMethod BuildManagerKit.Editor.<Class>.<Verb> \
         <-bmkArgs…> -logFile -
```

- `-logFile -` streams the log to stdout. Without it you get silence.
- `-quit=false` is required — the verbs call `EditorApplication.Exit` themselves with a meaningful
  code. Adding `-quit` races them and you lose the exit code.
- **Unity refuses to open a project that is already open.** If the user has the Editor running,
  the command fails on the project lock. Ask them to close it, or ask them to run the equivalent
  from `Tools ▸ Build Manager Kit` instead. Do not delete the lock file.

Find the Editor binary rather than assuming a version:

```bash
# macOS
ls -d /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity
# Linux
ls -d ~/Unity/Hub/Editor/*/Editor/Unity
# Windows
ls -d "/c/Program Files/Unity/Hub/Editor/"*/Editor/Unity.exe
```

Match the version in the project's `ProjectSettings/ProjectVersion.txt`.

Exit codes: `0` success · `1` build or health check failed · `2` usage error · `3` cancelled.

## Always start by reading the current state

```bash
"$UNITY" -batchmode -nographics -quit=false -projectPath "$PROJECT" \
         -executeMethod BuildManagerKit.Editor.ConfigCLI.Describe \
         -bmkResultFile bmk-state.json -logFile -
```

`bmk-state.json` lists every environment id, its defines, variables and published config keys, the
common configuration, the versioning each environment and profile resolves to, every profile, every
queue, and the current health check. Read it before editing anything — never guess an id.

## Environments

Create:

```bash
-executeMethod BuildManagerKit.Editor.ConfigCLI.CreateEnvironment \
  -bmkEnv qa -bmkDisplayName "QA" -bmkColor "#E0A030" \
  -bmkDefines "QA_BUILD;ANALYTICS_STAGING" \
  -bmkAppIdentifier com.studio.game.qa \
  -bmkVars "api_url=https://qa.api.example.com;log_level=verbose"
```

Edit — only the arguments you pass change, so you can adjust one field in isolation:

```bash
-executeMethod BuildManagerKit.Editor.ConfigCLI.SetEnvironment \
  -bmkEnv qa -bmkVars "api_url=https://qa2.api.example.com"
```

Delete: `ConfigCLI.DeleteEnvironment -bmkEnv qa`.

Switch the active environment (applies defines, player settings and the on-activate actions
exactly as a build would): `BuildCLI.SwitchEnvironment -bmkEnv qa`.

## The common configuration

The values that are the same in every environment — product and company name, bundle identifier,
icon, shared runtime variables and versioning — live in one block on the settings asset, edited with
`ConfigCLI.SetCommon`. Every environment starts from them and overrides only what differs.
`Describe` reports the block as `common`, and each environment's reported values are the resolved
ones, shared values included.

```bash
# The shared values
-executeMethod BuildManagerKit.Editor.ConfigCLI.SetCommon \
  -bmkCompanyName "Studio" -bmkAppIdentifier com.studio.game \
  -bmkVars "log_level=info"

# qa differs only in the identifier; company and log_level come from the common block
-executeMethod BuildManagerKit.Editor.ConfigCLI.SetEnvironment \
  -bmkEnv qa -bmkAppIdentifier com.studio.game.qa
```

- Precedence is most-specific-wins: **profile over environment over common**.
- There are no override switches: a field with a value overrides, an empty field takes the common
  value. `-bmkProductName ""` on an environment therefore means "use the common product name", and the
  same argument on `SetCommon` means "do not manage the product name at all".
- Defines and action lists are **not** shared this way. Shared defines are listed per environment
  (they may overlap), shared actions belong in the settings asset's global action lists, and shared
  config assets in its global config assets.

## Versioning

Versioning lives in the common configuration by default, and both halves are optional:

```bash
-executeMethod BuildManagerKit.Editor.ConfigCLI.SetCommon \
  -bmkVersion 1.4.2 -bmkBuildNumberPolicy AutoIncrementOnSuccess

# read the version from a file instead, and let CI own the build number
-executeMethod BuildManagerKit.Editor.ConfigCLI.SetCommon \
  -bmkVersionFile version.txt -bmkManageBuildNumber false
```

- `-bmkManageVersion false` leaves `PlayerSettings.bundleVersion` alone; `-bmkManageBuildNumber
  false` leaves the Android `versionCode` and the iOS/macOS build number alone. Use them when
  something outside Unity stamps those values.
- `-bmkVersionFile <path>` reads the first non-empty line of that file and writes bumps back to it;
  `-bmkVersionFile ""` or `-bmkNoVersionFile` switches it off. It is a toggle, not a source — do not
  pass `-bmkVersionSource VersionFile`, which is rejected.
- An environment can version differently with `SetEnvironment -bmkOverrideVersioning true` plus the
  same versioning arguments; `-bmkOverrideVersioning false` hands versioning back to the common block.
- The `AutoIncrementOnSuccess` counter lives on whichever asset owns the block — the settings asset
  for the shared one — and `Describe` reports which. A build number passed to `BuildCLI.Build` with
  `-bmkBuildNumber` is used as-is and leaves the counter alone.

## Per-environment config assets

An environment publishes assets under string keys. The build bakes only the active environment's
list into `Assets/Resources/BuildManagerKit/EnvironmentAssets.asset`, so the other environments'
assets never reach the player.

```bash
-executeMethod BuildManagerKit.Editor.ConfigCLI.SetConfigAsset \
  -bmkEnv qa -bmkKey gameConfig -bmkAsset Assets/Config/GameConfig_QA.asset
```

Add `-bmkDefaultConfig` instead of `-bmkEnv` to publish a project-wide default that environments
override by key. Remove with `ConfigCLI.RemoveConfigAsset -bmkEnv qa -bmkKey gameConfig`.

Runtime side:

```csharp
using BuildManagerKit;

var config = EnvironmentAssets.Current.Get<GameConfig>("gameConfig");
var endpoints = EnvironmentAssets.Current.GetJson<Endpoints>("endpoints");
string apiUrl = BuildInfo.Current.GetVariable("api_url", "https://localhost:8080");

if (BuildInfo.Current.IsEnvironment("prod"))
    Analytics.Enable();
```

Use `EnvironmentAssets` for assets, `BuildInfo` variables for short strings. Both are generated
on build *and* on Editor environment switch, so play mode matches the player.

## Building

```bash
-executeMethod BuildManagerKit.Editor.BuildCLI.Build \
  -bmkProfile android -bmkEnv qa -bmkResultFile build-result.json
```

Add `-bmkDryRun` to validate and log the whole pipeline without producing a player — use it to
check your work cheaply before committing to a real build.

Every profile setting has a per-run override (`-bmkDevelopment`, `-bmkScriptingBackend`,
`-bmkStripping`, `-bmkAppBundle`, …) so CI never has to dirty a profile asset. See
[references/cli.md](references/cli.md) for the full list.

**Never set `-bmkOutput` to a path inside `Assets/`, `Library/`, `Packages/`, `ProjectSettings/`
or the project root.** The runner refuses it, but do not try — a player written into `Assets/`
takes a long time to clean up.

## Verify before you report success

After any change:

```bash
-executeMethod BuildManagerKit.Editor.BuildCLI.Doctor
```

Exit code `0` means clean. It catches duplicate ids, colliding output paths, clashing defines,
several settings assets, broken queue entries and config-key gaps. `ConfigCLI` runs it
automatically after each mutation, so a non-zero exit from a `Set…`/`Create…` verb means the
change saved but left the project unhealthy — fix it before moving on.

`BuildCLI.ValidateAll` goes further and validates every enabled profile without building.

## Reference

- [references/cli.md](references/cli.md) — every verb and argument.
- [references/recipes.md](references/recipes.md) — worked examples: adding an environment to an
  existing project, per-environment icons and identifiers, CI wiring, extending the pipeline with
  a custom action.
