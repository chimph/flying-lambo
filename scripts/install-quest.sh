#!/bin/bash
set -euo pipefail
astro_root="$(cd "$(dirname "$0")/.." && pwd)"
astro_adb="${LAMBO_ADB:-${ASTRO_QUEST_ADB:-/Applications/Unity/Hub/Editor/6000.6.0f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb}}"
astro_apk="$astro_root/Builds/FlyingLambo.apk"
astro_device=()
astro_serial="${LAMBO_QUEST_SERIAL:-${ASTRO_QUEST_SERIAL:-}}"
if [[ -n "$astro_serial" ]]; then
  astro_device=(-s "$astro_serial")
fi
if [[ ! -f "$astro_apk" ]]; then
  printf '%s\n' 'Builds/FlyingLambo.apk is missing. Build it with bash scripts/unity.sh build.' >&2
  exit 1
fi
if [[ "$("$astro_adb" "${astro_device[@]}" get-state 2>/dev/null || true)" != device ]]; then
  printf '%s\n' 'Connect the Quest by USB and accept its USB debugging prompt. Set LAMBO_QUEST_SERIAL if several devices are attached.' >&2
  "$astro_adb" devices -l
  exit 1
fi
"$astro_adb" "${astro_device[@]}" install -r "$astro_apk"
"$astro_adb" "${astro_device[@]}" shell am start -n com.astroquest.prototype/com.unity3d.player.UnityPlayerActivity
