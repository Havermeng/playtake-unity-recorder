# Validation scope

The original project-local recorder was tested in Unity6000.3.3f1/HDRP17.3/Windows11 with real GPU capture, NVENC720p60+AAC48kHz, CPU640x360/24 without sound, settings persistence, and automatic save on exitingPlay. Full FFmpeg decode passed and a final upright frame was inspected. The public package uses isolated names/assemblies and independent preferences. Public-package validation is recorded below before release.

Desired FPS is a container rate. Short captures do not establish long-session stability or perfect AV sync. No standalone player, remote peer or other render pipeline test is claimed.

## Public package verification — 2026-11-09

- Local UPM install in Unity6000.3.3f1/HDRP17.3: both assemblies compiled without errors.
- Neutral unsaved test scene with moving UI and a generated440Hz tone, no game assets in the capture.
- Final NVENC640x360/30 +AAC48kHz,3.033s: full decode passed; decoded audio dominant frequency440.0Hz, peak0.03024. DSP timestamps replaced wall-clock callback timing before acceptance.
- SoftwareH.264640x360/24 without audio,2.042s: full decode passed; actual exit from Play finalized the file.
- Earlier recorder tests also covered720p60 capture; no public-package60fps performance guarantee.
- Payload lint: original utility sources only, no game source/assets/binaries, credentials or personal absolute paths. Deterministic UPM/unitypackage archive members verified byte-for-byte.
- Package CI validates payload/packaging on Linux. It does not run Unity or claim cross-platform Editor support.

Unitypackage was also imported through AssetDatabase into a previously absent Assets/PlayTake folder: source/asmdef bytes matched the payload, both assemblies loaded, and compilation had0errors. The temporary install was removed afterward. Original project package manifests were restored; the isolated test scene was discarded.
