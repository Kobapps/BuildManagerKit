# Changelog

All notable changes to BuildManagerKit are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] — 2026-07-29

### Added

- **Build profiles** — reusable per-platform recipes covering target, subtarget, scenes, output
  path, scripting backend, IL2CPP configuration, managed stripping, compression, extra defines and
  the full `BuildOptions` set.
- **Environments** — `dev` / `stage` / `prod` style flavours carrying scripting defines, product
  name, company name, bundle identifier and runtime key/value variables, applicable both to builds
  and to the Editor itself.
- **Environment quick switch** — from a dropdown in Unity's own main toolbar (Unity 6.5+, with a
  colour-coded pill showing the active environment), the window header, the `Tools` menu (`⌘⇧E`)
  or a Scene view overlay; switching applies exactly what a build applies and never leaves stale
  defines behind.
- **Runtime `BuildInfo`** — generated asset that lets shipped code read the environment, version,
  build number and git state it was built from.
- **Per-environment config assets** — an environment publishes assets under keys (a tuning
  ScriptableObject, a JSON TextAsset, an image, an audio clip) that shipped code reads through
  `EnvironmentAssets.Current.Get<T>(key)`, with `GetText`, `GetJson<T>`, `GetTexture`, `GetSprite`,
  `GetAudioClip`, `TryGet<T>` and `GetOrDefault<T>` alongside. The generated Resources asset
  references only the environment being built, so the other environments' assets never reach the
  player. Project-wide defaults on the settings asset are overridden per environment by key, and
  the health check warns when a key is published by some environments but not others.
- **Drag and drop reordering** — environments and every action list reorder by dragging the ≡ grip.
  The row lifts onto a drag layer and travels with the pointer across the whole window on both
  axes, an empty dashed slot opens at the landing position, and the rows between glide aside. The
  list holds its pre-drag height so nothing jumps when the row leaves the flow. Styled with the window's
  own rows rather than a stock `ListView`. The
  environment order is authoritative for the toolbar dropdown, the Scene view overlay, every menu,
  the CLI listing and the cycle shortcut. Undoable, and scriptable via
  `BuildManagerSettings.MoveEnvironment(from, to)`.
- **Pre/post build actions** — ordered, enableable, reorderable, filterable by environment and by
  build outcome, stored inline with `[SerializeReference]`.
- **Global action lists** — on-activate, pre build and post build lists on the settings asset that
  apply to every environment and profile, so shared work is configured once instead of being
  duplicated on each asset. The Environments and Profiles tabs show how many global actions also
  apply and link straight to them.
- **Action promotion and overrides** — "Make Global (move / copy)" lifts an action to the project
  wide list, and "Add Action ▸ Override Global" copies a global action into an environment or
  profile so it replaces the global one there. Backed by an override **Key**: actions sharing a key
  collapse to the most specific one, profile over environment over global.
- **Per-environment application icons** — an environment can replace the app icon, so a dev or
  stage build is recognisable on a device. Icons are captured and restored with the rest of the
  player settings, including across the persisted platform-switcher snapshots.
- **Platform icons throughout the UI** — the header pill, platform quick-switcher, profile
  catalogue, profile header and both dropdown menus show Unity's own build-target icons.
- **Built-in actions** — Increment Version, Set Scripting Defines, Clean Output Folder, Copy Files,
  Write Text File, Zip Output, Run Shell Command, Build Addressables, Post To Webhook, Require
  Clean Working Copy, Require Environment Variables, Log Message, Reveal Output Folder.
- **Extensibility** — derive from `BuildStep` and add `[BuildStepMenu]`, or mark a static method
  with `[BuildHook]`; both are discovered automatically.
- **Platform quick switcher** — stores the settings of the platform you leave and restores them
  when you come back.
- **Build queues** — multi-platform batches that persist their progress and resume themselves
  across the domain reloads caused by switching platforms.
- **Build history and log dashboard** — every run recorded with outcome, timing, size, artifacts
  and a persisted, searchable, severity-filtered log.
- **Command line interface** — `Build`, `BuildQueue`, `SwitchEnvironment`, `SwitchPlatform`,
  `List`, `ValidateAll`, `Doctor` and `Help` entry points with tolerant argument parsing, JSON
  reports and meaningful exit codes.
- **Complete build option coverage from the command line** — every setting the Editor exposes has
  a per-run override: development and profiler flags, strict mode, clean build, compression,
  scripting backend, IL2CPP configuration, stripping level, Android App Bundle / split binary /
  architectures / keystore / alias, Apple team id, standalone subtarget, executable name, the scene
  list and the platform-switch policy. Omitted flags keep the profile's value, so CI never has to
  edit profile assets; unparsable values exit `2` listing what was accepted; and the overrides in
  effect are echoed into the build log.
- **Configuration command line** — `ConfigCLI.Describe`, `CreateEnvironment`, `SetEnvironment`,
  `DeleteEnvironment`, `SetConfigAsset`, `RemoveConfigAsset` and `Help`. `Describe` emits the whole
  project — environments, profiles, queues, published config keys and the health check — as JSON,
  and the mutating verbs write through Unity's serialisation and run the health check afterwards.
  This gives provisioning scripts a validated alternative to editing the `.asset` YAML, where a
  text edit silently drops `[SerializeReference]` action lists and GUID asset references.
- **AI assistant skill** — ships in `Skills~/buildmanagerkit`, installable to `.claude/skills` at
  project or user level from *Tools ▸ Build Manager Kit ▸ AI Assistant Skill…* or the Settings tab.
  It teaches a coding agent to drive the command line rather than hand-edit assets, and documents
  the environment rules that are easy to get wrong. Installing replaces the folder wholesale;
  removing it first verifies the folder really is this skill.
- **CI templates** — generated GitHub Actions, GitLab CI, Jenkins and shell pipelines wired to the
  project's own profiles.
- **Validation** — configuration checks for missing modules, empty scene lists, absent scenes,
  missing keystores and unset signing secrets, runnable from the UI, the menu or CI.
- **Output path safety** — builds and clean steps refuse the project root, any folder containing
  the project, and `Assets` / `Library` / `Packages` / `ProjectSettings` / `UserSettings`;
  `Temp`, `obj` and `Logs` warn. Paths are collapsed before the check so `..` cannot escape, and
  the resolved length is checked against the Windows 260 character limit.
- **Project health check** — `BuildManagerIntegrity`, surfaced on the Dashboard, in
  `Tools ▸ Build Manager Kit ▸ Run Project Health Check` and as `BuildCLI.Doctor` for CI gates.
  Detects duplicate ids, multiple settings assets, colliding environment defines, illegal define
  symbols, profiles that overwrite each other's output, and broken queue or environment references.
- **Bounded resource use** — the build log is a 20 000 line ring buffer, the log embedded in the
  JSON result is capped at 256 KB (the log file stays complete), captured process output is capped
  at 4 MB per stream while streaming continues, and artifact registration is O(1) per entry.
  `Copy Files` streams its enumeration and caches created directories.
- **Editor window** — Dashboard, Profiles, Environments, Queues, History, CI/CD and Settings tabs,
  with a live build console.
- **Project Settings page** and custom inspectors for every asset type.

[1.0.0]: https://github.com/Kobapps/BuildManagerKit/releases/tag/v1.0.0
