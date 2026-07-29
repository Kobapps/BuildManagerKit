# CI Templates

The **CI / CD** tab of the Build Manager window generates these files already filled in with your
own profile and environment ids, and can write them straight into the project. The copies here are
for reference and for reading offline.

| File | System |
| --- | --- |
| `github-actions.yml` | GitHub Actions (via `game-ci/unity-builder`) → `.github/workflows/` |
| `gitlab-ci.yml` | GitLab CI (via the `unityci/editor` images) → `.gitlab-ci.yml` |
| `Jenkinsfile` | Jenkins declarative pipeline → repository root |
| `build.sh` | Plain shell, for local reproduction of a CI build |

## The one thing that matters

Whatever the system, the build is a single `-executeMethod` call:

```bash
Unity -batchmode -nographics -quit=false \
      -projectPath . \
      -executeMethod BuildManagerKit.Editor.BuildCLI.Build \
      -bmkProfile android -bmkEnv prod \
      -bmkResultFile build-result.json \
      -logFile -
```

Exit codes are `0` success, `1` build failed, `2` usage error, `3` cancelled, so no log scraping is
needed. `build-result.json` carries the status, duration, output size, error and warning counts,
the artifact list and the full log.

## Secrets

Never commit them. Export them as environment variables in the CI job; the actions read them by
name:

| Variable | Used by |
| --- | --- |
| `ANDROID_KEYSTORE_PASS`, `ANDROID_KEYALIAS_PASS` | Android signing (names configurable per profile) |
| `BMK_WEBHOOK_URL` | Post To Webhook |
| anything else | `context.GetVariable("NAME")` inside your own actions |

## Fetch depth

Build Manager Kit reads git for `{branch}`, `{commit}`, `GitTag` versioning and `GitCommitCount`
build numbers. Shallow clones break those, so set `fetch-depth: 0` (GitHub) or `GIT_DEPTH: 0`
(GitLab).

## Suggested pull request check

`BuildCLI.ValidateAll` validates every enabled profile — missing platform modules, empty scene
lists, absent scenes, missing keystores, unset signing secrets — in seconds, and exits non-zero on
any error. It is much cheaper than a real build and catches most broken configurations.
