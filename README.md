# BuildManagerKit — Build Pipeline & CI/CD Automation

[![Unity](https://img.shields.io/badge/Unity-6000.0%2B-black.svg)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](Packages/com.kobapps.buildmanagerkit/LICENSE.md)

AAA-quality build pipeline, environment management and CI/CD automation for Unity 6. Reusable
per-platform build profiles, first-class `dev` / `stage` / `prod` environments that apply to the
Editor as well as to builds, an extensible drag-and-drop pre/post build action system, build queues
that survive domain reloads, a searchable build history, and a command line that exposes every
build option so CI runs exactly the same pipeline as the button in the window.

```bash
Unity -batchmode -nographics -quit=false -projectPath . \
      -executeMethod BuildManagerKit.Editor.BuildCLI.Build \
      -bmkProfile android -bmkEnv prod -bmkResultFile build-result.json
```

```csharp
// Shipped code reads the environment it was built with.
if (BuildInfo.Current.IsEnvironment("prod"))
    Analytics.Enable();

var config = EnvironmentAssets.Current.Get<GameConfig>("gameConfig");
```

This repository contains:

- **The package** — [`Packages/com.kobapps.buildmanagerkit`](Packages/com.kobapps.buildmanagerkit)
  (see its [README](Packages/com.kobapps.buildmanagerkit/README.md) for full documentation).
- **A Unity 6 development project** hosting the package and its edit-mode test suite.

## Installation (into your own project)

Add it from the Package Manager — *Add package from git URL…*:

```
https://github.com/Kobapps/BuildManagerKit.git?path=Packages/com.kobapps.buildmanagerkit
```

Or add it to `Packages/manifest.json` directly:

```json
"com.kobapps.buildmanagerkit": "https://github.com/Kobapps/BuildManagerKit.git?path=Packages/com.kobapps.buildmanagerkit"
```

No third-party dependencies. Requires Unity 6000.0 or newer. The runtime assembly contains only two
small `ScriptableObject` types (`BuildInfo` and `EnvironmentAssets`); everything else is editor
only, so the package adds nothing to a player that does not use them.

## Getting started

1. `Tools ▸ Build Manager Kit ▸ Create Starter Setup` — creates the settings asset, the
   `dev` / `stage` / `prod` environments and a profile per installed platform.
2. `Tools ▸ Build Manager Kit ▸ Build Manager` (`⌘⇧K` / `Ctrl+Shift+K`).
3. Pick an environment, pick a profile, press **Build**.

## Highlights

| | |
| --- | --- |
| **Profiles** | Target, scenes, output path, scripting backend, IL2CPP configuration, stripping, compression, signing and the full `BuildOptions` set — as VCS-friendly assets. |
| **Environments** | Defines, product name, bundle identifier, app icon, runtime variables and per-environment config assets. Switchable in the Editor from the main toolbar, so play mode matches the shipped build. |
| **Actions** | Ordered pre/post build steps at global, environment and profile scope, with key-based overrides. Extend by deriving from `BuildStep` or marking a method `[BuildHook]`. |
| **CI/CD** | Every build option reachable from the command line, JSON reports, meaningful exit codes, and generated GitHub Actions / GitLab CI / Jenkins pipelines. |
| **Safety** | Refuses to build into `Assets`, `Library` or the project root; a project health check catches duplicate ids, colliding output paths and clashing defines before they reach a build. |

## Development

Open this repository as a Unity 6 project. The package lives under `Packages/`, and the edit-mode
test suite runs from *Window ▸ General ▸ Test Runner*, or headlessly:

```bash
Unity -batchmode -nographics -projectPath . \
      -runTests -testPlatform EditMode -testResults results.xml
```

## License

MIT — see [LICENSE.md](Packages/com.kobapps.buildmanagerkit/LICENSE.md).
