# PlayTake — Unity Video Recorder

Record the Unity **Game view to MP4** from a small Editor window. Choose NVIDIA NVENC or CPU H.264, include Unity audio, and start/stop without OBS.

**Windows Editor preview · Unity 6.3 · MIT**  
[Русская инструкция](Documentation~/README_RU.md) · [Downloads](https://github.com/Havermeng/playtake-unity-recorder/releases)

## Features

- 720p / 1080p / 1440p presets and custom even dimensions, up to3840×2160.
- 24 / 30 / 60fps output; adjustable CQ/CRF quality.
- NVIDIA NVENC, or software H.264 when NVIDIA hardware is unavailable.
- Optional Unity listener audio, encoded as stereo AAC. No microphone or desktop recording.
- Start/stop buttons and **Ctrl+Shift+F10**; per-user settings and output folder.
- Bounded GPU readback queues, letterboxing, real-time duration and unique filenames.
- Automatic finalization on leaving Play mode. No scene or prefab setup required.

## Requirements

- **Unity6000.3 or newer**, Windows Editor. Tested on Unity6000.3.3f1 / HDRP17.3 / Windows11 only. Other render pipelines and Unity versions are unverified.
- An external **FFmpeg** executable with `h264_nvenc` or `libx264`, and `aac`. FFmpeg is **not included**. Get it through the [official FFmpeg download page](https://ffmpeg.org/download.html); select `ffmpeg.exe` in the window. A WinGet link is detected when present.
- NVENC requires a compatible NVIDIA GPU and driver. Otherwise uncheck **NVIDIA NVENC**.
- An enabled AudioListener for audio. Async GPU readback support for video.

## Install

Choose **one** method; do not install both.

### Package Manager (Git)

Open **Window → Package Manager → + → Install package from Git URL** and enter:

```text
https://github.com/Havermeng/playtake-unity-recorder.git#v1.0.0-preview.1
```

Git must be available on your computer. The package ID is `com.havermeng.playtake`.

### Unitypackage

Download `PlayTake-1.0.0-preview.1.unitypackage` from [Releases](https://github.com/Havermeng/playtake-unity-recorder/releases/tag/v1.0.0-preview.1), then use **Assets → Import Package → Custom Package**. Import all files under `Assets/PlayTake`.

The release also includes a `.tgz` for Package Manager's **Install package from tarball**.

## Record

1. Open **Window → PlayTake → Recorder**.
2. Choose your folder, FFmpeg executable, resolution, FPS and quality. Lower CQ means better quality and a larger file. Defaults:1080p30,CQ20,NVENC,game audio enabled.
3. Enter Play mode and **keep the Game view visible**. Start recording with the button or Ctrl+Shift+F10.
4. Stop with the button or the same shortcut. Wait for **Saved** before closing Unity or copying the MP4.

Output defaults to your Windows **Videos/Unity** folder. The `.mp4.recording.json` sidecar reports duration, captured/dropped frames, requested FPS and audio sample count.

## Limits and troubleshooting

This is an **Editor tool**, not a standalone-game recorder or free camera. It records the Game view including UI. It does not alter Windows audio devices, game settings or camera controls.

- Output60fps does not guarantee60 unique frames. When rendering/capture cannot keep up, the last frame is repeated to preserve elapsed time. Start with1080p30 or720p30 under load.
- Switching away from the Game view can stop end-of-frame callbacks. Keep it visible. Resizing it ends the current recording with a notice; start a new take afterward.
- Changing scripts/reloading assemblies forces finalization. Finish recording before editing code.
- Audio is Unity's listener DSP output, not desktop loopback. OS volume, microphone and other apps are not recorded. No listener samples means a silent video with a notice. Audio blocks are aligned using Unity DSP time rather than main-thread callback timing.
- On an encoding/muxing failure, `.video.mp4` and `.audio.wav` temporary files are preserved for recovery. Check the window and Unity Console. Existing recordings are never overwritten.
- This preview has short capture tests. Long takes, exact perceptual AV sync, scene-transition audio and other render pipelines still need validation.

## Uninstall

Package Manager install: select PlayTake and click **Remove**. Unitypackage install: remove **only `Assets/PlayTake`** after stopping any recording. Output videos are kept in the selected folder. Local preferences may be cleared with `EditorPrefs.DeleteKey("PlayTake.Options.v1")` if desired.

## Development and credits

Created for **Havermeng**, with coding assistance from **OpenAI Codex**. Original utility code; no game code, game assets, FFmpeg binaries, sample game footage or credentials are distributed.

Uses Unity's `ScreenCapture`, `AsyncGPUReadback` and listener `OnAudioFilterRead`, plus external FFmpeg. NVIDIA/NVENC, Unity and FFmpeg are trademarks/projects of their respective owners; no affiliation is implied.

Source: MIT; see [LICENSE](LICENSE). External FFmpeg builds have their own licenses. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Run `python3 Tools~/validate.py` and `python3 Tools~/build_release.py` to validate/package without Unity. These checks do not replace live Unity testing.
