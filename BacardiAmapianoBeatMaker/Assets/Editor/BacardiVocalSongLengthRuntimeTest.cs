using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BacardiVocalSongLengthRuntimeTest {
    const string Key="Bacardi.VocalSongLengthRuntime";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static BacardiBeatMaker maker; static string report="";
    static BacardiVocalSongLengthRuntimeTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() { SessionState.SetBool(Key,true); EditorApplication.isPlaying=true; }
    static object Get(string key) { return typeof(BacardiBeatMaker).GetField(key,Flags).GetValue(maker); }
    static void Set(string key,object value) { typeof(BacardiBeatMaker).GetField(key,Flags).SetValue(maker,value); }
    static void Call(string key) { typeof(BacardiBeatMaker).GetMethod(key,Flags).Invoke(maker,null); }
    static BacardiAiVocalGenerator Vocals { get { return (BacardiAiVocalGenerator)Get("aiVocals"); } }
    static void Check(bool ok,string text) { if(!ok) throw new Exception(text); }
    static void Pass(string text) { report+="PASS "+text+"\n"; }
    static BacardiSongVariation.Plan Plan(int bars) {
        if(bars==32) return BacardiSongVariation.Create(42,1,0);
        for(int seed=0;seed<10000;seed++) { var p=BacardiSongVariation.Create(seed,1,2); if(p.bars==bars) return p; }
        throw new Exception("Fixture plan unavailable");
    }
    static void Wait() {
        double end=EditorApplication.timeSinceStartup+30;
        while(Vocals.Busy&&EditorApplication.timeSinceStartup<end) { Thread.Sleep(10); Vocals.Tick(AudioSettings.dspTime,104,AudioSettings.dspTime,1); }
        Check(!Vocals.Busy&&Vocals.State!="ERROR","Offline vocal operation failed: "+Vocals.Message);
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        SessionState.SetBool(Key,false); bool ok=false;
        var trap=new TcpListener(IPAddress.Loopback,0); string oldEndpoint=Environment.GetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL");
        bool hadPattern=PlayerPrefs.HasKey("BacardiPattern"); string oldPattern=PlayerPrefs.GetString("BacardiPattern","");
        try {
            trap.Start(); Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL","http://127.0.0.1:"+((IPEndPoint)trap.LocalEndpoint).Port+"/");
            maker=new GameObject("Offline song length fixture").AddComponent<BacardiBeatMaker>();
            Check(Vocals.SongBarsDisplay=="32"&&!Vocals.SongBarsReadOnly,"Default behavior changed");
            Set("songPlan",Plan(46)); Call("SyncVocalSongLength");
            Check(Vocals.SongBarsDisplay=="46"&&Vocals.SongBarsReadOnly,"Pending plan did not populate/read-lock length");
            Pass("valid pending 46-bar variation automatically populates read-only Song bars=46 without manual entry");
            maker.ApplyVariationToSong(); Check(Vocals.SynchronizedSongBars==maker.ArrangementLength&&Vocals.SongBarsDisplay=="46","APPLY did not sync immediately");
            Pass("APPLY immediately synchronizes vocal song length to active arrangement length");
            Set("songPlan",Plan(32)); Call("SyncVocalSongLength"); Check(Vocals.SynchronizedSongBars==46,"Pending plan overrode active arrangement");
            Pass("applied arrangement takes precedence over a different pending plan");
            string root=Path.GetFullPath("Tools/VocalSongLengthValidation/fixture-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var q=new AiVocalRequest { Lyrics="Come closer\nMove with me\nAll night\nStay with me",Bpm=104,Bars=8,Voice="Female",Role="Lead",Key="A",Scale="Minor" };
            var result=new AiVocalResult { Request=q,Source="REAL_AI",IsMock=false,Provider="ElevenLabs Music v2.5 / two-stem vocal separation",Pcm=new float[(int)Math.Round(q.Seconds*44100)*2] };
            for(int n=0;n<result.Pcm.Length;n++) result.Pcm[n]=.01f;
            string wav=Path.Combine(root,"AI_VOCAL_LEAD_104BPM_AMINOR.wav"); AiVocalWav.Write(wav,result.Pcm); BacardiSavedVocalLoader.WriteMetadata(wav,result);
            Vocals.LoadSavedVocal(wav,104); Wait(); Check(Vocals.SongBarsDisplay=="46"&&Vocals.Result.Request.Bars==8,"Vocal metadata changed full-song length");
            Pass("saved REAL_AI reload retains 46-bar song length and independent 8-bar vocal duration");
            Vocals.AddToSong(13); var original=Vocals.Placements[0];
            Vocals.Save(true,104,13,32); Wait(); string stem=Vocals.Message.Substring("Saved: ".Length); int rate;
            var pcm=BacardiStereoMaster.ReadWav(stem,out rate);
            Check(pcm.Length==BacardiVocalTimeline.FrameAtBar(47,104)*2&&rate==44100,"Export used stale 32-bar argument");
            Check(Vocals.Placements[0].StartBar==13&&Vocals.Placements[0].Vocal.Request.Bars==8,"Placement timing changed");
            Pass("placement remains at Bar 13; aligned stem uses synchronized 46-bar duration even with stale 32-bar argument");
            Vocals.AddToSong(39); var retained=Vocals.Placements[1]; maker.ApplyVariationToSong();
            Check(Vocals.SongBarsDisplay=="32"&&Vocals.PlacementValidationStatus.Contains("32-bar"),"Shortened arrangement not validated");
            Check(Vocals.Placements.Count==2&&ReferenceEquals(Vocals.Placements[0],original)&&ReferenceEquals(Vocals.Placements[1],retained)&&retained.StartBar==39&&retained.Vocal.Request.Bars==8,"Invalid placements modified/deleted");
            bool blocked=false; try { Vocals.Save(true,104,13,46); } catch(InvalidOperationException) { blocked=true; }
            Check(blocked&&!Vocals.Busy,"Invalid placement export not blocked");
            blocked=false; try { Vocals.AddToSong(1); } catch(InvalidOperationException) { blocked=true; } Check(blocked,"Add-to-song ignored invalid placements");
            Pass("32-bar shortening flags invalid placement and blocks affected operations without shifting, truncating, deleting or extending it");
            Vocals.Placements.RemoveAt(1); Check(Vocals.PlacementValidationStatus=="","Removed invalid placement still flagged");
            Pass("removing invalid placement clears validation error");
            maker.ReturnToOneBarGroove(); Call("SyncVocalSongLength"); Check(!Vocals.SongBarsReadOnly&&Vocals.SongBarsDisplay=="32","One-bar mode did not restore editable default");
            Pass("explicit one-bar mode restores editable/default behavior even while plan is retained for reapplying");
            Set("variationAmount",2); Set("variationSeed","123"); maker.GenerateNewSongVariation(true);
            Check(Vocals.SongBarsReadOnly&&Vocals.SynchronizedSongBars==((BacardiSongVariation.Plan)Get("songPlan")).bars,"Generation did not immediately synchronize");
            Pass("newly generated different-length variation automatically updates and locks Song bars");
            Set("songPlan",Plan(46)); maker.ApplyVariationToSong(); Call("Save");
            UnityEngine.Object.DestroyImmediate(maker.gameObject); maker=new GameObject("Restarted offline fixture").AddComponent<BacardiBeatMaker>();
            Call("Load"); Check(Vocals.SongBarsDisplay=="46"&&Vocals.SongBarsReadOnly&&!maker.ArrangementApplied,"Saved pattern reload did not synchronize pending plan");
            maker.ApplyVariationToSong(); Check(Vocals.SongBarsDisplay=="46"&&maker.ArrangementLength==46,"Restart/reload/apply lost length");
            Pass("restart + saved pattern/variation reload + APPLY restores 46 bars without manual entry");
            Check((float)Get("bpm")==104f&&q.Bars==8&&!trap.Pending(),"BPM/vocal duration changed or provider called");
            Pass("BPM and vocal duration unchanged; zero provider/API or proxy calls"); ok=true;
        } catch(Exception e) { report+="FAIL "+e+"\n"; }
        finally {
            if(maker!=null) UnityEngine.Object.DestroyImmediate(maker.gameObject);
            if(hadPattern) PlayerPrefs.SetString("BacardiPattern",oldPattern); else PlayerPrefs.DeleteKey("BacardiPattern");
            Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL",oldEndpoint); trap.Stop();
            Directory.CreateDirectory("Tools/VocalSongLengthValidation");
            File.WriteAllText("Tools/VocalSongLengthValidation/runtime-results.txt",(ok?"PASS\n":"FAIL\n")+report+"Synthetic offline fixtures only. No user song export/mastering. PART 9 not started.\n");
            EditorApplication.Exit(ok?0:1);
        }
    }
}
