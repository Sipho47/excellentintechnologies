using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public partial class BacardiBeatMaker : MonoBehaviour
{
    public AudioClip[] samples = new AudioClip[25];
    public static readonly string[] Names = { "MAIN PIANO", "RHODES", "PAD", "PLUCK", "STRINGS", "Vocal Chop", "Logdrum", "SUB BASS", "Kick", "Clap", "CLOSED HAT", "OPEN HAT", "SHAKER", "PERC 1", "PERC 2", "CONGA", "BONGO", "TAMBOURINE", "Rim", "HAND DRUMS", "LOW FLOOR TOM", "CRASH", "REVERSE CYMBAL", "RISER", "IMPACT" };
    const int Rate = 44100;
    readonly bool[,] steps = new bool[25, 16];
    readonly int[,] velocities = new int[25, 16];
    const int DefaultVelocity = 127;
    readonly int[,] noteOffsets = new int[25,16];
    static readonly int[] Roots = {60,53,48,72,55,64,33,29};
    readonly System.Random songRandom = new System.Random();
    int lastSongStyle = -1, lastSongKey = -1;
    readonly Queue<string> recentSongs = new Queue<string>();
    readonly HashSet<string> recentSongSet = new HashSet<string>();
    static readonly string[] SongStyles = { "Deep & spacious", "Driving Bacardi", "Broken groove", "Melodic bounce", "Percussion session" };
    float StepPitch(int id, int step) { return id < 8 ? Mathf.Pow(2, noteOffsets[id,step] / 12f) : 1; }
    readonly float[][] sounds = new float[25][];
    readonly AudioClip[] clips = new AudioClip[25];
    readonly AudioClip[] playbackClips = new AudioClip[25];
    readonly float[] flashes = new float[25];
    readonly AudioSource[] voices = new AudioSource[64];
    int voice, selected = 6, current = -1, nextStep;
    float bpm = 104, swing = .16f, volume = .65f;
    string masteringState="-- (Analyze / Set target)";
    bool playing, targetLufs, bypassMaster; double targetGainDb; string masteringResult="Optional stereo master target / bypass available";
    BacardiMasterLufsMeter lufsMeter;
    BacardiStereoMasterPanel stereoPanel;
    bool stereoMode;
    double analyzedLoopLufs=double.NaN;
    bool analysisRequested;
    bool showRoutingTelemetry;
    BacardiAiVocalGenerator aiVocals;
    double nextTime;
    Vector2 scroll;
    string status = "Tap a pad to play. Select a sound, then click steps to build your groove.";
    GUIStyle title, label, pad, lufsStyle;

    void Awake()
    {
        // Edit mode draws the interface without creating audio components or clips.
        if (!Application.IsPlaying(gameObject)) return;
        var listener = FindFirstObjectByType<AudioListener>();
        if (listener == null) listener = new GameObject("Master Audio Listener").AddComponent<AudioListener>();
        lufsMeter = listener.GetComponent<BacardiMasterLufsMeter>();
        if (lufsMeter == null) lufsMeter = listener.gameObject.AddComponent<BacardiMasterLufsMeter>();
        lufsMeter.BindUI(GetInstanceID());
        stereoPanel=gameObject.AddComponent<BacardiStereoMasterPanel>();
        for (int i = 0; i < voices.Length; i++) { voices[i] = gameObject.AddComponent<AudioSource>(); voices[i].playOnAwake = false; }
        for (int i = 0; i < 25; i++)
        {
            if (samples != null && i < samples.Length && samples[i] != null) clips[i] = samples[i];
            else { sounds[i] = Synthesize(i); clips[i] = AudioClip.Create(Names[i], sounds[i].Length, 1, Rate, false); clips[i].SetData(sounds[i], 0); }
            playbackClips[i]=CreatePlaybackClip(clips[i]);
        }
        aiVocals = new BacardiAiVocalGenerator(gameObject);
        Demo();
        SyncVocalSongLength();
    }

    static AudioClip CreatePlaybackClip(AudioClip original) {
        if(original.channels!=1) return original;
        // Analysis/export use x in BOTH L and R for mono. Unity's centered
        // mono AudioSource uses constant-power panning (x/sqrt(2) per side).
        // Publish the same dual-mono PCM to native playback, before mastering;
        // changing master gain would also affect genuine stereo samples.
        var mono=new float[original.samples];
        if(!original.GetData(mono,0)) throw new Exception("Use Decompress On Load for sample "+original.name);
        var stereo=new float[checked(mono.Length*2)];
        for(int n=0;n<mono.Length;n++) stereo[n*2]=stereo[n*2+1]=mono[n];
        var playback=AudioClip.Create(original.name+" (analysis stereo mapping)",original.samples,2,original.frequency,false);
        playback.SetData(stereo,0);
        return playback;
    }
    void ReleasePlaybackClips() {
        for(int i=0;i<playbackClips.Length;i++)
            if(playbackClips[i]!=null && playbackClips[i]!=clips[i]) Destroy(playbackClips[i]);
    }

    void OnEnable()
    {
        if (!Application.IsPlaying(gameObject))
        {
            Demo();
            status = "EDIT MODE PREVIEW - Press Unity's Play button to play sounds and edit your beat.";
        }
    }

    double Duration(int step) { return 60.0 / bpm / 4 * (step % 2 == 0 ? 1 + swing : 1 - swing); }
    void Update()
    {
        SyncVocalSongLength();
        if(stereoMode) return;
        if(arrangedPreviewRunning) {
            songStep=Math.Min(SongSteps-1,(int)(arrangedSource.time*bpm/60*4));
            if(!arrangedSource.isPlaying) { StopArrangedPreview(); playing=false; current=-1; }
            return;
        }
        if(Application.IsPlaying(gameObject) && analysisRequested) {
            analysisRequested=false;
            AnalyzeMaster();
        }
        if (Application.IsPlaying(gameObject) && aiVocals != null) aiVocals.Tick(AudioSettings.dspTime, bpm, playing ? nextTime : AudioSettings.dspTime, volume);
        if (!Application.IsPlaying(gameObject) || !playing) return;
        double now = AudioSettings.dspTime;
        if (nextTime < now - .5) nextTime = now + .03;
        while (nextTime < now + .1)
        {
            for (int i = 0; i < 25; i++) {
                int source = SongSourceStep(i, songStep);
                if (source >= 0) Trigger(i, nextTime, velocities[i, source], StepPitch(i,source));
            }
            nextTime += Duration(nextStep);
            current = nextStep;
            nextStep = (nextStep + 1) % 16;
            songStep = (songStep + 1) % SongSteps;
        }
    }
    void Trigger(int i, double time = 0, int velocity = DefaultVelocity, float pitch = 1)
    {
        if (!Application.IsPlaying(gameObject)) return;
        AudioSource source = voices[voice++ % voices.Length];
        source.Stop(); source.pitch = pitch; source.clip = playbackClips[i]; source.volume = volume * VelocityGain(velocity);
        if (time > 0) source.PlayScheduled(time); else source.Play();
        flashes[i] = Time.unscaledTime + .15f;
    }
    void PausePlayback() { if (aiVocals != null) aiVocals.Pause(AudioSettings.dspTime); StopBeatAudio(); }
    void Stop() { if (aiVocals != null) aiVocals.Stop(); StopBeatAudio(); }
    void StopBeatAudio() { StopArrangedPreview(); playing = false; current = -1; nextStep = 0; songStep = 0; foreach (var s in voices) if (s != null) s.Stop(); if(lufsMeter!=null) lufsMeter.CancelLoopCheck(); }
    void StartPlayback() {
        if(ArrangementApplied) { PreviewArrangedSong(); return; }
        if (lufsMeter != null) lufsMeter.ResetSession();
        playing = true; nextTime = AudioSettings.dspTime + .05;
        if (aiVocals != null) aiVocals.Play(nextTime, bpm);
        if(lufsMeter!=null && songPlan == null) lufsMeter.BeginLoopCheck(nextTime,60.0/bpm*4*16,analyzedLoopLufs);
    }
    static float VelocityGain(int velocity) { return Mathf.Clamp(velocity, 1, 127) / 127f; }
    void Clear() {
        songPlan = null; appliedSongPlan=null; arrangementApplied=false;
        Array.Clear(steps, 0, steps.Length);
        Array.Clear(noteOffsets, 0, noteOffsets.Length);
        for (int i = 0; i < 25; i++) for (int s = 0; s < 16; s++) velocities[i, s] = DefaultVelocity;
    }
    void Demo()
    {
        Clear();
        for (int s = 0; s < 16; s++) { steps[8,s] = s % 4 == 0; steps[10,s] = s % 2 == 0; steps[12,s] = s % 2 == 1; steps[9,s] = s == 4 || s == 12; }
        foreach (int s in new[] { 0, 3, 6, 7, 10, 11, 14, 15 }) steps[6,s] = true;
        int[] logNotes = { 0, 0, 0, 7, 0, 0, 0, 12, 0, 0, 7, 0, 0, 0, 0, 7 };
        for (int s = 0; s < 16; s++) {
            noteOffsets[6,s] = logNotes[s];
            velocities[6,s] = s % 2 == 0 ? 118 : s == 7 || s == 15 ? 76 : 98;
            steps[12,s] = true;
        }
        steps[11,6] = steps[11,14] = steps[18,3] = steps[18,11] = true;
        steps[0,0] = steps[1,8] = steps[15,7] = steps[16,15] = true;
        for (int s = 0; s < 16; s++) {
            velocities[10,s] = s % 4 == 0 ? 105 : 65;
            velocities[12,s] = s % 4 == 1 ? 80 : 48;
        }
        velocities[15,7] = 65; velocities[16,15] = 50;
        EnsureAllSoundsUsed();
    }

    // Fill unused parts with sparse accents; preserve every existing note and velocity.
    static readonly int[] AccentSteps = { 0, 8, 0, 6, 8, 11, 2, 0, 0, 4, 2, 14, 1, 3, 10, 7, 15, 5, 12, 9, 14, 0, 13, 12, 0 };
    void EnsureAllSoundsUsed()
    {
        for (int i = 0; i < 25; i++) {
            if (HasSteps(i)) continue;
            int s = AccentSteps[i];
            steps[i,s] = true;
            velocities[i,s] = i >= 21 ? 32 : i < 8 ? 48 : 55;
            noteOffsets[i,s] = 0;
        }
    }

    public void GenerateNewSong()
    {
        if (!Application.IsPlaying(gameObject)) return;
        Pattern p = null;
        int changed, layers, melodicChanges;
        bool empty = true;
        foreach (bool active in steps) if (active) { empty = false; break; }
        int style = 0, key = 0;
        // Reject similar candidates before committing anything to the current song.
        for (int attempt = 0; attempt < 128; attempt++) {
            style = lastSongStyle < 0 ? songRandom.Next(SongStyles.Length) : (lastSongStyle + songRandom.Next(1, SongStyles.Length)) % SongStyles.Length;
            key = lastSongKey < 0 ? songRandom.Next(12) : (lastSongKey + songRandom.Next(1, 12)) % 12;
            p = CreateSong(style, key); changed = layers = melodicChanges = 0;
            for (int i=0;i<25;i++) {
                bool rhythmChanged=false;
                for (int s=0;s<16;s++) {
                    int c=i*16+s;
                    if (steps[i,s]!=p.cells[c]) { changed++; rhythmChanged=true; }
                    if (i<8 && (steps[i,s] || p.cells[c]) && (steps[i,s]!=p.cells[c] || noteOffsets[i,s]!=p.notes[c])) melodicChanges++;
                }
                if(rhythmChanged) layers++;
            }
            if ((empty || changed >= 24) && layers >= 2 && melodicChanges >= 4 && !recentSongSet.Contains(SongSignature(p))) break;
            if (attempt == 127) {
                status = "Could not find a distinct idea. Press GENERATE NEW SONG to try again.";
                return;
            }
        }
        bool resume=playing; Stop();
        lastSongStyle = style; lastSongKey = key;
        string signature = SongSignature(p);
        recentSongs.Enqueue(signature); recentSongSet.Add(signature);
        if (recentSongs.Count > 256) recentSongSet.Remove(recentSongs.Dequeue());
        songPlan = null; appliedSongPlan=null; arrangementApplied=false;
        swing = p.shuffle;
        for(int i=0;i<25;i++) for(int s=0;s<16;s++) {
            int c=i*16+s; steps[i,s]=p.cells[c]; velocities[i,s]=p.velocities[c]; noteOffsets[i,s]=p.notes[c];
        }
        if(resume) StartPlayback();
        status=SongStyles[style] + " / " + bpm + " BPM / swing " + Mathf.RoundToInt(swing * 100) + "%. SAVE PATTERN to keep it.";
    }

    // Ignore tempo, key and velocity so transposing the same groove cannot bypass history.
    static string SongSignature(Pattern p)
    {
        var signature = new System.Text.StringBuilder(400);
        foreach (bool cell in p.cells) signature.Append(cell ? '1' : '0');
        return signature.ToString();
    }

    int[] VaryRhythm(int[] template, bool preserveDownbeat = false)
    {
        var rhythm = new List<int>(template);
        // Move individual hits, then add or remove a hit for different phrase lengths.
        for (int n = 0; n < rhythm.Count; n++) {
            if ((preserveDownbeat && rhythm[n] == 0) || songRandom.Next(3) == 0) continue;
            int moved = (rhythm[n] + (songRandom.Next(2) == 0 ? 1 : 15)) % 16;
            if (!rhythm.Contains(moved)) rhythm[n] = moved;
        }
        if (rhythm.Count > 2 && songRandom.Next(2) == 0) {
            int remove = songRandom.Next(rhythm.Count);
            if (!preserveDownbeat || rhythm[remove] != 0) rhythm.RemoveAt(remove);
        } else {
            int added = songRandom.Next(16);
            if (!rhythm.Contains(added)) rhythm.Add(added);
        }
        rhythm.Sort();
        return rhythm.ToArray();
    }

    Pattern CreateSong(int style, int key)
    {
        var p=new Pattern { cells=new bool[400], velocities=new int[400], notes=new int[400] };
        int[] tempos = { 100, 110, 104, 106, 102 };
        float[] swings = { .18f, .10f, .24f, .16f, .20f };
        p.tempo = tempos[style] + songRandom.Next(-4, 5);
        p.shuffle = Mathf.Clamp(swings[style] + (float)songRandom.NextDouble() * .08f - .04f, 0, .45f);
        for(int c=0;c<400;c++) p.velocities[c]=DefaultVelocity;
        int[] scale={0,2,4,7,9};
        int[][] rhythms={new[]{0,3,6,10,14},new[]{0,3,6,7,10,11,14,15},new[]{0,2,5,8,11,14,15},new[]{0,3,6,9,10,14},new[]{0,2,5,7,10,13,15}};
        int[][] motifs={new[]{0,10},new[]{0,3,6,10,13},new[]{1,4,7,11},new[]{0,2,5,8,10,14},new[]{3,9,15}};
        Action<int,int,int,int> hit=(id,s,v,note)=>{
            int c=id*16+s; p.cells[c]=true; p.velocities[c]=Mathf.Clamp(v+songRandom.Next(-9,10),1,127);
            p.notes[c]=id<8 ? Mathf.Clamp(note-Roots[id], -24, 24) : 0;
        };
        int[][] kicks = { new[]{0,10}, new[]{0,4,8,12}, new[]{0,6,11}, new[]{0,4,10,14}, new[]{0,7,12} };
        int shift = songRandom.Next(2) * 2;
        foreach(int s in VaryRhythm(kicks[style], true)) hit(8,s,style==1 ? 120 : 100,0);
        if(songRandom.Next(2)==0) hit(8,15,65,0);
        int backbeat = style==2 || style==4 ? 18 : 9;
        hit(backbeat,4,90,0); hit(backbeat,12,102,0);
        if(style==2) hit(backbeat,10,52,0);
        int hat = style==0 || style==4 ? 12 : 10;
        int spacing = style==0 ? 4 : style==1 ? 1 : 2;
        int phase=songRandom.Next(spacing);
        for(int s=phase;s<16;s+=spacing) {
            if(s % 4 != phase && songRandom.Next(5)==0) continue;
            hit(hat,s,s%4==phase ? 78 : 43,0);
        }
        if(style==1 || style==3) { hit(11,6+shift,58,0); hit(11,14,68,0); }
        int logDegree = songRandom.Next(5);
        foreach(int s in VaryRhythm(rhythms[style])) {
            int degree = (logDegree + (s >= 8 ? 3 : 0)) % 5;
            hit(6,(s+shift)%16,s%2==0 ? 110 : 74,33+key+scale[degree]);
        }
        if(style==0 || style==2 || songRandom.Next(3)==0) {
            hit(7,0,74,29+key); hit(7,8+shift,66,36+key);
        }
        int[][] leads = { new[]{1,4}, new[]{3,5}, new[]{0,5}, new[]{0,1,3}, new[]{3,1} };
        int lead=leads[style][songRandom.Next(leads[style].Length)], degreeStart=songRandom.Next(5);
        int[] motif=VaryRhythm(motifs[style]);
        int direction = songRandom.Next(2)==0 ? 1 : -1;
        int interval = songRandom.Next(1,4);
        for(int n=0;n<motif.Length;n++) {
            int degree = ((degreeStart + direction*n*interval) % 5 + 5) % 5;
            int octave = lead==3 ? 72 : lead==4 ? 55 : 60;
            hit(lead,(motif[n]+shift)%16,n%2==0 ? 80 : 58,octave+key+scale[degree]);
        }
        // Sparse profiles leave space; busier profiles answer the lead with harmony.
        if(style!=4) {
            int harmony = lead==1 ? 2 : style==1 ? 1 : new[]{1,2,4}[songRandom.Next(3)];
            if(harmony==lead) harmony=2;
            int chord=new[]{0,5,7}[songRandom.Next(3)];
            hit(harmony,shift,48,48+key+chord);
            if(style!=0) hit(harmony,8+shift,44,48+key+(chord==0 ? 7 : 0));
        }
        int[] percussion={13,14,15,16,17,18,19,20};
        for(int n=7;n>0;n--) { int j=songRandom.Next(n+1),t=percussion[n]; percussion[n]=percussion[j]; percussion[j]=t; }
        int count=style==0 ? 1 : style==4 ? 5 : songRandom.Next(2,4);
        for(int n=0;n<count;n++) {
            int offset=songRandom.Next(1,4);
            int spacingPerc = style==4 ? 3 : songRandom.Next(3,7);
            for(int s=offset;s<16;s+=spacingPerc) if(songRandom.Next(5)!=0) hit(percussion[n],s,s<8 ? 48 : 66,0);
        }
        // Every generated groove uses all 25 parts, with quiet supporting accents.
        for(int i=0;i<25;i++) {
            bool used=false;
            for(int s=0;s<16;s++) used |= p.cells[i*16+s];
            if(!used) hit(i,AccentSteps[i],i>=21 ? 32 : i<8 ? 48 : 55,Roots[Math.Min(i,7)]+key);
        }
        return p;
    }

    void OnGUI()
    {
        if(!stereoMode && Application.IsPlaying(gameObject) && Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.F8) {
            analysisRequested=true;
            Event.current.Use();
        }
        if (title == null) {
            title = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            pad = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold, wordWrap = true };
            lufsStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            lufsStyle.normal.textColor = new Color(.1f,.9f,.65f);
        }
        GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, new Color(.045f,.055f,.08f), 0, 0);
        GUILayout.BeginArea(new Rect(24,18,Screen.width-48,Screen.height-36));
        GUILayout.Label("BACARDI / AMAPIANO", title);
        GUILayout.Label("BEAT MAKER   •   25 SOUNDS   /   16 STEPS", label);
        bool previousEnabled = GUI.enabled;
        if(Application.IsPlaying(gameObject) && GUILayout.Button(stereoMode?"RETURN TO STEM MIX":"POST-ARRANGEMENT MASTER TEST",GUILayout.Height(38))) {
            stereoMode=!stereoMode;
            if(stereoMode) { PausePlayback(); foreach(var v in voices) if(v!=null) v.Stop(); }
            else stereoPanel.StopPlayback();
            lufsMeter.PostArrangementPlayback=stereoMode;
        }
        if(stereoMode) { stereoPanel.Draw(); GUILayout.EndArea(); return; }
        GUI.enabled = previousEnabled && Application.IsPlaying(gameObject);
        GUILayout.Space(12);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(playing ? "PAUSE" : "PLAY", GUILayout.Height(36), GUILayout.Width(100))) { if (playing) PausePlayback(); else StartPlayback(); }
        if (GUILayout.Button("STOP", GUILayout.Height(36), GUILayout.Width(80))) Stop();
        GUILayout.Label("TEMPO " + Mathf.RoundToInt(bpm), GUILayout.Width(90)); bpm = Mathf.Round(GUILayout.HorizontalSlider(bpm,80,150,GUILayout.Width(140)));
        GUILayout.Label("SWING " + Mathf.RoundToInt(swing*100) + "%", GUILayout.Width(90)); swing = GUILayout.HorizontalSlider(swing,0,.45f,GUILayout.Width(100));
        GUILayout.Label("MASTER", GUILayout.Width(60)); volume = GUILayout.HorizontalSlider(volume,0,1,GUILayout.Width(100));
        string loudness = lufsMeter != null ? lufsMeter.Readout : "LUFS-S: -- | LUFS-I: -- | True Peak: -- dBTP";
        bool meterFits = Screen.width >= 1430;
        if (meterFits) {
            GUILayout.Space(12);
            GUILayout.Label(loudness, lufsStyle, GUILayout.Width(590), GUILayout.Height(36));
        }
        GUILayout.EndHorizontal();
        // Keep the existing controls usable in a narrow Unity Game view.
        if (!meterFits) {
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(loudness, lufsStyle, GUILayout.Width(590), GUILayout.Height(24));
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(12);
        // Reserve this row even while the report is empty: asynchronous meter
        // updates must not move the buttons between mouse-down and mouse-up.
        GUILayout.Label(lufsMeter!=null ? lufsMeter.LoopReport : "",label,GUILayout.Height(42));
        Rect masteringControls=GUILayoutUtility.GetRect(580,36,GUILayout.ExpandWidth(true));
        // These are IMGUI controls: Active Input Handling must include the legacy
        // Input Manager (Both). Canvas raycasters/EventSystem do not route OnGUI input.
        bool enabledTarget=GUI.Toggle(new Rect(masteringControls.x,masteringControls.y,180,36),targetLufs,"TARGET \u221214 LUFS: " + (targetLufs ? "ON" : "OFF"),"Button");
        if(enabledTarget!=targetLufs) {
            Debug.Log("[Mastering UI] TARGET -14 LUFS clicked: " + (enabledTarget ? "ON" : "OFF"), this);
            if(enabledTarget) analysisRequested=true;
            else { targetLufs=false; if(lufsMeter!=null) { lufsMeter.SetTarget(false,targetGainDb); lufsMeter.ResetMeasurements(); } }
        }
        if(GUI.Button(new Rect(masteringControls.x+186,masteringControls.y,210,36),"Analyze / Set \u221214 LUFS")) {
            Debug.Log("[Mastering UI] Analyze / Set -14 LUFS clicked", this);
            analysisRequested=true;
        }
        bool nextBypass=GUI.Toggle(new Rect(masteringControls.x+402,masteringControls.y,180,36),bypassMaster,"BYPASS MASTERING: " + (bypassMaster ? "ON" : "OFF"),"Button");
        if(nextBypass!=bypassMaster) {
            Debug.Log("[Mastering UI] BYPASS MASTERING clicked: " + (nextBypass ? "ON" : "OFF"), this);
            bypassMaster=nextBypass;
            if(lufsMeter!=null) { lufsMeter.SetBypass(bypassMaster); lufsMeter.ResetMeasurements(); }
        }
        GUILayout.BeginHorizontal();
        GUILayout.Label("Limiter Gain Reduction / " + (lufsMeter!=null ? lufsMeter.GainReductionReadout : "GR: 0.0 dB"),lufsStyle,GUILayout.Width(330));
        GUILayout.Label("Status: " + masteringState,label);
        GUILayout.Label(!targetLufs ? "TARGET OFF / safety bypass" : bypassMaster ? "BYPASS ON / safety trim" : masteringState,label,GUILayout.Width(200));
        GUILayout.EndHorizontal();
        GUILayout.Label(masteringResult,label);
        if(lufsMeter!=null && Debug.isDebugBuild && GUILayout.Toggle(showRoutingTelemetry,"Runtime mastering telemetry")) {
            showRoutingTelemetry=true;
            GUILayout.Label(lufsMeter.RuntimeTelemetry,label);
        } else showRoutingTelemetry=false;
        // Bound the scroll viewport independently of the minimum widths of older
        // transport/export rows. The planner owns a fixed-height content block.
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true), GUILayout.Width(Mathf.Max(200, Screen.width-48)));
        GUILayout.BeginVertical(GUILayout.MinHeight(1800 + SongVariationHeight(Mathf.Max(180, Screen.width-80))));
        DrawSongVariation(Mathf.Max(180, Screen.width-80));
        for (int row=0; row<5; row++) {
            GUILayout.BeginHorizontal();
            for (int col=0; col<5; col++) {
                int i=row*5+col;
                GUI.backgroundColor = Time.unscaledTime < flashes[i] ? Color.white : i == selected ? new Color(.1f,.9f,.65f) : i < 6 ? new Color(.45f,.55f,.95f) : i < 9 ? new Color(.85f,.45f,.25f) : i < 21 ? new Color(.3f,.55f,.65f) : new Color(.7f,.4f,.7f);
                if (GUILayout.Button((i+1).ToString("00") + "\n" + Names[i],pad,GUILayout.Height(70))) { selected=i; Trigger(i); }
            }
            GUILayout.EndHorizontal();
        }
        GUI.backgroundColor = Color.white;
        GUILayout.Space(16);
        GUILayout.Label("SEQUENCER / " + Names[selected],label);
        GUILayout.BeginHorizontal();
        for (int s=0;s<16;s++) {
            GUILayout.BeginVertical();
            GUI.backgroundColor = s==current ? new Color(1,.75f,.25f) : steps[selected,s] ? Color.Lerp(new Color(.12f,.35f,.3f), new Color(.1f,.9f,.65f), VelocityGain(velocities[selected,s])) : new Color(.25f,.3f,.4f);
            if (GUILayout.Button((s+1).ToString("00"),GUILayout.Height(42))) steps[selected,s] = !steps[selected,s];
            GUI.backgroundColor = Color.white;
            GUILayout.Label(velocities[selected,s].ToString(), GUILayout.Height(20));
            velocities[selected,s] = Mathf.RoundToInt(GUILayout.VerticalSlider(velocities[selected,s],127,1,GUILayout.Height(65)));
            GUILayout.EndVertical();
        }
        GUI.backgroundColor=Color.white;
        GUILayout.EndHorizontal();
        GUILayout.Label("VELOCITY 1-127 / Drag each slider to soften a hit or add an accent. Step buttons turn hits on/off.", label);
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("CLEAR SOUND")) for (int s=0;s<16;s++) steps[selected,s]=false;
        if (GUILayout.Button("CLEAR ALL")) { Stop(); Clear(); }
        if (GUILayout.Button("DEMO GROOVE")) { Stop(); Demo(); }
        if (GUILayout.Button("GENERATE NEW SONG", GUILayout.Height(32))) GenerateNewSong();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("SAVE PATTERN")) Save();
        if (GUILayout.Button("LOAD PATTERN")) Load();
        if (GUILayout.Button("EXPORT WAV")) Export();
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("EXPORT MIX-BALANCED STEMS", GUILayout.Height(32))) Export(true);
        if (GUILayout.Button("EXPORT -14 LUFS STEMS", GUILayout.Height(32))) Export(true,true);
        if (GUILayout.Button("EXPORT MELODIC MIDI", GUILayout.Height(32))) ExportMidi();
        GUILayout.EndHorizontal();
        if (aiVocals != null) { SyncVocalSongLength(); aiVocals.Draw(bpm, loudness, Mathf.Max(200, Screen.width - 80)); }
        else GUILayout.Label("AI VOCAL GENERATOR / Press Play in Unity to enable local vocal generation.",label);
        GUI.enabled = previousEnabled;
        GUILayout.Space(8); GUILayout.Label(status,label);
        GUILayout.EndVertical(); GUILayout.EndScrollView();
        if(Event.current.type==EventType.Repaint) {
            Rect viewport=GUILayoutUtility.GetLastRect();
            VariationViewport=new Rect(GUIUtility.GUIToScreenPoint(viewport.position),viewport.size);
        }
        GUILayout.EndArea();
    }

    [Serializable] class Pattern { public float tempo, shuffle; public bool[] cells; public int[] velocities; public int[] notes; public BacardiSongVariation.Plan variation; }
    void Save() {
        var p = new Pattern { tempo=bpm, shuffle=swing, cells=new bool[400], velocities=new int[400], notes=new int[400] };
        for(int i=0;i<25;i++) for(int s=0;s<16;s++) { p.cells[i*16+s]=steps[i,s]; p.velocities[i*16+s]=velocities[i,s]; p.notes[i*16+s]=noteOffsets[i,s]; }
        p.variation = songPlan;
        PlayerPrefs.SetString("BacardiPattern",JsonUtility.ToJson(p)); PlayerPrefs.Save(); status="Pattern and song variation saved on this device.";
    }
    void Load() {
        try {
            var p=JsonUtility.FromJson<Pattern>(PlayerPrefs.GetString("BacardiPattern",""));
            if(p==null || p.cells==null || p.cells.Length!=400) { status="No saved pattern found."; return; }
            Stop(); bpm=Mathf.Clamp(p.tempo,80,150); swing=Mathf.Clamp(p.shuffle,0,.45f);
            appliedSongPlan=null; appliedSongPlan=null; arrangementApplied=false; songPlan = ValidSongPlan(p.variation) ? p.variation : null;
            if(songPlan != null) { variationSeed=songPlan.seed.ToString(); variationPersonality=songPlan.personality; variationAmount=songPlan.amount; variationDifference="LOW (saved variation restored)"; }
            for(int i=0;i<25;i++) for(int s=0;s<16;s++) {
                steps[i,s]=p.cells[i*16+s];
                noteOffsets[i,s]=i<8 && p.notes!=null && p.notes.Length==400 ? Mathf.Clamp(p.notes[i*16+s],-24,24) : 0;
                velocities[i,s]=p.velocities != null && p.velocities.Length == 400 ? Mathf.Clamp(p.velocities[i*16+s],1,127) : DefaultVelocity;
            }
            vocalSongLengthDetached=false; SyncVocalSongLength();
            status="Saved pattern loaded.";
        } catch(Exception e) { status="Could not load pattern: " + e.Message; }
    }
    string ExportFolder() {
        string path = Path.Combine(Application.persistentDataPath, "Exports", "Bacardi-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(path);
        return path;
    }

    bool HasSteps(int id) {
        for (int s = 0; s < SongSteps; s++) if (SongSourceStep(id,s) >= 0) return true;
        return false;
    }

    static bool HasAudiblePcm(float[] data, float gain = 1) {
        foreach (float value in data) {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new Exception("Audio contains invalid sample values.");
            if (Math.Abs(value * gain) >= .5f / 8388607) return true;
        }
        return false;
    }

    float[] RenderSound(int id, int frames) {
        float[] data = sounds[id];
        int channels = 1, sampleRate = Rate;
        if (data == null) {
            channels = clips[id].channels; sampleRate = clips[id].frequency;
            data = new float[clips[id].samples * channels];
            if (!clips[id].GetData(data, 0)) throw new Exception("Use Decompress On Load for sample " + Names[id]);
        }
        float[] result = new float[frames * 2];
        double time = 0;
        int sourceFrames = data.Length / channels;
        if (sourceFrames == 0 || channels > 2) throw new Exception("Use a non-empty mono or stereo sample for " + Names[id]);
        for (int eventStep = 0; eventStep < SongSteps; eventStep++) {
            int step = SongSourceStep(id,eventStep);
            if (step >= 0) {
                int start = (int)Math.Round(time * Rate);
                double pitch=StepPitch(id,step);
                int count = (int)Math.Ceiling(sourceFrames * (double)Rate / sampleRate / pitch);
                if(ArrangementApplied) {
                    int endBar=eventStep/16+1;
                    while(endBar<appliedSongPlan.bars && appliedSongPlan.masks[endBar*25+id]!=0) endBar++;
                    count=Math.Min(count,(int)Math.Ceiling(endBar*240.0/bpm*Rate)-start);
                }
                for (int n = 0; n < count && start + n < frames; n++) {
                    double position = n * (double)sampleRate / Rate * pitch;
                    int a = Math.Min(sourceFrames - 1, (int)position), b = Math.Min(sourceFrames - 1, a + 1);
                    float fraction = (float)(position - a);
                    for (int c = 0; c < 2; c++) {
                        int channel = channels == 1 ? 0 : c;
                        result[(start + n) * 2 + c] += Mathf.Lerp(data[a * channels + channel], data[b * channels + channel], fraction) * volume * VelocityGain(velocities[id,step]);
                    }
                }
            }
            time += Duration(eventStep % 16);
        }
        if(arrangementApplied) SilenceInactiveBars(id,result);
        return result;
    }

    static void WriteWav(string path, float[] data, float gain) {
        using (var w = new BinaryWriter(File.Create(path))) {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length * 3);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 6);
            w.Write((short)6); w.Write((short)24);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length * 3);
            foreach (float value in data) {
                int sample = (int)Math.Round(Mathf.Clamp(value * gain, -1, 1) * 8388607);
                w.Write((byte)sample); w.Write((byte)(sample >> 8)); w.Write((byte)(sample >> 16));
            }
        }

    }

    float[] ArrangementMix() {
        double end=SongEnd();
        var mix=new float[(arrangementApplied ? ArrangedFrames : (int)Math.Ceiling((end+.1)*Rate))*2];
        for(int i=0;i<25;i++) if(HasSteps(i)) {
            var track=RenderSound(i,mix.Length/2);
            for(int n=0;n<mix.Length;n++) mix[n]+=track[n];
        }
        return mix;
    }
    float[] PlaybackReference(float[] mix) {
        // A complete song is already the finite arrangement reference. Keep the
        // existing overlapping 16-repeat reference for the legacy one-bar groove.
        return appliedSongPlan == null ? BacardiMastering.LoopReference(mix,(int)Math.Round(60.0/bpm*4*Rate)) : mix;
    }
    void AnalyzeMaster() {
        bool resume=playing;
        Stop();
        if(lufsMeter!=null) lufsMeter.ResetSession();
        try {
            var mix=ArrangementMix();
            var result=BacardiMastering.Render(mix,Rate,PlaybackReference(mix));
            analyzedLoopLufs=result.ReferenceIntegrated;
            masteringState=Math.Abs(result.Integrated+14)<=.2 && Math.Abs(result.ReferenceIntegrated+14)<=.2 ? "TARGET OK" : "TP LIMITED / transient protection";
            targetGainDb=result.GainDb; targetLufs=true; bypassMaster=false;
            if(lufsMeter!=null) {
                lufsMeter.RecordAnalysis(result);
                lufsMeter.SetDrySafety(result.ReferenceInputTruePeak);
                lufsMeter.SetBypass(false); lufsMeter.SetTarget(true,targetGainDb);
                lufsMeter.ResetSession();
            }
            masteringResult=result.Summary + " | ANALYZED LOOP LUFS-I: " + result.ReferenceIntegrated.ToString("F1") +
                " LUFS-I | Max GR: " + Math.Max(result.MaxReductionDb,result.ReferenceMaxReductionDb).ToString("F1") + " dB";
            status="Fresh deterministic analysis / includes overlapping loop tails. Fixed gain; maximum limiter reduction 3 dB. Re-analyze after edits.";
        } catch(Exception e) { status="Master analysis failed: " + e.Message; }
        finally { if(resume) StartPlayback(); }
    }

    void Export(bool stems = false, bool normalizeStems = false) {
        try {
            if (volume <= 0) throw new Exception("Raise MASTER volume before exporting.");
            if (stems && songPlan == null) EnsureAllSoundsUsed();
            var active = new List<int>();
            for (int i = 0; i < 25; i++) if (HasSteps(i)) active.Add(i);
            if (active.Count == 0) throw new Exception("Add sequencer steps or choose DEMO GROOVE before exporting. Tapping a pad only previews its sound.");
            double end = SongEnd();
            int frames = (int)Math.Ceiling((end + .1) * Rate);
            float[] mix = new float[frames * 2];
            var tracks = new Dictionary<int, float[]>();
            float peak = 1, allStemPeak = 0;
            foreach (int i in active) {
                float[] track = RenderSound(i, frames);
                if (!HasAudiblePcm(track)) throw new Exception(Names[i] + " renders silent audio. Check its sample and sequencer velocity.");
                // Long arrangements render stems one at a time instead of retaining 25 full songs.
                tracks.Add(i, songPlan == null ? track : null);
                for (int n = 0; n < mix.Length; n++) { mix[n] += track[n]; allStemPeak = Mathf.Max(allStemPeak, Mathf.Abs(track[n])); peak = Mathf.Max(peak, Mathf.Abs(track[n])); }
            }
            Func<int,float[]> exportTrack = id => tracks[id] ?? RenderSound(id,frames);
            foreach (float value in mix) peak = Mathf.Max(peak, Mathf.Abs(value));
            float gain = 1 / peak;
            // A single trim only when necessary for 24-bit PCM headroom.
            // No gain change for normal levels; relative balance stays exact.
            float stemGain=1;
            if(stems && !normalizeStems) {
                float stemPeak=allStemPeak;
                if(stemPeak>1) stemGain=1/stemPeak;
            }
            if (!HasAudiblePcm(mix, gain)) throw new Exception("The mix is silent. Check sample levels and phase cancellation.");
            foreach (int i in active) if (!HasAudiblePcm(exportTrack(i), gain)) throw new Exception(Names[i] + " is too quiet for WAV export. Raise its level.");
            string folder = ExportFolder();
            string masterPath=Path.Combine(folder,"Full Mix.wav");
            if(targetLufs && !bypassMaster) {
                var mastered=BacardiMastering.Render(mix,Rate,PlaybackReference(mix));
                WriteWav(masterPath,mastered.Audio,1);
                string verified=BacardiLoudness.Verify(masterPath);
                masteringResult=verified + " | Gain: " + mastered.GainDb.ToString("F1") + " dB | Max GR: " + mastered.MaxReductionDb.ToString("F1") + " dB";
                File.WriteAllText(Path.Combine(folder,"MASTERING.txt"),masteringResult + "\r\nInput: " + mastered.InputLufs.ToString("F2") + " LUFS-I. Full Mix reference; stem mode is documented in IMPORT.txt.\r\nLoop reference: " + mastered.ReferenceIntegrated.ToString("F2") + " LUFS-I. Loop maximum reduction: " + mastered.ReferenceMaxReductionDb.ToString("F2") + " dB.");
            } else if(targetLufs && bypassMaster) {
                var peakMeter=new BacardiTruePeak(Rate,2); peakMeter.Observe(mix,2);
                float safetyGain=(float)Math.Min(1,Math.Pow(10,-1.3/20)/Math.Max(1e-20,peakMeter.Peak));
                WriteWav(masterPath,mix,safetyGain);
                masteringResult=BacardiLoudness.Verify(masterPath) + " | BYPASS / safety trim " + (20*Math.Log10(safetyGain)).ToString("F1") + " dB";
                File.WriteAllText(Path.Combine(folder,"MASTERING.txt"),masteringResult);
            } else WriteWav(masterPath,mix,gain);
            if (stems && normalizeStems) {
                var stemReport=new System.Text.StringBuilder("Individual stems: target -14 LUFS-I; true-peak ceiling -1.3 dBTP; maximum limiter reduction 3 dB.\r\nMeasured over each complete file including tails. Peak/transient protection can prevent reaching target.\r\nIndependent gains change the original mix balance; summing stems requires headroom and new master processing.\r\n\r\n");
                foreach (int i in active) {
                    string name=(i+1).ToString("00") + " " + Names[i] + " - LUFS Reference.wav";
                    string path=Path.Combine(folder,name);
                    // One fresh render per stem, measured over the exported finite
                    // program. Do not use the full mix's gain or loop reference.
                    var stem=BacardiMastering.Render(exportTrack(i),Rate);
                    WriteWav(path,stem.Audio,1);
                    string verified=BacardiLoudness.Verify(path);
                    stemReport.AppendLine(name + " | " + verified +
                        string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            " | Gain: {0:F2} dB | Max GR: {1:F2} dB",stem.GainDb,stem.MaxReductionDb));
                }
                File.WriteAllText(Path.Combine(folder,"STEM_LOUDNESS.txt"),stemReport.ToString());
                // Unused instruments remain silent; export never adds notes.
                for(int i=0;i<25;i++) if(!tracks.ContainsKey(i))
                    WriteWav(Path.Combine(folder,(i+1).ToString("00")+" "+Names[i]+" - LUFS Reference.wav"),new float[mix.Length],1);
            } else if(stems) {
                for(int i=0;i<25;i++)
                    WriteWav(Path.Combine(folder,(i+1).ToString("00")+" "+Names[i]+".wav"),
                        tracks.ContainsKey(i) ? exportTrack(i) : new float[mix.Length],stemGain);
                File.WriteAllText(Path.Combine(folder,"MIX_BALANCED.txt"),
                    "Mix-balanced arrangement stems: no individual normalization or limiter. " + (songPlan == null ? "Unused pads receive quiet accents. " : "Unused stems remain silent. ") + "Existing notes and velocities are preserved.\r\n"+
                    "Import all 25 files at the same start with unity gain; do not also play Full Mix.wav.\r\n"+
                    "The sum reconstructs the unmastered mix multiplied by the common PCM headroom gain: "+
                    stemGain.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+".\r\n"+
                    "Summing may exceed 0 dBFS; use DAW floating-point headroom and master the combined mix.\r\n");
            }
            WriteMidi(Path.Combine(folder, "Melodic.mid"));
            if(songPlan != null) {
                File.WriteAllText(Path.Combine(folder,"SONG_VARIATION.json"),JsonUtility.ToJson(songPlan,true));
                File.WriteAllText(Path.Combine(folder,"SONG_VARIATION.txt"),"Seed: "+songPlan.seed+" / "+BacardiSongVariation.Personalities[songPlan.personality]+" / "+BacardiSongVariation.Amounts[songPlan.amount]+"\r\n"+songPlan.Preview(Names));
            }
            File.WriteAllText(Path.Combine(folder, "IMPORT.txt"),
                "Bacardi / Amapiano export\r\nBPM: " + bpm.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                "\r\nSwing: " + (swing * 100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                "%\r\n" + SongBars + " bars in 4/4. WAV: stereo 44100 Hz, 24-bit PCM.\r\n" +
                "Set your DAW tempo to the BPM above. Place all stems at the same song start, on separate tracks.\r\n" +
                "All WAVs have equal length, with full sample tails. Loop the first " + SongBars + " bars, not the tail.\r\n" +
                (stems ? (songPlan == null ? "Unused pads receive quiet sequencer accents.\r\n" : "Inactive stems are silent; the section plan is in SONG_VARIATION.txt.\r\n") : "The mix uses the current sequencer notes.\r\n") +
                (stems ? (normalizeStems ? "LOUDNESS-NORMALIZED / REFERENCE STEMS: independently target -14 LUFS-I with peak/transient protection. See STEM_LOUDNESS.txt. Individual gains change mix balance.\r\n" : "MIX-BALANCED STEMS: use for FL Studio arrangement and mixing. No individual normalization or limiter. See MIX_BALANCED.txt for common headroom gain.\r\n") : "") +
                "Full Mix.wav is the separate stereo reference. Do not play it together with the stems. See MASTERING.txt when enabled.\r\n" +
                "Melodic.mid contains eight named tracks. Assign instruments in FL Studio; MIDI contains notes, not audio.\r\n" +
                "Notes use the current per-step pitches, per-step velocity (1-127) and a swung sixteenth-note gate. Pad/Strings include their synthesized triads.\r\n" +
                "Custom samples may have a different root pitch; adjust MIDI notes as needed. Swing is already baked into audio and MIDI.\r\n");
            status = (stems ? "25 " + (normalizeStems ? "LUFS reference" : "mix-balanced") + " stems + mix + MIDI exported: " : "Stereo mix + MIDI exported: ") + folder;
            Debug.Log(status);
        } catch(Exception e) { status="Export failed: " + e.Message; }
    }

    void ExportMidi() {
        try {
            string folder = ExportFolder();
            WriteMidi(Path.Combine(folder, "Melodic.mid"));
            status = "Melodic MIDI exported (assign instruments in FL Studio): " + folder;
            Debug.Log(status);
        } catch (Exception e) { status = "MIDI export failed: " + e.Message; }
    }

    static void BigEndian(BinaryWriter w, int value, int bytes) {
        for (int i = bytes - 1; i >= 0; i--) w.Write((byte)(value >> (i * 8)));
    }
    static void VariableLength(BinaryWriter w, int value) {
        int buffer = value & 127;
        while ((value >>= 7) > 0) buffer = (buffer << 8) | ((value & 127) | 128);
        while (true) { w.Write((byte)buffer); if ((buffer & 128) == 0) break; buffer >>= 8; }
    }
    static void MidiTrack(BinaryWriter file, Action<BinaryWriter> write) {
        using (var buffer = new MemoryStream()) {
            using (var track = new BinaryWriter(buffer, System.Text.Encoding.UTF8, true)) write(track);
            file.Write(System.Text.Encoding.ASCII.GetBytes("MTrk")); BigEndian(file, (int)buffer.Length, 4); file.Write(buffer.ToArray());
        }
    }
    void WriteMidi(string path) {
        const int ppq = 960, barTicks = ppq * 4;
        int[] roots = Roots;
        using (var file = new BinaryWriter(File.Create(path))) {
            file.Write(System.Text.Encoding.ASCII.GetBytes("MThd")); BigEndian(file, 6, 4);
            BigEndian(file, 1, 2); BigEndian(file, arrangementApplied ? 26 : 9, 2); BigEndian(file, ppq, 2);
            MidiTrack(file, w => {
                w.Write(new byte[] { 0, 255, 81, 3 }); BigEndian(w, (int)Math.Round(60000000.0 / bpm), 3);
                w.Write(new byte[] { 0, 255, 88, 4, 4, 2, 24, 8 });
                VariableLength(w, barTicks * SongBars); w.Write(new byte[] { 255, 47, 0 });
            });
            for (int i = 0; i < (arrangementApplied ? 25 : 8); i++) {
                int id = i;
                MidiTrack(file, w => {
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(Names[id]);
                    w.Write(new byte[] { 0, 255, 3 }); VariableLength(w, name.Length); w.Write(name);
                    int last = 0;
                    for (int eventStep = 0; eventStep < SongSteps; eventStep++) {
                        int s = SongSourceStep(id,eventStep);
                        if(s < 0) continue;
                        int tick = (int)Math.Round((eventStep + (eventStep % 2 == 1 ? swing : 0)) * ppq / 4.0);
                        int end = (int)Math.Round((eventStep + 1 + (eventStep % 2 == 0 ? swing : 0)) * ppq / 4.0);
                        int root=id<8 ? roots[id]+noteOffsets[id,s] : 36+id-8;
                        int channel=id<8 ? id : 9;
                        int[] notes = id == 2 || id == 4 ? new[] { root, root + 4, root + 7 } : new[] { root };
                        foreach (int note in notes) {
                            VariableLength(w, tick - last); w.Write((byte)(144 + channel)); w.Write((byte)note); w.Write((byte)Mathf.Clamp(velocities[id,s],1,127)); last = tick;
                        }
                        foreach (int note in notes) {
                            VariableLength(w, end - last); w.Write((byte)(128 + channel)); w.Write((byte)note); w.Write((byte)0); last = end;
                        }
                    }
                    VariableLength(w, barTicks * SongBars - last); w.Write(new byte[] { 255, 47, 0 });
                });
            }
        }
    }

    static float[] Synthesize(int id) {
        if (id == 6 || id == 7 || id == 8 || id == 9 || id == 10 || id == 11 || id == 12)
            return SynthesizeRhythm(id);
        float duration=id==2 || id==4 || id>=21 ? 2.4f : id<8 ? 1.3f : id==11 ? .5f : .32f;
        float[] data=new float[(int)(Rate*duration)]; var random=new System.Random(1200+id); float filtered=0;
        float[] pitches={261.63f,174.61f,130.81f,523.25f,196,330,55,43.65f,48,180,8000,6500,5500,380,620,220,330,7000,900,160,85,4000,4000,120,55};
        double phase=0;
        for(int n=0;n<data.Length;n++) {
            float t=(float)n/Rate, noise=(float)(random.NextDouble()*2-1), f=pitches[id];
            if(id==8 || id==20 || id==24) f+=140*Mathf.Exp(-t*35);
            if(id==23) f=120+2200*t/duration;
            phase+=2*Math.PI*f/Rate; float sine=(float)Math.Sin(phase), v;
            filtered=filtered*.85f+noise*.15f;
            if(id<5) { v=sine*.6f+(float)Math.Sin(phase*2)*.2f+(float)Math.Sin(phase*3)*.1f; if(id==2 || id==4) v=(sine+(float)Math.Sin(phase*1.2599)+(float)Math.Sin(phase*1.4983))*.25f; }
            else if(id==5) v=(float)Math.Sin(phase+3*Math.Sin(phase*.5))* (.6f+.4f*Mathf.Sin(t*35));
            else if(id==6) v=(float)Math.Sin(phase+5*Math.Exp(-t*12)*Math.Sin(phase*2));
            else if(id==7 || id==8 || id==20) v=sine;
            else if(id==9) v=noise*(.35f+.65f*Mathf.Abs(Mathf.Sin(t*180)));
            else if(id==10 || id==11 || id==12 || id==17 || id==21 || id==22) v=noise-filtered;
            else if(id==23) v=sine*.4f+noise*.4f;
            else if(id==24) v=sine*.7f+noise*.3f;
            else v=sine*.7f+(float)Math.Sin(phase*1.73)*.2f+noise*.1f;
            float decay=id==2 || id==4 ? 1.7f : id==0 || id==1 ? 4 : id==6 ? 7 : id==7 ? 5 : id==10 || id==18 ? 38 : id>=21 ? 2 : 15;
            float env=Mathf.Min(1,t*500)*Mathf.Exp(-t*decay)*Mathf.Min(1,(duration-t)*100);
            if(id==22 || id==23) env=Mathf.Pow(t/duration,1.5f)*Mathf.Min(1,(duration-t)*100);
            data[n]=v*env*.55f;
        }
        return data;
    }

    // Tuned low end, short transients and contrasting noise envelopes leave room
    // for the syncopated logdrum. These samples also feed WAV and stem exports.
    static float[] SynthesizeRhythm(int id)
    {
        double duration = id == 6 ? .65 : id == 7 ? .7 : id == 11 ? .28 : id == 9 ? .22 : id == 8 ? .24 : .10;
        var data = new float[(int)(Rate * duration)];
        var random = new System.Random(2400 + id);
        double phase = 0, lowNoise = 0, previousNoise = 0;
        for (int n = 0; n < data.Length; n++) {
            double t = (double)n / Rate;
            double noise = random.NextDouble() * 2 - 1;
            lowNoise += .22 * (noise - lowNoise);
            double bright = (noise - previousNoise) * .5;
            previousNoise = noise;
            double value;
            if (id == 6) {
                // Settle rapidly onto A1 so per-step pitch and MIDI agree.
                phase += 2 * Math.PI * (55 + 105 * Math.Exp(-t * 85)) / Rate;
                double body = Math.Sin(phase + 3.8 * Math.Exp(-t * 18) * Math.Sin(phase * 2));
                value = Math.Tanh(body * 1.65) * Math.Exp(-t * 8) * .72
                    + bright * Math.Exp(-t * 180) * .12;
            } else if (id == 7) {
                phase += 2 * Math.PI * 43.65353 / Rate;
                value = (Math.Sin(phase) + .12 * Math.Sin(phase * 2)) * Math.Exp(-t * 5) * .48;
            } else if (id == 8) {
                phase += 2 * Math.PI * (48 + 145 * Math.Exp(-t * 65)) / Rate;
                value = Math.Tanh(Math.Sin(phase) * 1.4) * Math.Exp(-t * 19) * .67
                    + bright * Math.Exp(-t * 220) * .15;
            } else if (id == 9) {
                double bursts = 0;
                for (int b = 0; b < 3; b++) {
                    double age = t - b * .011;
                    if (age >= 0) bursts += Math.Exp(-age * 150);
                }
                value = (noise - lowNoise) * (bursts * .25 + Math.Exp(-t * 24) * .3);
            } else {
                double decay = id == 11 ? 18 : id == 12 ? 62 : 85;
                double metal = (Math.Sin(2 * Math.PI * 7310 * t) + Math.Sin(2 * Math.PI * 4963 * t)) * .07;
                value = (bright * .65 + metal) * Math.Exp(-t * decay) * (id == 12 ? .30 : .38);
            }
            double edge = Math.Min(1, t * 1200) * Math.Min(1, (duration - t) * 200);
            data[n] = (float)(value * edge);
        }
        return data;
    }
    void OnDestroy() { StopArrangedPreview(); if(aiVocals!=null) aiVocals.Dispose(); ReleasePlaybackClips(); foreach(var c in clips) if(c!=null && (samples==null || Array.IndexOf(samples,c)<0)) Destroy(c); }
}












