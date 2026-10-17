#!/bin/bash
set -euo pipefail
astro_root="$(cd "$(dirname "$0")/.." && pwd)"
astro_editor="${LAMBO_UNITY_EDITOR:-${ASTRO_UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.0f1/Unity.app/Contents/MacOS/Unity}}"
astro_project="$astro_root/Unity"
mkdir -p "$astro_project/Logs"
case "${1:-help}" in
  build)
    exec "$astro_editor" -batchmode -nographics -quit -projectPath "$astro_project" \
      -buildTarget Android -executeMethod AstroQuestBuilder.BuildApk -logFile "$astro_project/Logs/build.log"
    ;;
  build-development)
    exec "$astro_editor" -batchmode -nographics -quit -projectPath "$astro_project" \
      -buildTarget Android -executeMethod AstroQuestBuilder.BuildDevelopmentApk -logFile "$astro_project/Logs/build.log"
    ;;
  generate)
    exec "$astro_editor" -batchmode -nographics -quit -projectPath "$astro_project" \
      -executeMethod AstroQuestBuilder.Generate -logFile "$astro_project/Logs/generate.log"
    ;;
  test)
    exec "$astro_editor" -batchmode -nographics -projectPath "$astro_project" \
      -runTests -testPlatform PlayMode -assemblyNames AstroQuest.PlayModeTests -testResults "$astro_root/flight-test-results.xml" \
      -logFile "$astro_project/Logs/tests.log"
    ;;
  preview)
    exec "$astro_editor" -batchmode -quit -projectPath "$astro_project" \
      -executeMethod AstroQuestBuilder.CapturePreview -logFile "$astro_project/Logs/preview.log"
    ;;
  *)
    printf '%s\n' 'Usage: bash scripts/unity.sh {build|build-development|generate|test|preview}'
    ;;
esac
