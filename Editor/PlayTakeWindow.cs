using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace PlayTake
{
    [Serializable]
    public sealed class RecordingOptions
    {
        public int width = 1920, height = 1080, fps = 30, quality = 20;
        public bool audio = true, nvenc = true;
        public string folder = "", ffmpeg = "";
    }

    public sealed class PlayTakeWindow : EditorWindow
    {
        const string Key = "PlayTake.Options.v1";
        RecordingOptions options;
        [MenuItem("Window/PlayTake/Recorder")]
        public static void Open() { GetWindow<PlayTakeWindow>("PlayTake Recorder"); }
        static RecordingOptions LoadOptions()
        {
            RecordingOptions result;
            try { result = JsonUtility.FromJson<RecordingOptions>(EditorPrefs.GetString(Key, "{}")) ?? new RecordingOptions(); }
            catch (ArgumentException) { result = new RecordingOptions(); }
            if (string.IsNullOrEmpty(result.folder)) result.folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Unity");
            if (string.IsNullOrEmpty(result.ffmpeg)) result.ffmpeg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft/WinGet/Links/ffmpeg.exe");
            return result;
        }
        void OnEnable()
        {
            options = LoadOptions();
            minSize = new Vector2(480, 620);
        }
        void OnInspectorUpdate() { Repaint(); }
        void Save() { EditorPrefs.SetString(Key, JsonUtility.ToJson(options)); }
        void OnGUI()
        {
            if (options == null) OnEnable();
            EditorGUILayout.LabelField("Record the Game view", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Enter Play mode and keep the Game view visible. Records Game view pixels, including UI, and optional Unity audio. No microphone or desktop audio.", MessageType.Info);
            using (new EditorGUI.DisabledScope(PlayTakeRecorder.IsBusy))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("720p")) { options.width = 1280; options.height = 720; Save(); }
                if (GUILayout.Button("1080p")) { options.width = 1920; options.height = 1080; Save(); }
                if (GUILayout.Button("1440p")) { options.width = 2560; options.height = 1440; Save(); }
                EditorGUILayout.EndHorizontal();
                options.width = EditorGUILayout.IntField("Width", options.width);
                options.height = EditorGUILayout.IntField("Height", options.height);
                options.fps = EditorGUILayout.IntPopup("Frame rate", options.fps, new[] { "24", "30", "60" }, new[] { 24, 30, 60 });
                options.quality = EditorGUILayout.IntSlider(new GUIContent("Quality (CQ)", "Lower values mean higher quality and larger files."), options.quality, 16, 30);
                options.nvenc = EditorGUILayout.Toggle("NVIDIA NVENC", options.nvenc);
                options.audio = EditorGUILayout.Toggle("Record Unity audio", options.audio);
                options.folder = EditorGUILayout.TextField("Output folder", options.folder);
                if (GUILayout.Button("Choose folder…")) { var p = EditorUtility.OpenFolderPanel("Output folder", options.folder, ""); if (!string.IsNullOrEmpty(p)) { options.folder = p; Save(); } }
                options.ffmpeg = EditorGUILayout.TextField("FFmpeg", options.ffmpeg);
                if (EditorGUI.EndChangeCheck()) Save();
            }
            if (Screen.height > 0 && Mathf.Abs((float)options.width / options.height - (float)Screen.width / Screen.height) > .02f)
                EditorGUILayout.HelpBox("Game view aspect ratio differs. Black bars preserve proportions.", MessageType.Info);
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || PlayTakeRecorder.IsBusy))
                if (GUILayout.Button("● Start recording", GUILayout.Height(34))) { Save(); PlayTakeRecorder.Start(options); }
            using (new EditorGUI.DisabledScope(!PlayTakeRecorder.IsRecording))
                if (GUILayout.Button("■ Stop and save", GUILayout.Height(30))) PlayTakeRecorder.Stop();
            EditorGUILayout.HelpBox(PlayTakeRecorder.Status, PlayTakeRecorder.Failed ? MessageType.Error : MessageType.None);
            if (PlayTakeRecorder.IsRecording) EditorGUILayout.LabelField($"{PlayTakeRecorder.Seconds:0.0} s · captured {PlayTakeRecorder.Captured} · dropped {PlayTakeRecorder.Dropped}");
            if (GUILayout.Button("Open output folder")) { Directory.CreateDirectory(options.folder); EditorUtility.RevealInFinder(options.folder); }
            if (!string.IsNullOrEmpty(PlayTakeRecorder.LastFile)) EditorGUILayout.SelectableLabel(PlayTakeRecorder.LastFile, GUILayout.Height(35));
            EditorGUILayout.LabelField("Ctrl+Shift+F10 — start / stop", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Keep the Game view visible while recording.", EditorStyles.miniLabel);
        }
        [MenuItem("Window/PlayTake/Start or Stop Recording %#F10")]
        static void Toggle()
        {
            if (PlayTakeRecorder.IsRecording) PlayTakeRecorder.Stop();
            else if (!PlayTakeRecorder.IsBusy)
            {
                PlayTakeRecorder.Start(LoadOptions());
            }
        }
    }

    [InitializeOnLoad]
    public static class PlayTakeRecorder
    {
        sealed class Frame { public byte[] data; public double time; }
        sealed class Sound { public float[] data; public double time; }
        sealed class Session
        {
            public RecordingOptions o;
            public Process encoder;
            public Task writer, soundWriter, finalizer;
            public readonly ConcurrentQueue<byte[]> pool = new ConcurrentQueue<byte[]>();
            public readonly BlockingCollection<Frame> frames = new BlockingCollection<Frame>(4);
            public readonly BlockingCollection<Sound> sounds = new BlockingCollection<Sound>(128);
            public readonly Stopwatch clock = Stopwatch.StartNew();
            public RenderTexture source, output;
            public PlayTakeLoop loop;
            public PlayTakeAudioTap tap;
            public int pending, captured, dropped, rate, sourceWidth, sourceHeight;
            public long soundFrames;
            public double nextFrame, duration, dspStart;
            public volatile bool stopping;
            public string outputPath, videoPath, audioPath, error;
            public readonly StringBuilder stderr = new StringBuilder();
        }
        static Session session;
        public static string Status { get; private set; } = "Ready. Enter Play mode to record.";
        public static bool Failed { get; private set; }
        public static string LastFile { get; private set; }
        public static bool IsBusy => session != null;
        public static bool IsRecording => session != null && !session.stopping;
        public static double Seconds => session?.clock.Elapsed.TotalSeconds ?? 0;
        public static int Captured => session?.captured ?? 0;
        public static int Dropped => session?.dropped ?? 0;
        static PlayTakeRecorder()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode) Stop(); };
            AssemblyReloadEvents.beforeAssemblyReload += FlushBeforeReload;
            EditorApplication.quitting += FlushBeforeReload;
        }
        static string Quote(string value) => "\"" + value.Replace("\"", "") + "\"";
        public static void Start(RecordingOptions options)
        {
            if (IsBusy) return;
            Failed = false;
            if (Application.platform != RuntimePlatform.WindowsEditor) { Fail("This preview supports the Windows Unity Editor only."); return; }
            if (!EditorApplication.isPlaying) { Status = "Enter Play mode first."; return; }
            if (!File.Exists(options.ffmpeg)) { Fail("FFmpeg not found. Set the path to ffmpeg.exe."); return; }
            if (!SystemInfo.supportsAsyncGPUReadback) { Fail("Async GPU readback is not supported by this graphics device."); return; }
            if (options.width < 256 || options.height < 144 || options.width > 3840 || options.height > 2160 || options.width % 2 != 0 || options.height % 2 != 0) { Fail("Use even dimensions between 256x144 and 3840x2160."); return; }
            var s = new Session { o = JsonUtility.FromJson<RecordingOptions>(JsonUtility.ToJson(options)), rate = AudioSettings.outputSampleRate };
            s.o.fps = Mathf.Clamp(s.o.fps, 1, 60); s.o.quality = Mathf.Clamp(s.o.quality, 16, 30);
            try
            {
                Directory.CreateDirectory(s.o.folder);
                var stem = "Unity_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                s.outputPath = Path.Combine(s.o.folder, stem + ".mp4");
                s.videoPath = Path.Combine(s.o.folder, stem + ".video.mp4");
                s.audioPath = Path.Combine(s.o.folder, stem + ".audio.wav");
                string codec = s.o.nvenc ? "-c:v h264_nvenc -preset p4 -rc vbr -cq " + s.o.quality + " -b:v 0" : "-c:v libx264 -threads 2 -preset veryfast -crf " + s.o.quality;
                var args = $"-hide_banner -loglevel error -nostdin -f rawvideo -pixel_format rgba -video_size {s.o.width}x{s.o.height} -framerate {s.o.fps} -i pipe:0 -an -vf scale=out_color_matrix=bt709:out_range=tv {codec} -pix_fmt yuv420p -color_primaries bt709 -color_trc bt709 -colorspace bt709 -movflags +faststart " + Quote(s.videoPath);
                s.encoder = Launch(s.o.ffmpeg, args, true);
                s.encoder.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (s.stderr) s.stderr.AppendLine(e.Data); };
                s.encoder.BeginErrorReadLine();
                s.sourceWidth = Screen.width; s.sourceHeight = Screen.height;
                s.source = new RenderTexture(s.sourceWidth, s.sourceHeight, 0, RenderTextureFormat.ARGB32); s.source.Create();
                s.output = new RenderTexture(s.o.width, s.o.height, 0, RenderTextureFormat.ARGB32); s.output.Create();
                for (int i = 0; i < 6; i++) s.pool.Enqueue(new byte[s.o.width * s.o.height * 4]);
                s.dspStart = AudioSettings.dspTime;
                s.clock.Restart();
                s.writer = Task.Run(() => WriteVideo(s));
                if (s.o.audio) s.soundWriter = Task.Run(() => WriteAudio(s));
                var go = new GameObject("Local video recording") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(go); s.loop = go.AddComponent<PlayTakeLoop>();
                s.loop.Frame = () => { try { Capture(s); } catch (Exception e) { s.error = "Capture: " + e.Message; } };
                session = s; AttachAudio(s); Status = "Recording…";
            }
            catch (Exception e)
            {
                session = s; s.error = e.Message; Stop();
            }
        }
        static Process Launch(string executable, string args, bool input)
        {
            return Process.Start(new ProcessStartInfo(executable, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = input, RedirectStandardError = true });
        }
        static void Fail(string message) { Failed = true; Status = message; Debug.LogError("PlayTake Recorder: " + message); }
        static void AttachAudio(Session s)
        {
            if (!s.o.audio || s.tap != null || s.stopping) return;
            foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.isActiveAndEnabled)
                {
                    s.tap = listener.gameObject.AddComponent<PlayTakeAudioTap>();
                    s.tap.hideFlags = HideFlags.HideAndDontSave;
                    s.tap.Samples = (data, channels, dspTime) =>
                    {
                        if (s.stopping || s.sounds.Count >= 128 || channels < 1) return;
                        int count = data.Length / channels;
                        var stereo = new float[count * 2];
                        for (int i = 0; i < count; i++) { stereo[i * 2] = data[i * channels]; stereo[i * 2 + 1] = data[i * channels + (channels > 1 ? 1 : 0)]; }
                        try { s.sounds.TryAdd(new Sound { data = stereo, time = dspTime - s.dspStart }); }
                        catch (InvalidOperationException) { /* Stop completed the queue during this DSP callback. */ }
                    };
                    break;
                }
        }
        static void Capture(Session s)
        {
            if (s.stopping || s.error != null) return;
            double now = s.clock.Elapsed.TotalSeconds;
            if (now + .0005 < s.nextFrame) return;
            s.nextFrame = Math.Max(s.nextFrame + 1.0 / s.o.fps, now);
            if (Screen.width != s.sourceWidth || Screen.height != s.sourceHeight) { s.error = "Game view size changed. Recording stopped; start a new recording after resizing."; return; }
            if (!s.pool.TryDequeue(out var bytes)) { s.dropped++; return; }
            ScreenCapture.CaptureScreenshotIntoRenderTexture(s.source);
            var old = RenderTexture.active;
            RenderTexture.active = s.output; GL.Clear(true, true, Color.black);
            GL.PushMatrix(); GL.LoadPixelMatrix(0, s.o.width, s.o.height, 0);
            float fit = Mathf.Min((float)s.o.width / s.sourceWidth, (float)s.o.height / s.sourceHeight);
            float w = s.sourceWidth * fit, h = s.sourceHeight * fit;
            Graphics.DrawTexture(new Rect((s.o.width - w) / 2, (s.o.height - h) / 2, w, h), s.source);
            GL.PopMatrix(); RenderTexture.active = old;
            s.pending++;
            AsyncGPUReadback.Request(s.output, 0, TextureFormat.RGBA32, r =>
            {
                try
                {
                    if (r.hasError) { s.error = "GPU readback failed."; s.pool.Enqueue(bytes); }
                    else
                    {
                        r.GetData<byte>().CopyTo(bytes);
                        if (s.frames.TryAdd(new Frame { data = bytes, time = now })) s.captured++;
                        else { s.dropped++; s.pool.Enqueue(bytes); }
                    }
                }
                catch (Exception e) { s.error = e.Message; }
                finally { s.pending--; }
            });
        }
        static void WriteVideo(Session s)
        {
            try
            {
                long written = 0; byte[] previous = null;
                foreach (var f in s.frames.GetConsumingEnumerable())
                {
                    long target = (long)Math.Floor(f.time * s.o.fps);
                    if (target < written) { s.pool.Enqueue(f.data); continue; }
                    while (written < target) { s.encoder.StandardInput.BaseStream.Write(previous ?? f.data, 0, f.data.Length); written++; }
                    s.encoder.StandardInput.BaseStream.Write(f.data, 0, f.data.Length); written++;
                    if (previous != null) s.pool.Enqueue(previous);
                    previous = f.data;
                }
                if (previous != null)
                {
                    while (written < Math.Ceiling(s.duration * s.o.fps)) { s.encoder.StandardInput.BaseStream.Write(previous, 0, previous.Length); written++; }
                    s.pool.Enqueue(previous);
                }
                s.encoder.StandardInput.Close();
                if (!s.encoder.WaitForExit(15000)) { s.encoder.Kill(); throw new IOException("FFmpeg did not finish within 15 seconds."); }
                s.encoder.WaitForExit();
                if (s.encoder.ExitCode != 0) throw new IOException(s.stderr.ToString());
            }
            catch (Exception e) { s.error = "Encoding: " + e.Message; }
        }
        static void WriteAudio(Session s)
        {
            try
            {
                using (var stream = new FileStream(s.audioPath, FileMode.CreateNew, FileAccess.Write))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(new byte[44]); long frames = 0;
                    foreach (var chunk in s.sounds.GetConsumingEnumerable())
                    {
                        long start = Math.Max(0, (long)Math.Round(chunk.time * s.rate));
                        while (frames < start) { writer.Write(0f); writer.Write(0f); frames++; }
                        int skip = (int)Math.Min(chunk.data.Length / 2, Math.Max(0, frames - start));
                        for (int i = skip * 2; i < chunk.data.Length; i++) writer.Write(chunk.data[i]);
                        frames += chunk.data.Length / 2 - skip;
                        s.soundFrames = frames;
                    }
                    long bytes = stream.Length - 44;
                    stream.Position = 0;
                    writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)(bytes + 36)); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    writer.Write(16); writer.Write((ushort)3); writer.Write((ushort)2); writer.Write(s.rate); writer.Write(s.rate * 8); writer.Write((ushort)8); writer.Write((ushort)32);
                    writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((uint)bytes);
                }
            }
            catch (Exception e) { s.error = "Audio capture: " + e.Message; }
        }
        public static void Stop()
        {
            var s = session; if (s == null || s.stopping) return;
            s.stopping = true; s.duration = s.clock.Elapsed.TotalSeconds;
            if (s.loop != null) { s.loop.Frame = null; Object.DestroyImmediate(s.loop.gameObject); }
            if (s.tap != null) { s.tap.Samples = null; Object.DestroyImmediate(s.tap); }
            Status = "Saving MP4…";
        }
        static void BeginFinalizing(Session s)
        {
            if (s.source != null) { s.source.Release(); Object.DestroyImmediate(s.source); }
            if (s.output != null) { s.output.Release(); Object.DestroyImmediate(s.output); }
            s.frames.CompleteAdding(); s.sounds.CompleteAdding();
            s.finalizer = Task.Run(() =>
            {
                try
                {
                    s.writer?.Wait(); s.soundWriter?.Wait();
                    if (s.captured == 0) throw new IOException("No frames captured. Keep the Game view visible and try again.");
                    if (s.encoder == null || s.encoder.ExitCode != 0) throw new IOException(s.error ?? "FFmpeg did not start.");
                    if (s.o.audio && s.soundFrames > 0)
                    {
                        string args = "-hide_banner -loglevel error -nostdin -i " + Quote(s.videoPath) + " -i " + Quote(s.audioPath) + " -map 0:v:0 -map 1:a:0 -c:v copy -c:a aac -b:a 192k -af apad -t " + s.duration.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) + " -movflags +faststart " + Quote(s.outputPath);
                        using (var mux = Launch(s.o.ffmpeg, args, false))
                        {
                            var log = mux.StandardError.ReadToEndAsync();
                            if (!mux.WaitForExit(30000)) { mux.Kill(); throw new IOException("Audio muxing exceeded 30 seconds."); }
                            if (mux.ExitCode != 0) throw new IOException(log.Result);
                        }
                        File.Delete(s.videoPath); File.Delete(s.audioPath);
                    }
                    else { File.Move(s.videoPath, s.outputPath); if (File.Exists(s.audioPath)) File.Delete(s.audioPath); }
                }
                catch (Exception e) { s.error = (s.error ?? "") + " " + e.Message; }
                finally
                {
                    s.encoder?.Dispose();
                    try { File.WriteAllText(s.outputPath + ".recording.json", "{\"duration\":" + s.duration.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ",\"capturedFrames\":" + s.captured + ",\"droppedFrames\":" + s.dropped + ",\"requestedFps\":" + s.o.fps + ",\"audioSampleFrames\":" + s.soundFrames + "}"); } catch (IOException) { }
                }
            });
        }
        static void Update()
        {
            var s = session; if (s == null) return;
            if (!s.stopping)
            {
                AttachAudio(s);
                if (s.error != null || (s.encoder != null && s.encoder.HasExited)) { s.error = s.error ?? s.stderr.ToString(); Stop(); }
            }
            if (s.stopping && s.pending == 0 && s.finalizer == null) BeginFinalizing(s);
            if (s.finalizer != null && s.finalizer.IsCompleted)
            {
                LastFile = File.Exists(s.outputPath) ? s.outputPath : s.videoPath;
                Failed = !string.IsNullOrEmpty(s.error);
                Status = Failed ? "Recording ended with an error: " + s.error + " Temporary files were preserved." : "Saved: " + s.outputPath + (s.o.audio && s.soundFrames == 0 ? " (no audio: no AudioListener samples received)" : "");
                if (Failed) Debug.LogError(Status); else Debug.Log(Status);
                s.frames.Dispose(); s.sounds.Dispose(); session = null;
            }
        }
        static void FlushBeforeReload()
        {
            Stop(); var s = session; if (s == null) return;
            AsyncGPUReadback.WaitAllRequests();
            if (s.finalizer == null) BeginFinalizing(s);
            if (!s.finalizer.Wait(45000)) Debug.LogWarning("PlayTake Recorder ещё завершает сохранение: " + s.videoPath);
        }
    }
}
