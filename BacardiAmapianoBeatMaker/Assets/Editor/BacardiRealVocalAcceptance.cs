using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Explicitly invoked only: this test makes one paid composition + separation.
// It validates a REAL take's technical workflow, not the model's musical/lyric correctness.
[InitializeOnLoad]
public static class BacardiRealVocalAcceptance {
    const string Key="Bacardi.RealVocalAcceptance";
    static int stage; static double deadline, at, epoch;
    static bool first, second;
    static BacardiBeatMaker maker; static BacardiAiVocalGenerator vocals; static BacardiMasterLufsMeter meter;
    static string report="";
    static BacardiRealVocalAcceptance() { EditorApplication.update+=Tick; }
    [MenuItem("Bacardi/Validate REAL Sung Vocal (uses API)")]
    public static void Run() {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode first.");
        if(UnityEngine.Object.FindFirstObjectByType<BacardiBeatMaker>()==null) throw new Exception("Open the Bacardi scene first.");
        SessionState.SetBool(Key,true); SessionState.SetBool(Key+"cli",false);
        stage=0; deadline=EditorApplication.timeSinceStartup+720; EditorApplication.isPlaying=true;
    }
    public static void RunCommandLine() {
        EditorSceneManager.OpenScene("Assets/Scenes/BacardiBeatMaker.unity"); Run(); SessionState.SetBool(Key+"cli",true);
    }
    static object Field(string name) { return typeof(BacardiBeatMaker).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(maker); }
    static void Invoke(string name) { typeof(BacardiBeatMaker).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(maker,null); }
    static void Assert(bool value,string message) { if(!value) throw new Exception(message); }
    static void Finish(bool ok,string message) {
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Real-Sung-Acceptance.txt",(ok?"PASS TECHNICAL REAL-AUDIO WORKFLOW\n":"FAIL / BLOCKED\n")+report+message+
            "\nAudition is still required to confirm sung words, female voice, actual C Major melody and absence of backing/reverb bleed. Request metadata alone is not proof.\n");
        SessionState.SetBool(Key,false); Debug.Log(message);
        if(SessionState.GetBool(Key+"cli",false)) EditorApplication.Exit(ok?0:1); else EditorApplication.isPlaying=false;
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)) return;
        if(deadline==0) deadline=EditorApplication.timeSinceStartup+720;
        if(EditorApplication.timeSinceStartup>deadline) { if(vocals!=null) vocals.Cancel(); Finish(false,"Timed out; upstream work may already be billed."); return; }
        if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try {
            double now=AudioSettings.dspTime;
            if(stage==0) {
                maker=UnityEngine.Object.FindFirstObjectByType<BacardiBeatMaker>(); if(maker==null) return;
                vocals=Field("aiVocals") as BacardiAiVocalGenerator; meter=UnityEngine.Object.FindFirstObjectByType<BacardiMasterLufsMeter>(); if(vocals==null || meter==null) return;
                typeof(BacardiBeatMaker).GetField("bpm",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(maker,104f);
                Invoke("AnalyzeMaster"); Invoke("Stop");
                vocals.Provider=new ElevenLabsAiVocalProvider();
                vocals.Generate(new AiVocalRequest { Lyrics=BacardiAiVocalGenerator.Hook,Bpm=104,Voice="Female",Role="Lead",Key="C",Scale="Major",Bars=4,Dry=true,RelativeMinor=false });
                stage=1;
            } else if(stage==1 && !vocals.Busy) {
                Assert(vocals.State!="ERROR" && vocals.Result!=null,"Real generation: "+vocals.Message);
                Assert(!vocals.Result.IsMock && vocals.Result.Provider.Contains("ElevenLabs"),"A real ElevenLabs take is required; mock fixture rejected.");
                Assert(vocals.Result.Request.Lyrics==BacardiAiVocalGenerator.Hook && vocals.Result.Request.Bpm==104,"Exact hook and requested BPM");
                report+="REAL provider: "+vocals.Result.Provider+"\nRequested Female, C Major, 104 BPM, four bars, exact four-line hook.\n";
                report+="Vocal peak: "+vocals.Result.PeakDb+" dBFS / LUFS-I: "+vocals.Result.Integrated+"\n";
                vocals.Preview(104); at=now; stage=2;
            } else if(stage==2 && now-at>.3) {
                var source=typeof(BacardiAiVocalGenerator).GetField("preview",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(vocals) as AudioSource;
                Assert(source.isPlaying && source.timeSamples>0 && source.pitch==1,"Real native preview at unchanged pitch");
                vocals.StopPreview(); vocals.Save(false,104,13,32); stage=3;
            } else if(stage==3 && !vocals.Busy) {
                Assert(vocals.State!="ERROR","Real WAV save: "+vocals.Message); report+=vocals.Message+"\n";
                vocals.AddToSong(13); vocals.AddToSong(17); vocals.Save(true,104,13,32); stage=4;
            } else if(stage==4 && !vocals.Busy) {
                Assert(vocals.State!="ERROR","Aligned real WAV: "+vocals.Message); report+=vocals.Message+"\n";
                Invoke("StartPlayback"); epoch=(double)Field("nextTime"); stage=5;
            } else if(stage==5) {
                double beat=vocals.Timeline.BeatsAt(now);
                if(beat>48.2 && beat<49.8) first|=vocals.Placements[0].Source.isPlaying;
                if(beat>64.2 && beat<65.8) second|=vocals.Placements[1].Source.isPlaying;
                if(beat<80.1) return;
                Assert(first && second,"Real hooks actually play at Bars 13 and 17");
                Assert(Math.Abs(vocals.Placements[0].ScheduledDsp-(epoch+48*60/104.0))<1.0/44100,"Bar 13 DSP mapping");
                Assert(Math.Abs(vocals.Placements[1].ScheduledDsp-(epoch+64*60/104.0))<1.0/44100,"Bar 17 DSP mapping");
                var tp=Regex.Match(meter.Readout,@"True Peak: ([-+0-9.]+) dBTP");
                Assert(tp.Success && double.Parse(tp.Groups[1].Value,CultureInfo.InvariantCulture)<=-1.3,"Full-song master safety readout");
                Assert(meter.RuntimeTelemetry.Contains("Mastering Processor Active: YES"),"Existing listener master path");
                report+=meter.Readout+"\n"+meter.RuntimeTelemetry+"\n";
                foreach(var log in vocals.ScheduleLog) report+=log+"\n";
                Invoke("Stop"); Finish(true,"PASS real-audio preview, placements, aligned export and existing master safety through both complete hooks.");
            }
        } catch(Exception e) { Finish(false,e.ToString()); }
    }
}
