#!/usr/bin/env bash
# Build Manager Kit — reproduce a CI build locally.
# Usage: ./build.sh [profile] [environment]
#
#   ./build.sh                 # first enabled profile, active environment
#   ./build.sh android prod
#   DRY_RUN=1 ./build.sh android prod
set -euo pipefail

PROFILE="${1:-win64}"
ENVIRONMENT="${2:-prod}"
PROJECT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
UNITY_VERSION="${UNITY_VERSION:-6000.5.0f1}"

case "$(uname -s)" in
    Darwin) DEFAULT_UNITY="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity" ;;
    Linux)  DEFAULT_UNITY="$HOME/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity" ;;
    *)      DEFAULT_UNITY="/c/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe" ;;
esac

UNITY="${UNITY:-$DEFAULT_UNITY}"

if [ ! -x "$UNITY" ]; then
    echo "Unity not found at '$UNITY'. Set UNITY=/path/to/Unity or UNITY_VERSION=x.y.z." >&2
    exit 2
fi

EXTRA_ARGS=()
[ "${DRY_RUN:-0}" = "1" ] && EXTRA_ARGS+=("-bmkDryRun")

echo "Building profile '$PROFILE' with environment '$ENVIRONMENT'…"

set +e
"$UNITY" \
    -batchmode \
    -nographics \
    -quit=false \
    -projectPath "$PROJECT_PATH" \
    -executeMethod BuildManagerKit.Editor.BuildCLI.Build \
    -bmkProfile "$PROFILE" \
    -bmkEnv "$ENVIRONMENT" \
    -bmkResultFile "$PROJECT_PATH/build-result.json" \
    "${EXTRA_ARGS[@]}" \
    -logFile -
EXIT_CODE=$?
set -e

case $EXIT_CODE in
    0) echo "✅ Build succeeded." ;;
    1) echo "❌ Build failed." >&2 ;;
    2) echo "❌ Usage or configuration error." >&2 ;;
    3) echo "⚠️  Build cancelled." >&2 ;;
    *) echo "❌ Unity exited with $EXIT_CODE." >&2 ;;
esac

[ -f "$PROJECT_PATH/build-result.json" ] && echo "Result: $PROJECT_PATH/build-result.json"
exit $EXIT_CODE
