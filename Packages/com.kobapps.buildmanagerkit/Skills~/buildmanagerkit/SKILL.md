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

`bmk-state.json` lists every environment id, its defines, variables and published config keys,
every profile, every queue, and the current health check. Read it before editing anything —
never guess an id.

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

### Rules that will bite you

- **The id must be a plain identifier** — lower case letters, digits, underscores, not starting
  with a digit. It becomes the `ENV_<ID>` preprocessor symbol and part of a filename. `my-env`
  is rejected, because `ENV_MY-ENV` does not compile. `CreateEnvironment` enforces this.
- **`-bmkDefines` replaces the whole list**, it does not append. Read the current list from
  `Describe` and pass the full set.
- **Sharing a define between environments is fine.** Switching strips every define contributed by
  *any* environment and then adds the incoming one's, so a define listed on both `dev` and `qa` is
  active whenever either is — which is how you express "non-production". What is *not* fine is two
  ids that sanitize to the same generated `ENV_<ID>` (`my-env` and `my_env`); the health check
  reports that as an error because runtime code cannot tell them apart.
- **Every environment should publish the same config keys.** A key that only some environments
  provide is a `null` at runtime on the others. The health check warns about it.
- **Passing an empty string clears an override**: `-bmkProductName ""` removes the product-name
  override rather than setting it to empty.

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
