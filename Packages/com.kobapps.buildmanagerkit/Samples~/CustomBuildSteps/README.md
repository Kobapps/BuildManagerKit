# Custom Build Steps

Three worked examples of extending the pipeline. Import the sample, and the actions appear in the
**Add Action** menu of the Build Manager window on the next compile — there is nothing to register.

| File | Shows |
| --- | --- |
| `UploadToCdnStep.cs` | A configurable post build action: validation, dry run handling, shelling out, publishing a variable for later actions. |
| `RequireMinimumSceneCountStep.cs` | A guard that fails fast before an expensive build starts. |
| `CodeHooks.cs` | The zero-configuration route: static methods marked with `[BuildHook]`. |

The scripts must live in an **editor** assembly (a folder named `Editor`, or an assembly definition
with `includePlatforms: ["Editor"]` that references `BuildManagerKit.Editor`). The sample
ships with such an assembly definition.
