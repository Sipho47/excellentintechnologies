using System;
using System.IO;
using System.Reflection;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Real AudioSources and live listener meter, not simulated PCM injection.
[InitializeOnLoad]
public static class BacardiVocalRuntimeTest {
    const string Key = "Bacardi.VocalRuntimeTest";
    static int stage;
    static double deadline, stageTime, epoch;
    static BacardiBeatMaker maker;
    static BacardiAiVocalGenerator vocals;
    static BacardiMasterLufsMeter meter;
    static string report = "";
    static bool firstAudible, secondAudible;
    static BacardiVocalRuntimeTest() { EditorApplication.update += Tick; }
    [MenuItem("Bacardi/Validate AI Vocal Timeline and Exports")]
    public static void Run() {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode first.");
        if(UnityEngine.Object.FindFirstObjectByType<BacardiBeatMaker>()==null) throw new Exception("Open the Bacardi scene first.");
        SessionState.SetBool(Key,true); SessionState.SetBool(Key+"cli",false);
        stage=0; deadline=EditorApplication.timeSinceStartup+160;
        EditorApplication.isPlaying=true;
    }
    public static void RunCommandLine() {
        EditorSceneManager.OpenScene("Assets/Scenes/BacardiBeatMaker.unity"); Run(); SessionState.SetBool(Key+"cli",true);
    }
    static void Invoke(string method) {
        typeof(BacardiBeatMaker).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(maker,null);
    }
    static object Field(string name) { return typeof(BacardiBeatMaker).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(maker); }
    static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
    sealed class FailedRealFixture : IAiVocalProvider {
        public string Name { get { return "REAL failure fixture (no network)"; } }
        public Exception Error;
        public bool ThrowSynchronously;
        public System.Threading.Tasks.Task<AiVocalResult> GenerateAsync(AiVocalRequest request, IProgress<AiVocalProgress> progress, System.Threading.CancellationToken token) {
            if (ThrowSynchronously) throw Error;
            return System.Threading.Tasks.Task.FromException<AiVocalResult>(Error);
        }
    }
    static void ValidateErrors() {
        int errors = 0; string diagnostic = "";
        Application.LogCallback listener = (message, trace, type) => { if(type == LogType.Error && message.Contains("vocal failure")) { errors++; diagnostic = message; } };
        Application.logMessageReceived += listener;
        try {
            foreach(string phase in new[] { "generation", "download", "vocal separation", "decoding", "file loading" }) {
                foreach(int status in new[] { 400, 401, 402, 429, 500 }) {
                    vocals.Provider = new FailedRealFixture { Error = new AiVocalFailure(phase, status, "Backend detail: original reason; Authorization: Bearer secret-value") };
                    int before = errors;
                    vocals.Generate(new AiVocalRequest { Lyrics = BacardiAiVocalGenerator.Hook });
                    vocals.Tick(AudioSettings.dspTime, 104, AudioSettings.dspTime, 1);
                    Assert(errors == before + 1 && diagnostic.Contains("HTTP=" + status) && diagnostic.Contains(phase) && diagnostic.Contains("original reason"), "Failure logged with stage/status/body");
                    Assert(!diagnostic.Contains("secret-value") && vocals.StatusText.Contains("ERROR " + status) && vocals.StatusText.Contains(phase), "Safe readable UI error");
                    Assert(vocals.Result == null && vocals.Provider is FailedRealFixture && vocals.Lyrics == BacardiAiVocalGenerator.Hook, "No fallback or lyric mutation");
                }
            }
            report += "PASS 25 staged HTTP failures: Unity LogError, status/body, UI, credential redaction, no fallback, unchanged Unicode lyrics.\n";
            foreach (bool synchronous in new[] { false, true }) {
                foreach (Exception error in new Exception[] {
                    new AiVocalFailure("generation", 0, "Vocal proxy unreachable", new System.Net.Http.HttpRequestException("Connection refused")),
                    new AiVocalFailure("download", 0, "Request timed out (HTTP client timeout: 12 minutes).", new System.Threading.Tasks.TaskCanceledException("Timeout detail")),
                    new InvalidOperationException("Unexpected file detail")
                }) {
                    vocals.Provider = new FailedRealFixture { Error = error, ThrowSynchronously = synchronous };
                    int before = errors;
                    vocals.Generate(new AiVocalRequest { Lyrics = BacardiAiVocalGenerator.Hook });
                    vocals.Tick(AudioSettings.dspTime, 120, AudioSettings.dspTime, 1);
                    Assert(errors == before + 1 && diagnostic.Contains(error.Message), "Network/timeout/exception logged exactly once");
                    Assert(vocals.StatusText.Contains(error.Message) && vocals.State == "ERROR", "Error survives tempo change in UI");
                    Assert(vocals.Provider is FailedRealFixture && !vocals.Busy, "Failure ends without fallback");
                }
            }
            report += "PASS six synchronous/asynchronous network, timeout and unexpected failures: Console details and persistent UI error.\n";
        } finally { Application.logMessageReceived -= listener; }
    }
    static void ValidateUnicodePaste() {
        string savedClipboard=GUIUtility.systemCopyBuffer;
        string original=vocals.Lyrics;
        try {
            string[] samples={
                "Sondela kimi, ungangidedeli\nHamba nami kancane\nSizodansa ubusuku bonke\nLapho siya khona, sazi thina",
                "  Coração — l’été!\r\nCanción, não me deixes…\n\n  ",
                "君と踊ろう\n함께 춤춰요\n一起跳舞\nارقص معي 🎵 e\u0301"
            };
            foreach(string sample in samples) {
                GUIUtility.systemCopyBuffer=sample;
                var editor=new TextEditor { text="old hook",isMultiline=true,cursorIndex=0,selectIndex=8 };
                var validate=new Event { type=EventType.ValidateCommand,commandName="Paste" };
                Assert(BacardiLyricsInput.HandlePaste(validate,editor) && editor.text=="old hook","Paste validation must not insert");
                var paste=new Event { type=EventType.KeyDown,keyCode=KeyCode.V,modifiers=EventModifiers.Control };
                Assert(BacardiLyricsInput.HandlePaste(paste,editor) && editor.text==sample,"Ctrl+V replaces selection with exact Unicode");
                BacardiLyricsInput.HandlePaste(new Event { type=EventType.KeyDown,keyCode=KeyCode.A,modifiers=EventModifiers.Control },editor);
                BacardiLyricsInput.HandlePaste(new Event { type=EventType.ExecuteCommand,commandName="Copy" },editor);
                Assert(GUIUtility.systemCopyBuffer==sample,"Unicode selection and copy");
                BacardiLyricsInput.HandlePaste(new Event { type=EventType.KeyDown,keyCode=KeyCode.X,modifiers=EventModifiers.Control },editor);
                Assert(editor.text=="" && GUIUtility.systemCopyBuffer==sample,"Unicode cut");
                Assert(BacardiLyricsInput.HandlePaste(new Event { type=EventType.ExecuteCommand,commandName="Paste" },editor) && editor.text==sample,"Paste command round trip");
                typeof(BacardiAiVocalGenerator).GetField("lyrics",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vocals,editor.text);
                vocals.PronunciationGuidance=BacardiAiVocalGenerator.IsiZuluPronunciation;
                vocals.CopyOriginalLyricsToPronunciationText();
                Assert(vocals.AiPronunciationText==sample && vocals.Lyrics==sample,"Copy preserves exact original Unicode lyrics");
                vocals.AiPronunciationText="Son-de-la manually edited";
                vocals.StrictLyrics=true;
                vocals.SlowerPhraseDensity=true;
                var request=vocals.CurrentRequest(104);
                Assert(request.PronunciationGuidance==BacardiAiVocalGenerator.IsiZuluPronunciation,"Preset captured separately in generation request");
                Assert(request.Lyrics==sample,"Generate/Regenerate use current edited lyrics");
                Assert(request.AiPronunciationText==vocals.AiPronunciationText && vocals.Lyrics==sample,"Manual pronunciation editing preserves canonical UI/request lyrics");
                Assert(request.StrictLyrics && request.SlowerPhraseDensity && request.Bpm==104,"Word preservation options captured without changing BPM");
                string json=JsonUtility.ToJson(request);
                string decoded=System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(json));
                Assert(JsonUtility.FromJson<AiVocalRequest>(decoded).Lyrics==sample,"Unity JSON UTF-8 round trip");
                Assert(JsonUtility.FromJson<AiVocalRequest>(decoded).PronunciationGuidance==request.PronunciationGuidance,"Unity serializes separate pronunciation guidance");
                Assert(JsonUtility.FromJson<AiVocalRequest>(decoded).AiPronunciationText==request.AiPronunciationText,"Unity serializes independent pronunciation text");
                Assert(JsonUtility.FromJson<AiVocalRequest>(decoded).StrictLyrics && JsonUtility.FromJson<AiVocalRequest>(decoded).SlowerPhraseDensity,"Unity serializes separate word preservation options");
            }
            vocals.GenerateNewLyrics();
            Assert(vocals.Lyrics.Split('\n').Length==4,"Offline four-line generation retained");
            report+="PASS native clipboard Ctrl+V/command paste, selection/copy/cut, exact current lyrics and Unity JSON UTF-8: isiZulu, accented Latin and non-Latin/mixed scripts.\n";
        } finally {
            GUIUtility.systemCopyBuffer=savedClipboard;
            vocals.AiPronunciationText="";
            vocals.StrictLyrics=false;
            vocals.SlowerPhraseDensity=false;
            typeof(BacardiAiVocalGenerator).GetField("lyrics",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(vocals,original);
        }
    }
    static void Finish(bool success,string text) {
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Unity-Vocal-Runtime.txt",(success?"PASS\n":"FAIL\n")+report+text);
        Debug.Log(text); SessionState.SetBool(Key,false);
        if(SessionState.GetBool(Key+"cli",false)) EditorApplication.Exit(success?0:1);
        else EditorApplication.isPlaying=false;
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)) return;
        if(deadline==0) deadline=EditorApplication.timeSinceStartup+160;
        if(EditorApplication.timeSinceStartup>deadline) { Finish(false,"Timed out"); return; }
        if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try {
            double now=AudioSettings.dspTime;
            if(stage==0) {
                maker=UnityEngine.Object.FindFirstObjectByType<BacardiBeatMaker>();
                if(maker==null) return;
                vocals=Field("aiVocals") as BacardiAiVocalGenerator;
                meter=UnityEngine.Object.FindFirstObjectByType<BacardiMasterLufsMeter>();
                if(vocals==null || meter==null) return;
                ValidateUnicodePaste(); ValidateErrors();
                Assert(BacardiBeatMaker.Names.Length==25,"25 sounds");
                Invoke("AnalyzeMaster"); Invoke("Stop");
                                vocals.Provider=new ElevenLabsAiVocalProvider("http://127.0.0.1:1");
                vocals.Generate(new AiVocalRequest { Lyrics=BacardiAiVocalGenerator.Hook }); stage=-1;
            } else if(stage==-1 && !vocals.Busy) {
                Assert(vocals.State=="ERROR" && vocals.Result==null && vocals.Provider is ElevenLabsAiVocalProvider,"Unavailable REAL fails without mock fallback");
                report+="PASS native REAL provider unavailable error; no implicit fallback.\n";
                vocals.Provider=new MockAiVocalProvider();
                vocals.Generate(new AiVocalRequest { Lyrics=BacardiAiVocalGenerator.Hook }); stage=1;
            } else if(stage==1 && !vocals.Busy) {
                Assert(vocals.Result!=null,"Mock generation: "+vocals.Message);
                vocals.Preview(104); stageTime=now; stage=2;
            } else if(stage==2 && now-stageTime>.25) {
                var source=typeof(BacardiAiVocalGenerator).GetField("preview",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(vocals) as AudioSource;
                Assert(source!=null && source.isPlaying && source.timeSamples>0,"Native vocal preview");
                vocals.StopPreview(); Assert(!source.isPlaying,"Stop preview");
                report+="PASS real Unity mock preview / stop preview.\n";
                vocals.Save(false,104,13,32); stage=3;
            } else if(stage==3 && !vocals.Busy) {
                Assert(vocals.State!="ERROR","Save WAV: "+vocals.Message);
                report+="Saved isolated WAV: "+vocals.Message+"\n";
                vocals.AddToSong(13); vocals.AddToSong(17);
                vocals.Save(true,104,13,32); stage=4;
            } else if(stage==4 && !vocals.Busy) {
                Assert(vocals.State!="ERROR","Save stem: "+vocals.Message);
                report+="Saved aligned stem: "+vocals.Message+"\n";
                Invoke("StartPlayback"); stageTime=now; stage=5;
            } else if(stage==5 && now-stageTime>.6) {
                Invoke("PausePlayback"); stageTime=vocals.Timeline.BeatsAt(now); epoch=now; stage=6;
            } else if(stage==6 && now-epoch>.2) {
                Assert(Math.Abs(vocals.Timeline.BeatsAt(now)-stageTime)<.001,"Pause preserves position");
                Invoke("StartPlayback"); stageTime=now; stage=7;
            } else if(stage==7 && now-stageTime>.3) {
                Assert(vocals.Timeline.BeatsAt(now)>0,"Resume advances");
                Invoke("Stop"); Assert(vocals.Timeline.BarAt(now)==1 && vocals.Timeline.BeatsAt(now)==0,"STOP Bar 1");
                report+="PASS integrated PLAY / PAUSE / resume / STOP transport.\n";
                Invoke("StartPlayback"); epoch=(double)Field("nextTime"); stage=8;
            } else if(stage==8) {
                double beats=vocals.Timeline.BeatsAt(now);
                if(beats>48.2 && beats<49.8) firstAudible |= vocals.Placements[0].Source.isPlaying && vocals.Placements[0].Source.timeSamples>0;
                if(beats>64.2 && beats<65.8) secondAudible |= vocals.Placements[1].Source.isPlaying && vocals.Placements[1].Source.timeSamples>0;
                if(beats<66) return;
                Assert(firstAudible && secondAudible,"Both bar 13 and 17 sources actually playing");
                Assert(Math.Abs(vocals.Placements[0].ScheduledDsp-(epoch+48*60/104.0))<1.0/44100,"Bar 13 DSP accuracy");
                Assert(Math.Abs(vocals.Placements[1].ScheduledDsp-(epoch+64*60/104.0))<1.0/44100,"Bar 17 DSP accuracy");
                Assert(vocals.Placements[0].Source.outputAudioMixerGroup==null && vocals.Placements[0].Source.spatialBlend==0,"Existing listener master routing");
                Assert(meter.RuntimeTelemetry.Contains("Mastering Processor Active: YES | Bypass Mix: 0.000"),"Listener mastering active");
                report+="PASS both native vocal sources audibly advancing at Bars 13 and 17; scheduled DSP error <1 source sample.\n";
                foreach(var line in vocals.ScheduleLog) report+=line+"\n";
                report+=meter.Readout+"\n"+meter.RuntimeTelemetry+"\n";
                typeof(BacardiBeatMaker).GetField("bpm",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(maker,120f);
                stageTime=now; stage=9;
            } else if(stage==9 && now-stageTime>.4) {
                Assert(Math.Abs(vocals.Placements[1].Source.pitch-1)<.00001,"No tempo pitch shift");
                Assert(!vocals.Placements[1].Source.isPlaying,"Mismatched tempo waits for regeneration");
                report+="PASS tempo edits end mismatched audio at the scheduled boundary; native pitch remains 1.0.\n";
                vocals.Generate(new AiVocalRequest { Lyrics=BacardiAiVocalGenerator.Hook,Bpm=120 },true); stage=10;
            } else if(stage==10 && !vocals.Busy) {
                Assert(vocals.Result.Request.Bpm==120 && vocals.Placements[1].Vocal==vocals.Result,"Explicit regeneration updates placed audio");
                Assert(vocals.Placements[0].StartBar==13 && vocals.Placements[1].StartBar==17,"Regeneration retains bar placement");
                stageTime=now; stage=11;
            } else if(stage==11 && now-stageTime>.3) {
                Assert(vocals.Placements[1].Source.isPlaying && Math.Abs(vocals.Placements[1].Source.pitch-1)<.00001,"Regenerated vocal playback, original pitch");
                Invoke("PausePlayback"); stageTime=vocals.Timeline.BeatsAt(now); epoch=now; stage=12;
            } else if(stage==12 && now-epoch>.2) {
                Assert(Math.Abs(vocals.Timeline.BeatsAt(now)-stageTime)<.001,"Active vocal pause position");
                Invoke("StartPlayback"); stageTime=now; stage=13;
            } else if(stage==13 && now-stageTime>.3) {
                Assert(vocals.Placements[1].Source.isPlaying && vocals.Placements[1].Source.timeSamples>0,"Active regenerated vocal resumes at musical offset");
                report+="PASS explicit 120 BPM regeneration, preserved Bars 13/17 and active-vocal pause/resume without pitch shifting.\n";
                Invoke("Stop"); Finish(true,"PASS Unity vocal runtime acceptance.");
            }
        } catch(Exception e) { Finish(false,e.ToString()); }
    }
}

