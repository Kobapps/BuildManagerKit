# Recipes

Worked examples. Each assumes `$UNITY` and `$PROJECT` are set and the Editor is closed:

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.0.30f1/Unity.app/Contents/MacOS/Unity
PROJECT=/path/to/project
bmk() { "$UNITY" -batchmode -nographics -quit=false -projectPath "$PROJECT" -logFile - -executeMethod "$@"; }
```

## Bootstrap a project that has no BuildManagerKit setup

There is no CLI verb for the first-run setup, because it inspects which platform modules are
installed. Ask the user to run `Tools ▸ Build Manager Kit ▸ Create Starter Setup` once — it
creates the settings asset, `dev`/`stage`/`prod`, and a profile per installed platform. After
that everything below works headlessly.

## Add a QA environment to an existing project

```bash
bmk BuildManagerKit.Editor.ConfigCLI.Describe -bmkResultFile /tmp/bmk.json
```

Read `/tmp/bmk.json` first — you need the existing `configKeys` so the new environment publishes
the same set, and the existing define names so you deliberately choose which to share and which
to keep exclusive.

```bash
bmk BuildManagerKit.Editor.ConfigCLI.CreateEnvironment \
    -bmkEnv qa -bmkDisplayName "QA" -bmkColor "#E0A030" \
    -bmkDescription "Internal QA builds against the staging backend" \
    -bmkDefines "QA_BUILD" \
    -bmkAppIdentifier com.studio.game.qa \
    -bmkProductName "MyGame QA" \
    -bmkVars "api_url=https://qa.api.example.com;log_level=verbose"
```

Then publish the same config keys the other environments publish:

```bash
bmk BuildManagerKit.Editor.ConfigCLI.SetConfigAsset \
    -bmkEnv qa -bmkKey gameConfig -bmkAsset Assets/Config/GameConfig_QA.asset
```

Verify: `bmk BuildManagerKit.Editor.BuildCLI.Doctor` must exit `0`.

## Change one field without disturbing the rest

```bash
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment -bmkEnv qa -bmkVars "log_level=info"
```

Variables merge by key, so this rewrites `log_level` and leaves `api_url` alone. Defines do
**not** merge — `-bmkDefines` replaces the list, so read the current one from `Describe` and pass
the full set:

```bash
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment -bmkEnv qa -bmkDefines "QA_BUILD;NEW_FLAG"
```

## Hand a field back to the common configuration

There are no override switches — a value overrides, an empty field takes the shared value:

```bash
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment -bmkEnv qa -bmkProductName ""
```

The same argument on `SetCommon` means something stronger: "do not manage the product name at all",
leaving `PlayerSettings.productName` as the project has it.

## Move shared settings into the common configuration

When several environments repeat the same company name, identifier prefix or variables, put the value
in the common block once and drop the duplicates:

```bash
# 1. What does each environment currently set?
bmk BuildManagerKit.Editor.ConfigCLI.Describe -bmkResultFile /tmp/bmk.json

# 2. The shared values, versioning included
bmk BuildManagerKit.Editor.ConfigCLI.SetCommon \
    -bmkCompanyName "Studio" -bmkVars "log_level=info" \
    -bmkVersion 1.4.0 -bmkBuildNumberPolicy AutoIncrementOnSuccess

# 3. Drop the now-redundant copies; each of these falls back to the common value
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment -bmkEnv dev  -bmkCompanyName ""
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment -bmkEnv qa   -bmkCompanyName ""
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment -bmkEnv prod -bmkCompanyName ""

# 4. Confirm the resolved values are unchanged, then check health
bmk BuildManagerKit.Editor.ConfigCLI.Describe -bmkResultFile /tmp/bmk-after.json
bmk BuildManagerKit.Editor.BuildCLI.Doctor
```

`Describe` reports resolved values, so `companyNameOverride` should read `Studio` for every
environment both before and after — that diff being empty is the proof the move was safe.

## Let CI own the version, not Unity

```bash
# Read the version from a file the release script writes, and never touch the build number
bmk BuildManagerKit.Editor.ConfigCLI.SetCommon \
    -bmkVersionFile version.txt -bmkManageBuildNumber false
```

Or hand both to the build command instead, leaving the configuration alone:

```bash
bmk BuildManagerKit.Editor.BuildCLI.Build \
    -bmkProfile android -bmkEnv prod -bmkVersion "$CI_VERSION" -bmkBuildNumber "$CI_PIPELINE_IID"
```

`-bmkVersion` / `-bmkBuildNumber` on `Build` override the run without editing any asset — and a
supplied build number leaves the stored counter alone — which is the right choice for CI. `ConfigCLI`
is for the values a project should keep.

## One environment or platform on its own version

```bash
# Staging ships a release candidate; everything else stays on the common version
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment \
    -bmkEnv stage -bmkOverrideVersioning true -bmkVersion 1.5.0-rc1

# ...and back to sharing it
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment -bmkEnv stage -bmkOverrideVersioning false
```

There is no CLI verb for per-profile versioning; use the Profiles tab's *Version this profile
differently* switch when one platform needs its own counter. `Describe` reports each profile's
`versioning` and which asset it came from, so you can check which level won.

## Per-environment app icons

```bash
bmk BuildManagerKit.Editor.ConfigCLI.SetEnvironment \
    -bmkEnv qa -bmkIcon Assets/Art/Icons/AppIcon_QA.png
```

The texture must import as a **Texture2D**, not a Sprite — a Sprite-mode import is the usual cause
of `-bmkIcon` failing. Pass `-bmkIcon ""` to hand the icon back to the common configuration, or set a
shared one with `SetCommon -bmkIcon <path>`.

The icon is applied when the environment is activated and when it is built, and restored
afterwards along with the rest of the player settings, so a badged QA icon never leaks into a
production build. In the window it lives under `Environments ▸ <env> ▸ Application icon`, which
previews the texture in effect — the environment's own, or the shared one when the field is empty.

## Add per-environment JSON that shipped code reads

1. Create the file as a `TextAsset`, e.g. `Assets/Config/endpoints_qa.json`.
2. Publish it: `bmk … ConfigCLI.SetConfigAsset -bmkEnv qa -bmkKey endpoints -bmkAsset Assets/Config/endpoints_qa.json`
3. Repeat for every other environment with its own file — same key, different asset.
4. Read it:

```csharp
[Serializable] class Endpoints { public string api; public string cdn; }

var endpoints = EnvironmentAssets.Current.GetJson<Endpoints>("endpoints");
```

Skipping step 3 leaves `endpoints` null on the environments that do not publish it. The health
check warns about exactly this.

## Run a build in CI

```bash
bmk BuildManagerKit.Editor.BuildCLI.Build \
    -bmkProfile android -bmkEnv prod \
    -bmkBuildNumber "$CI_PIPELINE_IID" \
    -bmkResultFile build-result.json
echo "exit=$?"
```

Gate the pipeline cheaply before the expensive job:

```bash
bmk BuildManagerKit.Editor.BuildCLI.Doctor -bmkStrict   # seconds, not minutes
bmk BuildManagerKit.Editor.BuildCLI.ValidateAll
```

Ready-made GitHub Actions, GitLab CI and Jenkins pipelines ship as a sample — import
*CI Templates* from the package in the Package Manager, or generate one from the window's CI tab.

Signing passwords come from environment variables named on the profile
(`ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_PASS` by default). Set those as CI secrets. Never put
a password in an argument.

## Extend the pipeline with a custom action

Derive from `BuildStep` in an editor assembly. It appears in the Add Action menu automatically —
no registration:

```csharp
[Serializable]
[BuildStepMenu("Custom/Upload To CDN", Tooltip = "Uploads the archive to the CDN.")]
public sealed class UploadToCdnStep : BuildStep
{
    [SerializeField] private string m_Bucket = "releases";

    public override string Summary => m_Bucket;

    public override void Validate(BuildContext context, BuildValidationReport report)
    {
        if (string.IsNullOrEmpty(context.GetVariable("CDN_TOKEN")))
            report.AddError("CDN_TOKEN is not set.");
    }

    public override void Execute(BuildContext context)
    {
        context.Log.Info($"Uploading {context.OutputPath} to {m_Bucket}…");
        context.AddArtifact(context.OutputPath);   // throw BuildStepException to fail the build
    }
}
```

Or, for logic with nothing to configure, a static hook:

```csharp
[BuildHook(BuildStepScope.PreBuild, Order = -100)]
static void StampLicences(BuildContext context) =>
    LicenceBaker.Bake(context.Version, context.Environment.Id);
```

Implement `Validate` whenever the step depends on something that can be missing — it runs during
`-bmkDryRun`, which turns a 20-minute build failure into a 2-second one.

Adding a *configured instance* of a step to an environment or profile is UI work — the action
lists are `[SerializeReference]` arrays and the CLI does not edit them. Write the class, then ask
the user to add and configure it in the window.

## Diagnosing a failed build

1. Read the `-bmkResultFile` JSON: `statusText`, `message`, `errors`, `logFile`.
2. Read the file named by `logFile` — the full build log, kept under `Logs/BuildManagerKit`.
3. Re-run with `-bmkDryRun` to see whether validation alone reproduces it.
4. Run `BuildCLI.Doctor` — a surprising number of build failures are configuration collisions.

If the build fails only in CI, compare `Describe` output between the two machines; the usual
cause is a different active environment or a stale generated `BuildInfo` asset.
