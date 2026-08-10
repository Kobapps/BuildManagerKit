# Changelog

All notable changes to BuildManagerKit are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the package uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.5.1] — 2026-08-10

### Fixed

- **A per-environment application icon now reaches the icons the built app ships.** The override wrote
  only the legacy `IconKind.Application` slots, which on Android is the legacy launcher icon alone —
  every launcher from API 26 on draws the *adaptive* icon and ignores it, so a dev or QA build kept the
  project's icon on the home screen while the Inspector showed the new one. On iOS the spotlight icons
  and the 1024 marketing icon the App Store asks for were untouched for the same reason.

  Every kind, size and layer the target reports is written now, through the platform icon API the
  player reads: on Android the adaptive icon's background and foreground at all six densities plus the
  round and legacy icons, on iOS the application, spotlight and 1024 store icons, and on a desktop
  player every size from 16 to 1024. A magenta icon through an environment gives **24** icon resources
  in the APK where it previously gave 6, and all ten entries of the generated Xcode project's
  `AppIcon.appiconset` — `ios-marketing` 1024 included — hold it.

  Notification and settings icons are deliberately left alone: a white silhouette and a small glyph
  are not a launcher icon, so the project's own stay in place.

- **The icons restored after a build cover the same ground**, so a badged QA icon cannot survive in an
  adaptive slot into the next build. A snapshot persisted by an earlier version restores its legacy
  icons exactly as before.

### Added

- **A warning when the assigned icon is smaller than the largest slot it fills.** Unity upscales it
  without a word, and a blurry 1024 store icon is otherwise found by a store reviewer.

## [1.5.0] — 2026-08-02

### Added

- **Typed per-environment configs.** Derive a `ScriptableObject` from the new
  `BuildManagerKit.EnvironmentConfig`, list the asset on the environments that should publish it,
  and read it at runtime through `EnvironmentConfigs.Get<YourConfig>()` — the type is the address,
  so there is no key to keep in sync between the asset list and the call site, and a rename is a
  compile error rather than a null at runtime. Full facade: `Get<T>` · `TryGet<T>` · `Has<T>` ·
  `GetOrDefault<T>` · `GetOrCreate<T>` · `Require<T>` · `Get<T>(key)` · `All` · `EnvironmentId`.
  Lookup prefers an exact type match and falls back to an assignable one, so asking for a base class
  returns whichever subclass the environment publishes.
- **Configs are shared by listing one asset on several environments.** There is a single asset, so
  it is edited once and every environment listing it picks the change up; an environment needing
  different values gets its own instead. The window marks a shared config `SHARED ×n`, names the
  other environments in the tooltip, and its ⋮ menu adds or removes it from any of them.
- **Each config's inspector is drawn inline in the Environments tab**, folded behind a one-line
  summary, so comparing dev against prod no longer means clicking through to separate assets. The
  new **Add** menu creates a config of any type in the project, adopts one another environment
  already publishes, or accepts a drag-and-drop; `EnvironmentConfig.Summary` supplies the collapsed
  line.
- `EnvironmentAssets.GetConfig<T>()`, `TryGetConfig<T>()` and `Configs` expose the same type-based
  lookup on the generated asset, and `BuildEnvironment.GetConfig<T>()` answers for any environment
  from Editor code.
- `EnvironmentConfigCatalog` — the Editor-side operations behind all of this (discover config types,
  find assets, attach, detach, create, and list which environments publish a given config), so a
  script or a test does exactly what the window does.
- The **New** menu offers only types an asset can actually be created from — concrete, top-level and
  non-generic. Unity resolves a `ScriptableObject`'s script by finding a file named after the class,
  which a nested type never has, so an asset created from one saves with a null script reference and
  cannot be loaded. `TypeCache` reports every loaded assembly including the test ones, whose fixtures
  are exactly that shape.
- The health check reports two configs resolving to the same key in one environment, and empty slots
  in a config list. The existing "published by some environments but not others" warning now covers
  configs, so a config on `dev` but not on `prod` is caught before the release build.

### Changed

- `-bmkDescribe` now lists each environment's typed configs as `key=Type (path)` alongside the
  existing `configKeys`, so an agent can share a config by referencing the asset rather than
  creating a second one.
- Publish precedence is now, lowest to highest: global config assets → the environment's configs →
  the environment's own keyed entries. Existing setups are unaffected — keyed entries still win over
  the global defaults exactly as before.

### Removed

- **The Appearance card on the Settings tab.** Theme and density are EditorCoreKit-wide preferences
  rather than anything this tool owns, and embedding the picker put a 260-pixel copy of another
  package's settings pane in the middle of a build tool. Set them where they live, in the
  EditorCoreKit theme settings.

## [1.4.0] — 2026-07-31

### Changed

- **Updated for EditorCoreKit 2.0.0**, which renames the kit's `Eck` prefix to `KUI`
  (`EckBadge` → `KUIBadge`). Every window, view, inspector and drawer now uses the new type names,
  and `BuildManager.uss` moves to the renamed styling layer — kit classes are `.kui-*` and theme
  tokens `--kui-*`. BuildManagerKit's own public API is unchanged.
- **The EditorCoreKit version gate now requires 2.0.0 rather than 1.0.0.** The `versionDefines`
  expression on the editor, bootstrap, samples and test assemblies read `1.0.0`, which Unity
  interprets as *that version or newer* — so a project holding EditorCoreKit 1.x still compiled the
  editor assembly and then failed on every renamed type. Raising the gate means an out-of-date kit
  now switches the tool off and asks for the newer one, which is what the constraint was there to do.

## [1.3.1] — 2026-07-30

### Fixed

- **Installing the package on its own no longer fills the console with errors.** 1.3.0 depended on
  EditorCoreKit but could only document the requirement — the Package Manager accepts version numbers
  in a package's `dependencies` and nothing else, so a git-distributed package cannot declare a
  git-distributed dependency. A project that installed only this package therefore compiled the whole
  editor assembly against types that were not there, producing a `CS0246` per use.

  The editor assembly now carries a `BUILDMANAGERKIT_EDITORCOREKIT` define constraint, satisfied by a
  version define on the package, so without the kit it is not compiled rather than compiled and
  broken. The same constraint covers the tests and the samples.

- **The dependency installs itself.** A small `BuildManagerKit.Bootstrap` assembly — compiled only
  while EditorCoreKit is *missing*, so it ships in no working project — adds it through
  `Client.Add` on the first domain load and reports what happened. A batch-mode Editor logs the
  requirement instead: CI must resolve what its manifest says, and a build that rewrites its own
  dependencies is not reproducible. *Tools ▸ Build Manager Kit ▸ Install EditorCoreKit (required)…*
  retries a failed install.

## [1.3.0] — 2026-07-30

### Added

- **Build and Run** — builds, then launches: a standalone player on this machine, an Android or iOS
  build deployed to the connected device, a WebGL build in a browser. In the header ▼ menu, the ⋮
  menu, the Dashboard, a profile's header and its inspector, and at
  *Tools ▸ Build Manager Kit ▸ Build And Run Selected Profile* (`⌘⇧⌥R`). It is a property of the run
  rather than of the profile — `BuildRunRequest.RunAfterBuild`, `-bmkRun` on the command line — so no
  profile asset can leave a player running on a build server. The confirmation dialog of a protected
  environment says which of the two it is about to do, and the reveal-on-success Finder window is
  skipped when the build was launched.
- **Open Output Folder** — the resolved output folder of the selected profile, from the status bar,
  both menus, the Dashboard, a profile's header and its inspector, and
  *Tools ▸ Build Manager Kit ▸ Open Build Output Folder*. `BuildRunner.ResolveOutputPath` and
  `ResolveOutputDirectory` expose the same resolution a build performs — every token, the version,
  the build number and the platform's file extension — so the path shown is the path used. Nothing is
  created: a profile with no builds yet opens the deepest existing folder of that path and logs which
  one, rather than failing or making an empty directory.

### Changed

- **The window is built on [EditorCoreKit](https://github.com/Kobapps/EditorCoreKit)**, which must be
  added to the project alongside this package — the Package Manager cannot resolve a git-only
  dependency declared by another package, so it will not appear on its own. The window shell, cards,
  pills, badges, banners, lists, drag-to-reorder, action cards, the searchable console, the empty
  states and the theme all come from the kit, which means every theme and density it ships now applies
  to this window; pick them in the Settings tab or under `Preferences ▸ EditorCoreKit`. The
  package-local `DragReorder`, `DashedBox` and `BuildConsole` are gone, and `BuildManager.uss` is down
  to the few rules only a build tool needs, all written against the kit's colour tokens.
- The two-pane pages — Profiles, Environments, History — have a draggable divider that remembers
  where it was left, and the action list is a proper collapsible card whose body is built the first
  time it is opened.
- A profile's detail header keeps Build, Build and Run and Open Output Folder as buttons and moves
  Validate, Dry Run, Ping Asset and Delete behind a ⋮ menu.

## [1.2.0] — 2026-07-30

### Added

- **Common configuration** — the settings that are the same in every environment (product and company
  name, bundle identifier, application icon, force development build, shared runtime variables and
  versioning) now live in one block on the settings asset. It appears as a **Common configuration**
  item pinned above the environment list in the Environments tab, in its own section because it is not
  an environment, and selecting it edits it exactly like one. Every environment starts from those
  values and overrides only what differs, so a company rename is one edit instead of one per flavour.
  Precedence follows the rest of the kit — profile over environment over common. `ConfigResolver`
  exposes the same resolution to code and `ConfigCLI.SetCommon` edits it headlessly.
- **Optional versioning** — version management and build number management are independent switches,
  and the version text file is a toggle of its own rather than one of the sources. With a switch off
  the kit leaves those player settings exactly as the project has them, which is what a project that
  stamps versions from a release script actually wants. The fields of a disabled switch are hidden
  rather than greyed out, in the window and in the Inspector alike, through a shared
  `VersioningConfig` drawer.
- **Versioning is shared, with per-level overrides** — the block lives in the common configuration,
  and an *Override versioning* switch on an environment or a profile covers the cases that differ
  (staging shipping `1.4.0-rc`, one platform on its own counter). The auto-increment counter is stored
  on whichever asset owns the block, and the Dashboard, the build log and `Describe` all name that
  asset. A build number supplied with `-bmkBuildNumber` is used as-is and no longer advances the stored
  counter, which used to make it drift away from what was shipped.
- **Delete a profile or an environment from the window** — a Delete button in the detail header and a
  right-click menu on every catalogue row. Deleting cleans up after itself: a profile is removed from
  the settings and from the queue entries that built it, and an environment additionally from the
  profiles that allowed or defaulted to it, the queue defaults and the active slot — handing the Editor
  to another environment when the deleted one was active. Deleting the last remaining environment while
  it is active is refused rather than leaving the Editor with defines from an asset that no longer
  exists. `ConfigCLI.DeleteEnvironment` performs the same cleanup.
- **`ConfigCLI.SetCommon`** plus versioning arguments on `SetEnvironment`:
  `-bmkOverrideVersioning`, `-bmkManageVersion`, `-bmkVersionSource`, `-bmkVersion`,
  `-bmkVersionFile`, `-bmkNoVersionFile`, `-bmkManageBuildNumber`, `-bmkBuildNumberPolicy` and
  `-bmkBuildNumber`. `Describe` reports the common configuration and the versioning each environment
  and profile resolves to, with the asset it came from.
- **Health checks for the common configuration** — duplicate shared variable keys, a variable with no
  key, and a version file that does not exist. Each of those would otherwise show up in every flavour
  at once.

### Fixed

- **The main toolbar environment pill never appeared on Unity 6.4.** It was compiled behind
  `UNITY_6000_5_OR_NEWER`, but the main-toolbar extension API it uses is already present in 6000.4 — so
  a project on 6.4 installed the package and simply got no toolbar element, with nothing to explain
  why. The gate is now `UNITY_6000_4_OR_NEWER`, verified by compiling the element against both the
  6000.4 and 6000.5 editor assemblies.

### Changed

- **The header profile selector is gone; Build is a split button.** The wide half builds the selected
  profile and says which one it is; the ▼ half lists every profile with its platform icon, plus Dry
  Run and Validate. Building a specific target is one click, and the choice becomes the new selection,
  so the header keeps answering "what does Build do". The menu stays available when no profile exists
  yet, because that is where the starter profiles are created. The header previously carried two
  similar looking pills — the Editor platform and the build profile — that read as two copies of one
  control.
- **The override checkboxes are gone.** Product name, company name, bundle identifier and the
  application icon are plain fields now: a value overrides the shared one, and clearing the field goes
  back to it. An environment's empty field shows the shared value greyed out as its placeholder and
  says underneath where the value in effect comes from, so nothing has to be inferred from a checkbox
  and a field that looks unset. In the common configuration an empty field means the kit does not
  manage that setting at all. `-bmkProductName ""` on an environment therefore hands the field back to
  the common value rather than clearing an override switch.

### Migration

- Profiles authored before this release carried their own versioning. They are migrated when they load
  — with the override switched on and the counter intact — so the next build stamps exactly what it did
  before. Profiles created afterwards share the common versioning. To move a profile onto the shared
  block, switch *Version this profile differently* off.
- Environments authored before this release had an override checkbox beside each player setting. They
  are migrated when they load: a field whose box was unchecked is cleared, because a value left behind
  by an unchecked box would otherwise start overriding the shared one the moment the boxes disappeared.
  The one case that cannot survive is "override with an empty value", which used to mean "no product
  name at all" and now means "take the common one".
- `VersionService.Resolve`, `ResolveBuildNumber`, `WriteVersionFile` and `CommitBuildNumber` now take
  the resolved `VersioningConfig` (or the `BuildContext`) rather than a `BuildTargetProfile`, and
  `EnvironmentManager.ApplyPlayerSettingOverrides` has an overload taking the settings asset. Code
  calling the old signatures needs the one-line change; the profile's `VersionSource`, `Version`,
  `VersionFilePath`, `BuildNumberPolicy` and `BuildNumber` properties still read as before.

## [1.1.0] — 2026-07-29

### Added

- **Configuration command line** — `ConfigCLI.Describe`, `CreateEnvironment`, `SetEnvironment`,
  `DeleteEnvironment`, `SetConfigAsset`, `RemoveConfigAsset` and `Help`. `Describe` emits the whole
  project — environments, profiles, queues, published config keys and the health check — as JSON,
  and the mutating verbs write through Unity's serialisation, change only the arguments passed, and
  run the health check when they finish. This gives provisioning scripts a validated alternative to
  editing the `.asset` YAML, where a text edit silently drops `[SerializeReference]` action lists
  and GUID asset references.
- **Environment id validation** — an id has to survive being both an `ENV_<ID>` preprocessor symbol
  and part of a filename, so `CreateEnvironment` rejects anything that is not a plain identifier
  and names the sanitised form it would otherwise have used.
- **`-bmkIcon`** — sets or clears an environment's application icon override from the command line,
  which previously had no headless route at all. `Describe` reports it as an asset path.
- **AI assistant skill** — ships in `Skills~/buildmanagerkit`, installable to `.claude/skills` at
  project or user level from *Tools ▸ Build Manager Kit ▸ AI Assistant Skill…* or the Settings tab.
  It teaches a coding agent to drive the command line rather than hand-edit assets, and documents
  the environment rules that are easy to get wrong. Installing replaces the folder wholesale;
  removing it first verifies the folder really is this skill.

### Changed

- **Application icon override** — moved out of the generic property list into its own card with a
  live preview of the texture, its dimensions and format. The fields only appear once the override
  is switched on.

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

[1.2.0]: https://github.com/Kobapps/BuildManagerKit/releases/tag/v1.2.0
[1.1.0]: https://github.com/Kobapps/BuildManagerKit/releases/tag/v1.1.0
[1.0.0]: https://github.com/Kobapps/BuildManagerKit/releases/tag/v1.0.0
