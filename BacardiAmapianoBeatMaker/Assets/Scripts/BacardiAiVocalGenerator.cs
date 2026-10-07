using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Adds no separate listener or master processor.
public sealed class BacardiAiVocalGenerator : IDisposable
{
    public const string Hook = "Come closer, don\u2019t let go\nMove with me, nice and slow\nAll night, feel the glow\nWhere we\u2019re going, only we know";
    public const string Chant = "Come closer\nMove with me\nAll night\nStay with me";
    public const string IsiZuluPronunciation = "Native South African isiZulu pronunciation. Natural isiZulu vowel sounds and consonants. Avoid English, American, or European accenting. Keep the lyrics exactly unchanged.";
    public string PronunciationGuidance = "";
    public string AiPronunciationText = "";
    public bool StrictLyrics, SlowerPhraseDensity;
    public string DisplayedAiPronunciationText { get; private set; } = "";
    const string PronunciationTextControlName = "Bacardi.AiPronunciationText";
    int pronunciationTextEditorId;
    public string GenerationLyricsSource { get { return !(Provider is MockAiVocalProvider) && !string.IsNullOrWhiteSpace(AiPronunciationText) ? "PRONUNCIATION TEXT" : "ORIGINAL LYRICS"; } }
    public void CopyOriginalLyricsToPronunciationText() {
        AiPronunciationText = lyrics;
        if (Event.current != null && pronunciationTextEditorId != 0) {
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), pronunciationTextEditorId);
            editor.text = AiPronunciationText;
            editor.cursorIndex = editor.selectIndex = 0;
            editor.scrollOffset = Vector2.zero;
            GUI.FocusControl(null);
        }
    }
    public Rect GuidanceLabelRect { get; private set; }
    public Rect GuidanceFieldRect { get; private set; }
    public Rect GuidancePresetRect { get; private set; }
    public Rect LyricsRect { get; private set; }
    public Rect GenerateRect { get; private set; }
    public Rect RegenerateRect { get; private set; }
    public Rect StrictLyricsRect { get; private set; }
    public Rect SlowerPhraseDensityRect { get; private set; }
    public Rect WordPreservationStatusRect { get; private set; }
    public string WordPreservationStatus { get { return "STRICT LYRICS: " + (StrictLyrics ? "ON" : "OFF") + "\nSLOWER PHRASE DENSITY: " + (SlowerPhraseDensity ? "ON" : "OFF"); } }
    public int GuidanceRepaintCount { get; private set; }
    public string DisplayedPronunciationGuidance { get; private set; } = "";
    int guidanceEditorId;
    public void ApplyIsiZuluPronunciation() {
        PronunciationGuidance = IsiZuluPronunciation;
        // A focused IMGUI TextEditor keeps its own editing buffer. Synchronize
        // that buffer before it can overwrite a programmatic preset next frame.
        if (Event.current != null) {
            if (guidanceEditorId != 0) {
                var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), guidanceEditorId);
                editor.text = PronunciationGuidance;
                editor.cursorIndex = editor.selectIndex = 0;
                editor.scrollOffset = Vector2.zero;
            }
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }
    }
    static Rect ScreenRect(Rect rect) { return new Rect(GUIUtility.GUIToScreenPoint(rect.position), rect.size); }
    public readonly BacardiVocalTimeline Timeline = new BacardiVocalTimeline();
    public IAiVocalProvider Provider { get; set; } = new MockAiVocalProvider();
    public sealed class Placement {
        public AiVocalResult Vocal;
        public int StartBar;
        public AudioSource Source;
        public bool Scheduled;
        public double ScheduledDsp;
    }
    public readonly List<Placement> Placements = new List<Placement>();
    public readonly List<string> ScheduleLog = new List<string>();
    readonly GameObject owner;
    readonly Dictionary<AiVocalResult, AudioClip> clips = new Dictionary<AiVocalResult, AudioClip>();
    readonly ConcurrentQueue<AiVocalProgress> progress = new ConcurrentQueue<AiVocalProgress>();
    sealed class ProgressSink : IProgress<AiVocalProgress> {
        readonly ConcurrentQueue<AiVocalProgress> queue;
        public ProgressSink(ConcurrentQueue<AiVocalProgress> queue) { this.queue = queue; }
        public void Report(AiVocalProgress value) { queue.Enqueue(value); }
    }
    AudioSource preview;
    AudioReverbFilter reverb;
    CancellationTokenSource cancellation;
    Task<AiVocalResult> generation;
    Task<string> export;
    Task<string> connectionTest;
    public string ConnectionStatus { get; private set; } = "";
    public bool TestingConnection { get { return connectionTest != null; } }
    public void TestRealConnection() {
        if (TestingConnection || disposed) return;
        try {
            var real = Provider as ElevenLabsAiVocalProvider ?? new ElevenLabsAiVocalProvider();
            ConnectionStatus = "CHECKING PROXY: " + real.ProxyUrl;
            connectionTest = real.TestConnectionAsync();
        } catch (Exception e) { ConnectionStatus = "PROXY CONNECTION ERROR: " + AiVocalFailure.Clean(e.ToString()); }
    }
    public AiVocalResult Result { get; private set; }
    public string State { get; private set; } = "READY";
    public string Message { get; private set; } = "";
    float fraction;
    bool disposed;
    string lyrics = Hook, startText = "13", songText = "32";
    readonly System.Random lyricRandom = new System.Random();
    public string Lyrics { get { return lyrics; } }
    public AiVocalRequest CurrentRequest(double bpm) {
        return new AiVocalRequest { Lyrics = lyrics, PronunciationGuidance = PronunciationGuidance, AiPronunciationText = AiPronunciationText, StrictLyrics = StrictLyrics, SlowerPhraseDensity = SlowerPhraseDensity, Bpm = bpm, Voice = Voices[voice], Role = Roles[role],
            Key = key == 0 ? "C" : "A", Scale = key == 0 ? "Major" : "Minor", Bars = Lengths[length],
            Energy = energy, Emotion = emotion, Dry = dry, RelativeMinor = minorFlavor };
    }
    public void GenerateNewLyrics() {
        if (Busy || disposed) return;
        lyrics = BacardiHookLyrics.Generate(lyricRandom, lyrics);
        Message = "New hook ready. Edit the lyrics or click GENERATE VOCAL to sing them. Existing audio still uses its previous lyrics.";
    }
    int providerChoice;
    bool replacePlacedOnCompletion; AiVocalResult replacePrevious;
    int voice, role, key, length = 1;
    float energy = .45f, emotion = .6f;
    bool dry = true, lightReverb, minorFlavor = true;
    static readonly string[] Voices = { "Female", "Male" }, Roles = { "Lead", "Harmony", "Ad-lib" };
    static readonly int[] Lengths = { 2, 4, 8, 16 };
    public bool Busy { get { return generation != null || export != null; } }
    public BacardiAiVocalGenerator(GameObject owner) {
        this.owner = owner; preview = NewSource();
        reverb = preview.gameObject.AddComponent<AudioReverbFilter>();
        reverb.reverbPreset = AudioReverbPreset.Room;
        reverb.dryLevel = 0; reverb.room = -2500; reverb.reverbLevel = -2200; reverb.enabled = false;
    }
    AudioSource NewSource() {
        var child = new GameObject("AI Vocal Layer"); child.transform.SetParent(owner.transform, false);
        var source = child.AddComponent<AudioSource>();
        source.playOnAwake = false; source.spatialBlend = 0; source.loop = false; return source;
    }
    AudioClip Clip(AiVocalResult vocal) {
        if (vocal == null || vocal.Pcm == null || vocal.Pcm.Length == 0 || vocal.Pcm.Length % 2 != 0 || vocal.SampleRate != 44100 || vocal.Request == null)
            throw new ArgumentException("Provider must return non-empty 44.1 kHz interleaved stereo PCM and generation settings.");
        AudioClip clip;
        if (!clips.TryGetValue(vocal, out clip)) {
            clip = AudioClip.Create("AI " + vocal.Request.Role + (vocal.IsMock ? " [MOCK]" : ""), vocal.Pcm.Length / 2, 2, 44100, false);
            if (!clip.SetData(vocal.Pcm, 0)) { UnityEngine.Object.Destroy(clip); throw new Exception("Could not load vocal PCM."); }
            clips.Add(vocal, clip);
        }
        return clip;
    }
    public void Generate(AiVocalRequest request, bool replacePlaced = false) {
        if (Busy) return;
        replacePlacedOnCompletion = replacePlaced; replacePrevious = Result;
        StopPreview(); cancellation = new CancellationTokenSource();
        State = "GENERATING..."; Message = Provider.Name; fraction = 0;
        try { generation = Provider.GenerateAsync(request, new ProgressSink(progress), cancellation.Token); }
        catch (Exception e) { cancellation.Dispose(); cancellation = null; Fail(e, "generation"); }
    }
    public void Cancel() { if (cancellation != null) cancellation.Cancel(); }
    public void Play(double dspStart, double bpm) {
        Timeline.Play(dspStart, bpm);
        foreach (var p in Placements) { if (p.Source != null) p.Source.Stop(); p.Scheduled = false; }
    }

    public void Pause(double now) {
        Timeline.Pause(now);
        foreach (var p in Placements) { if (p.Source != null) p.Source.Stop(); p.Scheduled = false; }
    }
    public void Stop() {
        Timeline.Stop();
        foreach (var p in Placements) { if (p.Source != null) p.Source.Stop(); p.Scheduled = false; }
    }
    public void Tick(double now, double bpm, double nextBeatBoundary, float masterVolume) {
        if (disposed) return;
        if (connectionTest != null && connectionTest.IsCompleted) {
            ConnectionStatus = connectionTest.IsFaulted ? "PROXY CONNECTION ERROR: " + AiVocalFailure.Clean(connectionTest.Exception.ToString()) : connectionTest.Result;
            connectionTest = null;
            Debug.Log(ConnectionStatus);
        }

        AiVocalProgress update;
        while (progress.TryDequeue(out update)) { State = update.State; fraction = update.Fraction; }
        if (generation != null && generation.IsCompleted) {
            var task = generation; generation = null; cancellation.Dispose(); cancellation = null;
            if (task.IsCanceled) { State = Result == null ? "READY" : "READY TO PREVIEW"; Message = "Generation cancelled."; }
            else if (task.IsFaulted) { Fail(task.Exception.InnerException, "generation"); }
            else {
                try { Result = task.Result; Clip(Result); if (replacePlacedOnCompletion && replacePrevious != null) ReplacePlacements(replacePrevious); State = "READY TO PREVIEW"; fraction = 1; Message = Result.Provider; ReleaseUnusedClips(); }
                catch (Exception e) { Fail(e, "file loading"); }
            }
        }
        if (export != null && export.IsCompleted) {
            var task = export; export = null;
            if (task.IsFaulted) { Fail(task.Exception.InnerException, "file loading"); }
            else { State = "READY TO PREVIEW"; fraction = 1; Message = "Saved: " + task.Result; }
        }
        double boundary = Math.Max(now, nextBeatBoundary);
        if (Math.Abs(Timeline.Bpm - bpm) > .000001) {
            Timeline.SetTempo(bpm, boundary);
            foreach (var p in Placements) {
                if (!p.Scheduled) continue;
                if (p.ScheduledDsp >= boundary) { p.Source.Stop(); p.Scheduled = false; }
                else { p.Source.SetScheduledEndTime(boundary); }
            }
            if (State != "ERROR") Message = "Tempo changed. REGENERATE at " + bpm + " BPM to update matching placements. No automatic paid generation or pitch shift.";
        }
        if (!Timeline.Running) return;
        foreach (var p in Placements) {
            double firstBeat = (p.StartBar - 1) * 4.0, endBeat = firstBeat + p.Vocal.Request.Bars * 4;
            double position = Timeline.BeatsAt(now); p.Source.volume = masterVolume;

            if (p.Scheduled || position >= endBeat || Math.Abs(Timeline.Bpm - p.Vocal.Request.Bpm) > .000001) continue;
            double start = Timeline.DspAtBeat(firstBeat);
            if (start > now + .2) continue;
            bool continuation = start < now;
            if (continuation) start = now + .03;
            double offsetBeats = Math.Max(0, Timeline.BeatsAt(start) - firstBeat);
            int offsetFrames = (int)Math.Round(offsetBeats * 60 / p.Vocal.Request.Bpm * 44100);
            if (offsetFrames >= p.Vocal.Pcm.Length / 2) continue;
            p.Source.clip = Clip(p.Vocal); p.Source.pitch = 1;
            p.Source.timeSamples = offsetFrames; p.Source.PlayScheduled(start);
            p.Scheduled = true; p.ScheduledDsp = start;
            string log = string.Format(CultureInfo.InvariantCulture,
                "AI vocal Bar {0}: DSP={1:F9}, musicalBeat={2:F6}, offsetFrames={3}, {4}",
                p.StartBar, start, Timeline.BeatsAt(start), offsetFrames, continuation ? "resume/late join" : "bar trigger");
            ScheduleLog.Add(log); Debug.Log(log);
        }
    }
    public void Preview(double bpm) {
        if (Result == null) throw new InvalidOperationException("Generate a vocal first.");
        AiVocalWav.RequireTempo(Result, bpm);
        preview.Stop(); preview.clip = Clip(Result); preview.pitch = 1; preview.volume = 1;
        reverb.enabled = lightReverb; preview.Play();
    }
    void ReplacePlacements(AiVocalResult previous) {
        foreach (var p in Placements) if (p.Vocal == previous) {
            p.Source.Stop(); p.Scheduled = false; p.Vocal = Result;
        }
        ReleaseUnusedClips();
    }
    public void StopPreview() { if (preview != null) preview.Stop(); }
    public void AddToSong(int startBar) {
        if (Result == null) throw new InvalidOperationException("Generate a vocal first.");
        if (startBar < 1 || startBar > 256) throw new ArgumentException("Start bar must be 1-256.");
        if (Placements.Count >= 16) throw new ArgumentException("Maximum 16 placements. Remove one first.");
        Placements.Add(new Placement { Vocal = Result, StartBar = startBar, Source = NewSource() });
        Message = "Added separate " + Result.Request.Role + " layer at Bar " + startBar + " through Bar " + (startBar + Result.Request.Bars) + " (exclusive).";
    }
    public void Save(bool aligned, double bpm, int startBar, int songBars) {
        if (Result == null || Busy) return;
        if (startBar < 1 || startBar > 256 || songBars < 1 || songBars > 512)
            throw new ArgumentException("Start bar: 1-256; song length: 1-512 bars.");
        var current = Result;
        var placements = new List<Placement>(Placements);
        string folder = Path.Combine(Application.persistentDataPath, "Exports",
            "Bacardi-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6), "AI Vocals");
        State = "PROCESSING..."; fraction = .9f;
        export = Task.Run(() => {
            Directory.CreateDirectory(folder);
            string name = "AI_VOCAL_" + current.Request.Role.ToUpperInvariant().Replace("-", "").Replace(" ", "") + "_" +
                current.Request.Bpm.ToString("0.##", CultureInfo.InvariantCulture) + "BPM_" + current.Request.Key + current.Request.Scale.ToUpperInvariant();
            if (!aligned) {
                string path = Path.Combine(folder, name + ".wav"); AiVocalWav.Write(path, current.Pcm);
                File.WriteAllText(Path.Combine(folder, "VOCAL_INFO.txt"), AiVocalWav.Info(current)); return path;
            }
            if (placements.Count == 0) placements.Add(new Placement { Vocal = current, StartBar = startBar });
            int neededBars = songBars;
            foreach (var p in placements) neededBars = Math.Max(neededBars, p.StartBar - 1 + p.Vocal.Request.Bars);
            float[] stem = new float[checked((int)BacardiVocalTimeline.FrameAtBar(neededBars + 1, bpm) * 2)];
            var info = new System.Text.StringBuilder("ALIGNED VOCAL STEM\nCurrent export BPM: " + bpm.ToString(CultureInfo.InvariantCulture) +
                "\n4/4. Start at song Bar 1. Song bars: " + neededBars + "\n");
            foreach (var p in placements) {
                var track = AiVocalWav.Aligned(p.Vocal, p.StartBar, neededBars, bpm);
                for (int n = 0; n < stem.Length; n++) stem[n] += track[n];
                info.AppendLine("Placement Bar " + p.StartBar + " / start frame " + BacardiVocalTimeline.FrameAtBar(p.StartBar, bpm));
                info.AppendLine(AiVocalWav.Info(p.Vocal));
            }
            double peak = 0; foreach (float x in stem) peak = Math.Max(peak, Math.Abs(x));
            double trim = peak > .5 ? .5 / peak : 1;
            if (trim < 1) for (int n = 0; n < stem.Length; n++) stem[n] *= (float)trim;
            info.AppendLine("Overlap-only peak headroom trim: " + trim.ToString("R", CultureInfo.InvariantCulture));
            info.AppendLine("Stem LUFS-I: " + BacardiMastering.Measure(stem, 44100).ToString("F2", CultureInfo.InvariantCulture));
            info.AppendLine("Stem peak dBFS: " + (20 * Math.Log10(Math.Max(1e-20, peak * trim))).ToString("F2", CultureInfo.InvariantCulture));
            info.AppendLine("Constant export tempo; regenerate each vocal at export BPM. No resampling or pitch shifting.");
            string output = Path.Combine(folder, "AI_VOCAL_ALIGNED_" + bpm.ToString("0.##", CultureInfo.InvariantCulture) + "BPM.wav");
            AiVocalWav.Write(output, stem); File.WriteAllText(Path.Combine(folder, "VOCAL_INFO.txt"), info.ToString()); return output;
        });
    }
    void ReleaseUnusedClips() {
        var unused = new List<AiVocalResult>();
        foreach (var pair in clips) {
            if (pair.Key == Result) continue;
            bool used = false; foreach (var p in Placements) if (p.Vocal == pair.Key) used = true;
            if (!used) unused.Add(pair.Key);
        }
        foreach (var r in unused) { UnityEngine.Object.Destroy(clips[r]); clips.Remove(r); }
    }
    public string StatusText { get { return State == "ERROR" ? Message : State + " | " + Mathf.RoundToInt(fraction * 100) + "%"; } }
    void Fail(Exception error, string stage) {
        var failure = error as AiVocalFailure ?? new AiVocalFailure(stage, 0, error.Message, error);
        State = "ERROR"; Message = failure.UiMessage; fraction = 0;
        Debug.LogError(failure.Diagnostic);
    }
    void Action(Action action) { try { action(); } catch (Exception e) { Fail(e, "file loading"); } }
    static int Parse(string text, string name) {
        int value; if (!int.TryParse(text, out value)) throw new ArgumentException(name + " must be a whole number."); return value;
    }
    public void Draw(double bpm, string masterReadout, float visiblePanelWidth = 0) {
        GUILayout.Space(18); GUILayout.Label("AI VOCAL GENERATOR", GUI.skin.box);
        GUILayout.Label((Provider is MockAiVocalProvider ? "LOCAL MOCK: synthetic vowel melody; lyrics are metadata." : "Provider: " + Provider.Name) + " Style: Smooth Amapiano / Afro-house.");
        double now = AudioSettings.dspTime;
        GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "4/4 | Tempo {0:0} BPM | Bar {1} | Beat {2:F2} | Musical beats {3:F2} | Elapsed {4:F2}s",
            bpm, Timeline.BarAt(now), Timeline.BeatInBarAt(now), Timeline.BeatsAt(now), Timeline.ElapsedAt(now)));
        bool enabled = GUI.enabled; GUI.enabled = enabled && !Busy;
        int selectedProvider = GUILayout.SelectionGrid(providerChoice, new[] { "MOCK", "REAL AI" }, 2);
        if (selectedProvider != providerChoice) Action(() => {
            Provider = selectedProvider == 0 ? (IAiVocalProvider)new MockAiVocalProvider() : new ElevenLabsAiVocalProvider();
            providerChoice = selectedProvider;
            Message = "Selected " + Provider.Name + ". No request is sent until GENERATE or REGENERATE.";
        });
        if (providerChoice == 1) {
            GUI.enabled = enabled && !TestingConnection;
            if (GUILayout.Button("TEST REAL AI CONNECTION")) TestRealConnection();
            GUI.enabled = enabled && !Busy;
            GUILayout.Label(ConnectionStatus, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.Label("REAL AI uses ElevenLabs Music + vocal stem separation via your backend. Generation/separation may incur API charges. Errors never silently fall back to MOCK.");
            GUILayout.Label("Selected vocal model may not support this language. Pronunciation text, when supplied, replaces the sung words sent to REAL AI. Original lyrics are preserved separately.", new GUIStyle(GUI.skin.label) { wordWrap = true });
        }
        GUILayout.BeginHorizontal();
        voice = GUILayout.SelectionGrid(voice, Voices, 2);
        role = GUILayout.SelectionGrid(role, Roles, 3);
        key = GUILayout.SelectionGrid(key, new[] { "C Major", "A Minor" }, 2);
        length = GUILayout.SelectionGrid(length, new[] { "2 bars", "4 bars", "8 bars", "16 bars" }, 4);
        GUILayout.EndHorizontal();
        minorFlavor = GUILayout.Toggle(minorFlavor, "A minor flavor with C Major");
        GUILayout.Label("Character: warm, intimate, airy, clear, soulful, hypnotic");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Energy " + energy.ToString("F2")); energy = GUILayout.HorizontalSlider(energy, 0, 1);
        GUILayout.Label("Emotion " + emotion.ToString("F2")); emotion = GUILayout.HorizontalSlider(emotion, 0, 1);
        dry = GUILayout.Toggle(dry, "Dry vocal"); lightReverb = GUILayout.Toggle(lightReverb, "Light reverb preview only");
        GUILayout.EndHorizontal();
        if (reverb != null) reverb.enabled = lightReverb;
        GUILayout.Label("Original Lyrics (multiline; select, copy, cut and Ctrl+V paste)"); lyrics = BacardiLyricsInput.Draw(lyrics);
        if (Event.current.type == EventType.Repaint) LyricsRect = ScreenRect(GUILayoutUtility.GetLastRect());
        GUILayout.Label("PRONUNCIATION / ACCENT GUIDANCE", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold }, GUILayout.Height(24));
        if (Event.current.type == EventType.Repaint) GuidanceLabelRect = ScreenRect(GUILayoutUtility.GetLastRect());
        GUI.SetNextControlName("Bacardi.PronunciationGuidance");
        var guidanceStyle = new GUIStyle(GUI.skin.textArea) { wordWrap = true, alignment = TextAnchor.UpperLeft, fontSize = 14 };
        guidanceStyle.normal.textColor = guidanceStyle.hover.textColor = guidanceStyle.active.textColor = guidanceStyle.focused.textColor = Color.white;
        Color savedContentColor = GUI.contentColor;
        GUI.contentColor = Color.white;
        string visibleGuidance = PronunciationGuidance ?? "";
        PronunciationGuidance = GUILayout.TextArea(visibleGuidance, guidanceStyle, GUILayout.Height(72), GUILayout.ExpandWidth(true));
        GUI.contentColor = savedContentColor;
        if (GUI.GetNameOfFocusedControl() == "Bacardi.PronunciationGuidance") guidanceEditorId = GUIUtility.keyboardControl;
        if (Event.current.type == EventType.Repaint) {
            GuidanceFieldRect = ScreenRect(GUILayoutUtility.GetLastRect());
            DisplayedPronunciationGuidance = visibleGuidance;
        }
        if (GUILayout.Button("SOUTH AFRICAN / ISIZULU", GUILayout.Height(30))) ApplyIsiZuluPronunciation();
        if (Event.current.type == EventType.Repaint) {
            GuidancePresetRect = ScreenRect(GUILayoutUtility.GetLastRect());
            GuidanceRepaintCount++;
        }
        GUILayout.Label("AI PRONUNCIATION TEXT (optional; REAL AI only)");
        AiPronunciationText = BacardiLyricsInput.Draw(AiPronunciationText, PronunciationTextControlName, 90);
        if (Event.current.type == EventType.Repaint) DisplayedAiPronunciationText = AiPronunciationText;
        if (GUI.GetNameOfFocusedControl() == PronunciationTextControlName) pronunciationTextEditorId = GUIUtility.keyboardControl;
        GUILayout.Label("Pronunciation text changes what the AI receives as sung words; original lyrics are preserved separately.", new GUIStyle(GUI.skin.label) { wordWrap = true });
        if (GUILayout.Button("COPY ORIGINAL LYRICS")) CopyOriginalLyricsToPronunciationText();
        // Reserve layout space in the parent's scroll view. Explicit row heights
        // keep the controls and both status lines visible on narrow Game views.
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.MinHeight(130),
            visiblePanelWidth > 0 ? GUILayout.Width(visiblePanelWidth) : GUILayout.ExpandWidth(true));
        var wordToggleStyle = new GUIStyle(GUI.skin.toggle) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
        GUI.SetNextControlName("Bacardi.StrictLyrics");
        StrictLyrics = GUILayout.Toggle(StrictLyrics, "STRICT LYRICS / WORD PRESERVATION", wordToggleStyle, GUILayout.Height(36), GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint) StrictLyricsRect = ScreenRect(GUILayoutUtility.GetLastRect());
        GUI.SetNextControlName("Bacardi.SlowerPhraseDensity");
        SlowerPhraseDensity = GUILayout.Toggle(SlowerPhraseDensity, "SLOWER PHRASE DENSITY", wordToggleStyle, GUILayout.Height(36), GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint) SlowerPhraseDensityRect = ScreenRect(GUILayoutUtility.GetLastRect());
        GUILayout.Label(WordPreservationStatus, new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true }, GUILayout.Height(42));
        if (Event.current.type == EventType.Repaint) WordPreservationStatusRect = ScreenRect(GUILayoutUtility.GetLastRect());
        GUILayout.EndVertical();
        GUILayout.Label("REAL AI guidance only; project BPM unchanged." + (Provider is MockAiVocalProvider ? " MOCK ignores these options." : ""), new GUIStyle(GUI.skin.label) { wordWrap = true });
        GUILayout.Label("GENERATE VOCAL / REGENERATE will use: " + GenerationLyricsSource);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("GENERATE NEW LYRICS")) GenerateNewLyrics();
        if (GUILayout.Button("DEFAULT HOOK")) lyrics = Hook;
        if (GUILayout.Button("SHORT CHANT")) lyrics = Chant;
        GUILayout.EndHorizontal();
        var request = CurrentRequest(bpm);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("GENERATE VOCAL — " + GenerationLyricsSource)) Generate(request);
        if (Event.current.type == EventType.Repaint) GenerateRect = ScreenRect(GUILayoutUtility.GetLastRect());
        if (GUILayout.Button("REGENERATE — " + GenerationLyricsSource)) Generate(request, true);
        if (Event.current.type == EventType.Repaint) RegenerateRect = ScreenRect(GUILayoutUtility.GetLastRect());
        GUI.enabled = enabled && generation != null;
        if (GUILayout.Button("CANCEL")) Cancel();
        GUI.enabled = enabled && !Busy && Result != null;
        if (GUILayout.Button("PREVIEW")) Action(() => Preview(bpm));
        GUI.enabled = enabled;
        if (GUILayout.Button("STOP PREVIEW")) StopPreview();
        GUILayout.EndHorizontal();
        GUILayout.Label(request.Prompt, new GUIStyle(GUI.skin.label) { wordWrap = true });
        GUILayout.BeginHorizontal();
        GUILayout.Label("Start bar", GUILayout.Width(65)); startText = GUILayout.TextField(startText, 3, GUILayout.Width(60));
        if (GUILayout.Button("HOOK 1: 13")) startText = "13";
        if (GUILayout.Button("HOOK 2: 17")) startText = "17";
        GUILayout.Label("Song bars", GUILayout.Width(70)); songText = GUILayout.TextField(songText, 3, GUILayout.Width(60));
        GUI.enabled = enabled && !Busy && Result != null;
        if (GUILayout.Button("ADD TO SONG")) Action(() => AddToSong(Parse(startText, "Start bar")));
        if (GUILayout.Button("SAVE VOCAL WAV")) Action(() => Save(false, bpm, Parse(startText, "Start bar"), Parse(songText, "Song bars")));
        if (GUILayout.Button("EXPORT VOCAL STEM")) Action(() => Save(true, bpm, Parse(startText, "Start bar"), Parse(songText, "Song bars")));
        GUI.enabled = enabled; GUILayout.EndHorizontal();
        for (int i = 0; i < Placements.Count; i++) {
            var p = Placements[i]; GUILayout.BeginHorizontal();
            GUILayout.Label(p.Vocal.Request.Role + " / Bars " + p.StartBar + "-" + (p.StartBar + p.Vocal.Request.Bars) + " (end exclusive)");
            if (GUILayout.Button("REMOVE", GUILayout.Width(90))) {
                p.Source.Stop(); UnityEngine.Object.Destroy(p.Source.gameObject); Placements.RemoveAt(i--); ReleaseUnusedClips();
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(StatusText, new GUIStyle(GUI.skin.label) { wordWrap = true });
        Rect rect = GUILayoutUtility.GetRect(1, 6, GUILayout.ExpandWidth(true));
        GUI.Box(rect, ""); GUI.Box(new Rect(rect.x, rect.y, rect.width * fraction, rect.height), "");
        GUILayout.Label(Message, new GUIStyle(GUI.skin.label) { wordWrap = true });
        if (Result != null) GUILayout.Label("Current audio: " + Result.Provider + " / " + Result.Request.Bpm + " BPM | Vocal peak: " + Result.PeakDb.ToString("F1") + " dBFS | Vocal LUFS-I: " + Result.Integrated.ToString("F1"));
        GUILayout.Label("Full master: " + masterReadout);
        GUILayout.Label("-14 LUFS is a MASTER target, not a vocal-stem target. Full-song sources use the existing listener mastering path.");
        GUILayout.Label("Beat/MIDI exports retain their existing one-bar behavior. EXPORT VOCAL STEM writes the separate aligned vocal arrangement. Tempo edits require explicit regeneration at the new BPM; pitch stays unchanged. REGENERATE updates placements referencing the previous current vocal, retaining their bar numbers.");
        GUI.enabled = enabled;
    }
    public void Dispose() {
        if (disposed) return; disposed = true;
        if (cancellation != null) cancellation.Cancel();
        StopPreview(); Stop();
        if (preview != null) UnityEngine.Object.Destroy(preview.gameObject);
        foreach (var p in Placements) if (p.Source != null) UnityEngine.Object.Destroy(p.Source.gameObject);
        foreach (var clip in clips.Values) UnityEngine.Object.Destroy(clip);
        if (generation != null) {
            var pendingCancellation = cancellation;
            generation.ContinueWith(t => { var ignored = t.Exception; pendingCancellation.Dispose(); }, TaskScheduler.Default);
        }
        if (export != null) export.ContinueWith(t => { var ignored = t.Exception; }, TaskScheduler.Default);
    }
}


