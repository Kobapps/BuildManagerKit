# BuildManagerKit — Build Pipeline & CI/CD Automation

AAA-quality build pipeline, environment management and CI/CD automation for Unity 6.

One editor window replaces the scattered manual build process: reusable per-platform profiles,
first-class `dev` / `stage` / `prod` environments you can also switch inside the Editor, a
drag-and-drop pre/post build action system that is trivial to extend, and a command line that
makes CI run exactly the same pipeline as the button in the UI.

---

## Contents

- [Install](#install)
- [Two minute setup](#two-minute-setup)
- [Concepts](#concepts)
- [The window](#the-window)
- [Environments](#environments)
- [Reading the environment at runtime](#reading-the-environment-at-runtime)
- [Safety and scale](#safety-and-scale)
- [Pre and post build actions](#pre-and-post-build-actions)
- [Writing a custom action](#writing-a-custom-action)
- [Code hooks](#code-hooks)
- [Tokens](#tokens)
- [Command line / CI](#command-line--ci)
- [Build queues](#build-queues)
- [Versioning](#versioning)
- [Android signing](#android-signing)
- [What is written where](#what-is-written-where)
- [FAQ](#faq)

---

## Install

Add it from the Package Manager with
`https://github.com/Kobapps/BuildManagerKit.git?path=Packages/com.kobapps.buildmanagerkit`, or copy
`com.kobapps.buildmanagerkit` into your project's `Packages/` folder. Requires Unity 6000.0 or
newer. The runtime assembly contains a single
small `ScriptableObject`; everything else is editor only.

## Two minute setup

1. `Tools ▸ Build Manager Kit ▸ Create Starter Setup` — creates the settings asset, the
   `dev` / `stage` / `prod` environments and one profile per installed platform.
2. `Tools ▸ Build Manager Kit ▸ Build Manager` (`⌘⇧K` / `Ctrl+Shift+K`).
3. Pick an environment in the header, pick a profile, press **Build**.

Everything the wizard creates is a normal asset under `Assets/BuildManagerKit/`. Rename, move or
delete anything you do not want.

## Concepts

| Concept | What it is |
| --- | --- |
| **Profile** (`BuildTargetProfile`) | *How* to build one platform: target, scenes, output path, scripting backend, IL2CPP configuration, stripping, compression, signing, build options. |
| **Environment** (`BuildEnvironment`) | *Which flavour* to build: scripting defines, product name, bundle identifier, application icon, runtime key/value variables and per-environment config assets. |
| **Action** (`BuildStep`) | A unit of work that runs before or after the player build. |
| **Queue** (`BuildQueue`) | An ordered list of profiles built back to back. |
| **Settings** (`BuildManagerSettings`) | The project-wide catalogue, the global action lists and the behaviour toggles. |

Profiles and environments are deliberately orthogonal: three profiles and three environments give
you nine builds without duplicating a single setting.

## The window

| Tab | Purpose |
| --- | --- |
| **Dashboard** | Active environment and platform, the next build's resolved output path, Build / Dry Run / Validate, a live console and recent runs. |
| **Profiles** | The profile catalogue plus the full configuration and both action lists. |
| **Environments** | Create, edit and activate environments. |
| **Queues** | Multi-platform batches and their progress. |
| **History** | Every past run with its outcome, timings, artifacts and full searchable log. |
| **CI / CD** | The exact command line for your current selection, plus generated GitHub Actions, GitLab CI, Jenkins and shell pipelines. |
| **Settings** | Behaviour toggles, the global action lists and the extension reference. |

The header pills switch environment, platform and profile from anywhere.

There are two more places to switch environment without opening anything:

- **Main toolbar dropdown** (Unity 6.5+) — a coloured pill in Unity's own toolbar showing the
  active environment. Click to switch, right-click for the window and the other shortcuts. Move or
  hide it with Unity's normal toolbar customisation. On Unity 6.0–6.4 the toolbar API does not
  exist yet, so the overlay and menu below cover it.
- **Scene view overlay** — *Build Environment*, enable it from the Scene view overlay menu.

## Environments

Activating an environment applies **exactly** what a build would apply:

- adds its defines and the generated `ENV_<ID>` define, and removes the defines owned by every
  other environment, so nothing accumulates;
- applies the product name, company name and bundle identifier overrides;
- replaces the **application icon** when the environment sets one — a badged or tinted icon makes
  it obvious at a glance which flavour is installed on a device;
- regenerates the runtime `BuildInfo` asset;
- runs the global and the environment's own *on activate* actions.

Icons, defines and identifiers are all captured before a build and put back afterwards, so an
environment build never leaves the project settings changed.

Play mode therefore behaves like the shipped build. `⌘⇧E` / `Ctrl+Shift+E` cycles to the next one.

Mark production as **Require Confirmation** and it asks before activating or building.

### Ordering

Drag the ≡ grip on an environment row to reorder it — the row lifts out of the list and follows
the pointer around the window, an empty dashed slot opens where it will land, and the rows between
glide aside. The list order
is authoritative and every switcher reads it — the main toolbar dropdown, the Scene view overlay, the window header
menu, the dashboard buttons, `BuildCLI.List` and the order `⌘⇧E` cycles through. Reordering is
undoable, and `BuildManagerSettings.MoveEnvironment(from, to)` does the same thing from a script.

### Per-environment config assets

Beyond strings, an environment can publish whole **assets** — a tuning `ScriptableObject`, a JSON
`TextAsset`, a splash image, an audio bank — under keys that runtime code looks up:

```csharp
using BuildManagerKit;

// A ScriptableObject of tuning values, different per flavour.
var config = EnvironmentAssets.Current.Get<GameConfig>("gameConfig");

// A JSON TextAsset parsed into your own type.
if (EnvironmentAssets.Current.TryGetJson<Endpoints>("endpoints", out var endpoints))
    Api.Configure(endpoints.baseUrl);

// Images, clips, or anything else Unity can reference.
splash.texture = EnvironmentAssets.Current.GetTexture("splash");
var bank       = EnvironmentAssets.Current.GetAudioClip("stinger");

// Absent keys return null (or a fallback) rather than throwing.
var theme = EnvironmentAssets.Current.GetOrDefault("theme", defaultTheme);
```

Full API: `Get<T>` · `TryGet<T>` · `GetOrDefault<T>` · `GetConfig<T>` · `GetText` · `GetJson<T>` ·
`TryGetJson<T>` · `GetTexture` · `GetSprite` · `GetAudioClip` · `Has` · `Keys` · `Entries`. Keys are
case-insensitive and `EnvironmentAssets.Current` is never null.

**Only the environment being built ships.** The generated
`Assets/Resources/BuildManagerKit/EnvironmentAssets.asset` holds direct references to the active
environment's assets, so Unity's dependency scanner pulls in exactly those. A 200 MB debug atlas
referenced only by `dev` never reaches a prod build.

**Defaults and overrides.** Assets shared by every environment go in **Global Config Assets** on
the settings asset; an environment listing the same key overrides the default — the same rule as
the global action lists. The Environments tab shows the resolved set and marks which keys are
inherited.

The health check warns when a key is published by some environments but not others, because that
is a `null` that only appears in one flavour's build.

## Reading the environment at runtime

Build Manager Kit generates `Assets/Resources/BuildManagerKit/BuildInfo.asset` before every build
and on every Editor switch.

```csharp
using BuildManagerKit;

if (BuildInfo.Current.IsEnvironment("prod"))
    Analytics.Enable();

string api = BuildInfo.Current.GetVariable("api_url", "https://localhost:8080");
int   retries = BuildInfo.Current.GetIntVariable("retries", 3);

Debug.Log(BuildInfo.Current.ShortVersionString);   // 1.4.2+118 (prod)
Debug.Log(BuildInfo.Current.GitCommit);            // a1b2c3d
```

Add the key/value pairs under **Runtime Variables** on the environment asset. `BuildInfo.Current`
is never null — an unbaked project reports the `editor` environment.

## Pre and post build actions

Actions run in a fixed, predictable order:

```
global pre  →  environment pre  →  profile pre  →  [PLAYER BUILD]  →  profile post  →  environment post  →  global post
```

Pre build widens from general to specific; post build unwinds the other way, so a global
notification always sees the final state.

### Global actions — configure once, not per environment

Every list exists at three scopes. Anything that should happen for *all* environments (or all
profiles) belongs in the **global** list on the settings asset, not copied onto each asset:

| List | Where | Runs |
| --- | --- | --- |
| Global on activate | Settings tab | Whenever **any** environment is activated, before that environment's own actions |
| Global pre build | Settings tab | Before **every** build, first in the chain |
| Global post build | Settings tab | After **every** build, last in the chain |
| On activate / pre / post | Environment asset | Only for that environment |
| Pre / post | Profile asset | Only for that profile |

Global actions still see the environment they are running for, so one action covers all of them —
a global on-activate `Log Message` of `Now on {env}` prints `Now on dev` or `Now on prod` as
appropriate. The Environments and Profiles tabs show how many global actions also apply, with a
button that jumps straight to them.

Environment activation runs `global on activate → environment on activate`.

### Promoting and overriding

Two commands move an action between tiers, so you never have to choose the right scope up front:

- **⋮ ▸ Make Global (move / copy)** on any environment or profile action promotes it to the
  matching global list.
- **Add Action ▸ Override Global ▸ …** copies a global action into the current list so it replaces
  the global one *here only*.

Overriding works through the **Key** field. Actions that share a key collapse to the single most
specific one — profile beats environment beats global — and the winner runs at its own position in
the chain. Actions with an empty key never compete and always run. Keyed actions show a `⇄ key`
badge in the list header. "Override Global" fills the key in for you, generating one on the global
action the first time it is overridden.

```
global:      [notify → slack]        suppressed by the profile's key
environment: —
profile:     [notify → discord]  ⇄ notify     ← this one runs
```

Actions run top to bottom, and the ≡ grip on each card drags it to a new position — the ⋮ menu
also has Move Up / Move Down for long lists.

Every action has an **enabled** toggle, an **on error** policy (fail the build or warn and
continue), an **environment filter**, and — for post build actions — a **run when** filter of
always / on success / on failure.

Built in:

| Category | Action |
| --- | --- |
| Versioning | Increment Version (major / minor / patch, optionally persisted) |
| Player Settings | Set Scripting Defines |
| Files | Clean Output Folder · Copy Files · Write Text File · Zip Output |
| Automation | Run Shell Command |
| Content | Build Addressables *(when the package is installed)* |
| Notifications | Post To Webhook (Slack / Discord / Teams / raw JSON) |
| Guards | Require Clean Working Copy · Require Environment Variables |
| Utility | Log Message · Reveal Output Folder |

Player settings changed by a build are restored when it finishes (toggleable in Settings), so a
build never leaves the project dirty.

## Writing a custom action

Add a class. It appears in the **Add Action** menu on the next compile — there is nothing to
register.

```csharp
using System;
using UnityEngine;
using BuildManagerKit.Editor;

[Serializable]
[BuildStepMenu("Custom/Upload To CDN", Tooltip = "Uploads the archive to the release bucket.")]
public sealed class UploadToCdnStep : BuildStep
{
    [SerializeField] private string m_Bucket = "releases";

    // Shown collapsed in the action list — keep it short and specific.
    public override string Summary => m_Bucket;

    // Runs before anything is built, and from the Validate button.
    public override void Validate(BuildContext context, BuildValidationReport report)
    {
        if (string.IsNullOrEmpty(context.GetVariable("CDN_TOKEN")))
            report.AddError("CDN_TOKEN is not set.");
    }

    public override void Execute(BuildContext context)
    {
        var archive = context.GetVariable("archivePath", context.OutputPath);

        if (context.DryRun)
        {
            context.Log.Info($"[dry run] Would upload {archive} to {m_Bucket}.");
            return;
        }

        var result = ProcessRunner.RunShell($"aws s3 cp {ProcessRunner.Quote(archive)} s3://{m_Bucket}/");
        if (!result.Succeeded)
            throw new BuildStepException($"Upload failed with exit code {result.ExitCode}.");

        context.SetVariable("cdnUrl", $"https://cdn.example.com/{m_Bucket}");
        context.Log.Success("Uploaded.");
    }
}
```

Useful `BuildContext` members: `Profile`, `Environment`, `Target`, `Version`, `BuildNumber`,
`OutputPath`, `OutputDirectory`, `Scenes`, `Git`, `Report`, `Status`, `DryRun`, `IsBatchMode`,
`Log`, `Resolve(template)`, `ResolvePath(template)`, `GetVariable` / `SetVariable`,
`AddArtifact(path)`, `Fail(message)`.

Throw `BuildStepException` for an expected failure — the message is shown without a stack trace.

## Code hooks

For logic that does not need configuring:

```csharp
[BuildHook(BuildStepScope.PreBuild, Order = -100)]
static void StampLicences(BuildContext context) =>
    LicenceBaker.Bake(context.Version, context.Environment.Id);
```

The method must be static and take a single `BuildContext`. Hooks are discovered through
`TypeCache`, run after the configured lists of their phase, and are ordered by `Order`.

## Tokens

Available in output paths, file names, shell commands, written files and notification messages:

`{projectRoot}` `{projectName}` `{productName}` `{companyName}` `{bundleId}` `{profile}`
`{profileName}` `{env}` `{ENV}` `{envName}` `{target}` `{targetShort}` `{platform}` `{version}`
`{versionDots}` `{buildNumber}` `{executable}` `{extension}` `{branch}` `{commit}` `{commitLong}`
`{dirty}` `{user}` `{machine}` `{buildType}` `{outputDir}` `{outputPath}`

Date and time tokens accept a format argument: `{date}` `{date:yyMMdd}` `{time}` `{time:HH-mm}`
`{datetime}` `{timestamp}`.

Unknown tokens are left untouched so typos stay visible. Environment variables become tokens too,
so a variable named `api_url` is usable as `{api_url}`.

The default output template is:

```
{projectRoot}/Builds/{env}/{target}/{version}+{buildNumber}
```

## Command line / CI

```bash
Unity -batchmode -nographics -quit=false \
      -projectPath . \
      -executeMethod BuildManagerKit.Editor.BuildCLI.Build \
      -bmkProfile android \
      -bmkEnv prod \
      -bmkResultFile build-result.json \
      -logFile -
```

Argument names are matched loosely: `-bmkProfile`, `--bmk-profile`, `-bmk.profile` and
`-BMK_PROFILE` are the same flag, and both `-name value` and `-name=value` work.

| Entry point | Arguments |
| --- | --- |
| `BuildCLI.Build` | `-bmkProfile` *(required)* plus everything below |
| `BuildCLI.BuildQueue` | `-bmkQueue` *(required)* `-bmkEnv` `-bmkStopOnFailure` `-bmkResultFile`, plus every build override |
| `BuildCLI.SwitchEnvironment` | `-bmkEnv` *(required)* |
| `BuildCLI.SwitchPlatform` | `-bmkTarget` *(required)* `-bmkServer` |
| `BuildCLI.List` | — |
| `BuildCLI.ValidateAll` | — |
| `BuildCLI.Doctor` | `-bmkStrict` (fail on warnings too) |
| `BuildCLI.Help` | Prints the full reference below |

### Every build option, from the command line

Anything the Editor exposes is reachable per run, so a pipeline never has to edit — and therefore
dirty — a profile asset. **Omit a flag and the profile's own value is used**; there is no implicit
default, so `-bmkStrictMode false` and "not passing `-bmkStrictMode`" mean different things.

| Flag | Value | Overrides |
| --- | --- | --- |
| `-bmkEnv` | id | Environment |
| `-bmkOutput` | path | Output directory |
| `-bmkExecutable` | name | Player file name (tokens allowed) |
| `-bmkVersion` | `x.y.z` | Version string |
| `-bmkBuildNumber` | n | Build number |
| `-bmkDefines` | `A;B` | Extra scripting defines |
| `-bmkScenes` | `a.unity;b.unity` | The whole scene list |
| `-bmkSubtarget` | `Player` \| `Server` | Standalone subtarget (`-bmkServer` is shorthand) |
| `-bmkDevelopment` | bool | Development player |
| `-bmkAutoConnectProfiler` | bool | Auto connect the profiler |
| `-bmkDeepProfiling` | bool | Deep profiling support |
| `-bmkScriptDebugging` | bool | Allow script debugging |
| `-bmkStrictMode` | bool | Fail the build on any error |
| `-bmkCleanBuild` | bool | Clean the build cache first |
| `-bmkDetailedReport` | bool | Detailed build report |
| `-bmkCompression` | `Default` \| `Lz4` \| `Lz4HC` | Compression |
| `-bmkScriptingBackend` | `Mono2x` \| `IL2CPP` | Scripting backend |
| `-bmkIl2CppConfig` | `Debug` \| `Release` \| `Master` | IL2CPP configuration |
| `-bmkStripping` | `Disabled` … `High` | Managed stripping level |
| `-bmkAppBundle` | bool | Android `.aab` instead of `.apk` |
| `-bmkSplitBinary` | bool | Android split application binary |
| `-bmkAndroidArchitectures` | `ARM64`, `ARMv7`, or a comma separated set | Android architectures |
| `-bmkKeystore` | path | Android keystore (implies signing) |
| `-bmkKeyalias` | name | Android key alias |
| `-bmkAppleTeamId` | id | Apple Developer Team ID |
| `-bmkDryRun` | — | Validate and log without building |
| `-bmkNoPlatformSwitch` | — | Fail instead of switching the active platform |
| `-bmkResultFile` | path | Where to write the JSON result |
| `-bmkNoExit` | — | Do not call `EditorApplication.Exit` |

Signing passwords are never flags: they come from the environment variables named on the profile
(`ANDROID_KEYSTORE_PASS` and `ANDROID_KEYALIAS_PASS` by default), so they stay out of shell history
and CI logs.

Unparsable enum values abort with exit code `2` and print the accepted values rather than silently
falling back. Every override that was in effect is echoed into the build log, so a CI run records
what it was actually asked to do:

```
Overrides: development=True, compression=Lz4HC, scriptingBackend=Mono2x, executable=Nightly
```

One release job and one nightly job can therefore share a single profile:

```bash
# nightly: fast, debuggable, distinct name
-bmkProfile android -bmkEnv dev  -bmkDevelopment true -bmkScriptDebugging true \
  -bmkScriptingBackend Mono2x -bmkExecutable "{productName}-nightly"

# release: signed App Bundle, fully stripped
-bmkProfile android -bmkEnv prod -bmkDevelopment false -bmkAppBundle true \
  -bmkScriptingBackend IL2CPP -bmkIl2CppConfig Master -bmkStripping High \
  -bmkKeystore keys/release.keystore -bmkKeyalias release
```

Exit codes: `0` success · `1` build failed · `2` usage error · `3` cancelled.

`-bmkResultFile` writes a JSON report:

```json
{
  "statusText": "Succeeded",
  "profileId": "android",
  "environmentId": "prod",
  "target": "Android",
  "version": "1.4.2",
  "buildNumber": 118,
  "outputPath": "/…/Builds/prod/Android/1.4.2+118/MyGame.aab",
  "outputSizeBytes": 128374651,
  "durationSeconds": 412.7,
  "errors": 0,
  "warnings": 3,
  "gitBranch": "main",
  "gitCommit": "a1b2c3d",
  "artifacts": ["…/MyGame.aab", "…/build_manifest.json"],
  "logFile": "/…/Logs/BuildManagerKit/20260729-104501_android_prod.log",
  "log": "…"
}
```

The **CI / CD** tab generates ready to run definitions for GitHub Actions, GitLab CI, Jenkins and a
plain shell script, already wired to your profiles. Samples of each are also under `Samples~`.

## Build queues

A queue builds several profiles in order. Switching platforms between entries reloads the script
domain and would normally kill a running method, so the queue persists its progress in
`SessionState` and resumes itself — it finishes unattended in the Editor. In batch mode
`BuildCLI.BuildQueue` runs the whole queue synchronously instead.

Set **Stop On First Failure** per queue.

## Versioning

Version source: `PlayerSettings` · `Profile` · `VersionFile` (first non-empty line of a text file)
· `GitTag` (`git describe --tags`, leading `v` stripped).

Build number policy: `Manual` · `AutoIncrementOnSuccess` (the profile's counter, bumped after every
success) · `GitCommitCount` · `Timestamp` (minutes since 2020-01-01 UTC — monotonic and safely
below Google Play's `versionCode` ceiling).

The resolved values are written to `PlayerSettings.bundleVersion`,
`PlayerSettings.Android.bundleVersionCode`, `PlayerSettings.iOS.buildNumber` and
`PlayerSettings.macOS.buildNumber`.

## Android signing

The profile stores the keystore path and alias. **Passwords are never stored in the asset** — the
profile names the environment variables to read them from (`ANDROID_KEYSTORE_PASS` and
`ANDROID_KEYALIAS_PASS` by default). Validation warns before a long build starts if they are
missing.

## What is written where

| Path | Contents | Commit it? |
| --- | --- | --- |
| `Assets/BuildManagerKit/` | Settings, profiles, environments | yes |
| `Assets/Resources/BuildManagerKit/BuildInfo.asset` | Generated runtime info | optional |
| `Assets/Resources/BuildManagerKit/EnvironmentAssets.asset` | Generated config asset references | optional |
| `Logs/BuildManagerKit/` | One text log per run | no |
| `Library/BuildManagerKit/history.json` | Build history | no |
| `Library/BuildManagerKit/platform-settings.json` | Saved per-platform settings | no |
| `<output>/build_manifest.json` | Manifest next to each build | n/a |

## Safety and scale

The checks below exist because these mistakes are cheap to make and expensive to discover, and
both get more likely as a project grows.

### Where a build may write

Refused outright, before anything is created: the project root, any folder **containing** the
project, and `Assets`, `Library`, `Packages`, `ProjectSettings`, `UserSettings`. Writing a player
into `Assets` makes Unity import the entire build; a clean step aimed at the project root deletes
the project. `..` segments are collapsed first, so a template cannot sneak out and back in.

`Temp`, `obj` and `Logs` are allowed but warned about — Unity treats them as scratch space and may
clear them.

The resolved path is also length-checked: a warning past 180 characters, an error past 240, because
Unity appends its own sub-paths beneath the one it is given and Windows stops at 260.

### Project health check

`Tools ▸ Build Manager Kit ▸ Run Project Health Check`, the banner at the top of the Dashboard, or
`BuildCLI.Doctor` in CI. It reports:

- two profiles, environments or queues sharing an id — `-bmkProfile` resolves to exactly one, so CI
  would silently build the wrong thing;
- more than one settings asset, where which one wins is not deterministic;
- environments whose generated defines collide (`my env` and `my-env` both become `ENV_MY_ENV`);
- defines that are not legal C# symbols, which break compilation the moment they are applied;
- profiles sharing an output template with no `{target}`, `{platform}` or `{profile}` token, so a
  queue building them in sequence keeps only the last one;
- a default environment the profile does not allow, broken queue entries, unregistered assets, and
  a log folder inside `Assets`.

Run it as a pull request gate — it costs seconds rather than a build:

```bash
Unity -batchmode -nographics -quit=false -projectPath . \
      -executeMethod BuildManagerKit.Editor.BuildCLI.Doctor -logFile -
```

### Bounds

Nothing grows without a ceiling:

| Thing | Bound | Behaviour past it |
| --- | --- | --- |
| Build log in memory | 20 000 lines | Oldest dropped in blocks, count reported in the log file |
| Log embedded in the JSON result | 256 KB | Tail kept, omission noted; the log **file** is complete |
| Captured output per shell command | 4 MB per stream | Capture stops, streaming to the log continues |
| Build history | `HistoryLimit` (10–500) | Oldest entries and their log files removed |

Artifact registration is a hash set, so a step registering one artifact per copied file stays
linear. `Copy Files` streams with `EnumerateFiles` rather than materialising every path, caches the
directories it has created, and reports progress every 500 files.

## FAQ

**Does it affect my shipped game?**
The runtime assembly contains only `BuildInfo`. If you never call it and disable *Write Build Info
Asset*, nothing at all ships.

**Can I keep using File ▸ Build Settings?**
Yes. Profiles default to the enabled scenes of Build Settings; switch a profile to *Custom* to pin
its own list.

**What if the platform module is not installed?**
Validation reports it as an error before anything runs, and the profile is flagged in the UI.

**Does it work with Unity 6 Build Profiles?**
They coexist. Build Manager Kit drives `BuildPipeline.BuildPlayer` directly so its behaviour is
identical in the Editor and in batch mode.

**How do I stop a build from dirtying my project?**
That is the default: *Restore Settings After Build* captures player settings before the build and
puts them back afterwards.
