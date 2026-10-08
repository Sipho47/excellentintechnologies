using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BacardiArrangedVocalPreviewRuntimeTest {
    const string Key="Bacardi.ArrangedVocalPreviewRuntime";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static string report="";
    static BacardiBeatMaker maker;
    static BacardiAiVocalGenerator vocals;
    static BacardiArrangedVocalPreviewRuntimeTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() { SessionState.SetBool(Key,true); EditorApplication.isPlaying=true; }
    sealed class Spy : IAiVocalProvider {
        public int Calls;
        public string Name { get { return "Offline generation spy"; } }
        public Task<AiVocalResult> GenerateAsync(AiVocalRequest q,IProgress<AiVocalProgress> p,CancellationToken t) { Calls++; throw new Exception("Provider called during preview"); }
    }
    static object Get(string name) { return typeof(BacardiBeatMaker).GetField(name,Flags).GetValue(maker); }
    static void Set(string name,object value) { typeof(BacardiBeatMaker).GetField(name,Flags).SetValue(maker,value); }
    static void Check(bool ok,string text) { if(!ok) throw new Exception(text); }
    static void Pass(string text) { report+="PASS "+text+"\n"; }
    static BacardiAiVocalGenerator.Placement Placement(int bar,float left,float right) {
        var q=new AiVocalRequest { Lyrics="Come closer\nMove with me\nAll night\nStay with me",Bpm=120,Bars=4,Voice="Female",Role="Lead",Key="A",Scale="Minor" };
        var pcm=new float[(int)Math.Round(q.Seconds*44100)*2];
        for(int n=0;n<pcm.Length;n+=2) { pcm[n]=left; pcm[n+1]=right; }
        var child=new GameObject("Offline placed vocal"); child.transform.SetParent(maker.transform);
        return new BacardiAiVocalGenerator.Placement { StartBar=bar,Vocal=new AiVocalResult { Request=q,Pcm=pcm,Source="REAL_AI",IsMock=false,Provider="Offline PCM fixture" },Source=child.AddComponent<AudioSource>() };
    }
    static float[] Preview() {
        maker.PreviewArrangedSong(); var clip=(AudioClip)Get("arrangedClip");
        Check(clip!=null&&(bool)Get("arrangedPreviewRunning"),"Preview failed: "+Get("status"));
        Check(clip.channels==2&&clip.frequency==44100,"Preview format changed");
        var data=new float[clip.samples*2]; Check(clip.GetData(data,0),"Cannot read actual preview clip"); return data;
    }
    static void Equal(float[] actual,float[] expected,string text) {
        Check(actual.Length==expected.Length,text+" length");
        for(int n=0;n<actual.Length;n++) if(actual[n]!=expected[n]) throw new Exception(text+" mismatch at "+n);
    }
    static float[] Expected(float[] instrument) {
        var result=(float[])instrument.Clone();
        foreach(var p in vocals.Placements) {
            // Independent expected placement arithmetic; compare actual AudioClip samples.
            int offset=checked((int)Math.Round((p.StartBar-1)*240.0/120*44100)*2);
            for(int n=0;n<p.Vocal.Pcm.Length;n++) result[offset+n]+=p.Vocal.Pcm[n];
        }
        double peak=0; foreach(float sample in result) peak=Math.Max(peak,Math.Abs((double)sample));
        if(peak>1) for(int n=0;n<result.Length;n++) result[n]=(float)(result[n]/peak);
        return result;
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        SessionState.SetBool(Key,false); bool ok=false;
        var trap=new TcpListener(IPAddress.Loopback,0); string oldEndpoint=Environment.GetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL");
        try {
            trap.Start(); Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL","http://127.0.0.1:"+((IPEndPoint)trap.LocalEndpoint).Port+"/");
            maker=new GameObject("Offline arranged preview fixture").AddComponent<BacardiBeatMaker>(); Set("bpm",120f);
            vocals=(BacardiAiVocalGenerator)Get("aiVocals"); var spy=new Spy(); vocals.Provider=spy;
            var plan=BacardiSongVariation.Create(123,1,2); plan.bars=46; plan.masks=new int[46*25];
            int start=0; int[] lengths={4,4,12,6,12,8}; for(int i=0;i<6;i++) { plan.sections[i].start=start; plan.sections[i].bars=lengths[i]; start+=lengths[i]; }
            for(int bar=0;bar<46;bar++) plan.masks[bar*25+8]=1;
            Set("songPlan",plan); maker.ApplyVariationToSong(); string originalPlan=JsonUtility.ToJson(Get("appliedSongPlan"));
            var steps=(bool[,])Get("steps"); Array.Clear(steps,0,steps.Length); steps[8,0]=true;
            var sounds=(float[][])Get("sounds"); sounds[8]=new float[128]; for(int n=0;n<128;n++) sounds[8][n]=.05f;
            var instrument=(float[])typeof(BacardiBeatMaker).GetMethod("ArrangementMix",Flags).Invoke(maker,null);
            Check(Array.Exists(instrument,x=>x!=0),"Instrumental fixture silent"); Equal(Preview(),instrument,"Instrument-only preview");
            Pass("instrumental content present in actual arranged preview AudioClip");
            var first=Placement(13,.01f,-.02f); vocals.Placements.Add(first);
            first.Source.clip=AudioClip.Create("Separate vocal playback fixture",1000,2,44100,false); first.Source.loop=true; first.Source.Play();
            var separatePreview=(AudioSource)typeof(BacardiAiVocalGenerator).GetField("preview",Flags).GetValue(vocals);
            separatePreview.clip=first.Source.clip; separatePreview.loop=true; separatePreview.Play();
            var single=Preview(); Equal(single,Expected(instrument),"Single placement preview");
            Check(!first.Source.isPlaying&&!separatePreview.isPlaying,"Separate vocal playback still active");
            Pass("REAL_AI vocal PCM present at Bar 13 only, with exact zero vocal contribution before/after placement; separate vocal playback stopped");
            Check(single.Length==(int)Math.Ceiling(46*240.0/120*44100)*2,"Song duration changed");
            Pass("46-bar duration, 120 BPM and stereo channel alignment preserved");
            vocals.Placements.Add(Placement(17,.03f,.04f)); vocals.Placements.Add(Placement(15,-.02f,.03f));
            var multiple=Preview(); Equal(multiple,Expected(instrument),"Multiple/overlapping placements");
            Pass("multiple placements and overlaps at Bars 13, 17 and 15 preview correctly");
            var priorClip=(AudioClip)Get("arrangedClip"); Equal(Preview(),multiple,"Repeated preview");
            Check((AudioClip)Get("arrangedClip")!=priorClip,"Preview reused baked clip");
            Pass("repeated preview builds a fresh clip without double-summing vocals");
            // Same removal operation as the existing REMOVE button.
            var removed=vocals.Placements[0]; removed.Source.Stop(); UnityEngine.Object.Destroy(removed.Source.gameObject); vocals.Placements.RemoveAt(0);
            typeof(BacardiAiVocalGenerator).GetMethod("ReleaseUnusedClips",Flags).Invoke(vocals,null);
            Equal(Preview(),Expected(instrument),"Removed placement preview");
            Pass("REMOVE is reflected on the next preview; removed PCM is absent");
            var meter=(BacardiMasterLufsMeter)Get("lufsMeter");
            typeof(BacardiBeatMaker).GetMethod("Stop",Flags).Invoke(maker,null); meter.PostArrangementPlayback=false;
            meter.SetTarget(true,12); Set("targetLufs",true); Set("targetGainDb",12d);
            Preview(); Check(meter.PostArrangementPlayback,"Mastering bypass flag not set");
            float[] listener={.1f,-.2f,.3f,-.4f}; var listenerExpected=(float[])listener.Clone();
            typeof(BacardiMasterLufsMeter).GetMethod("OnAudioFilterRead",Flags).Invoke(meter,new object[]{listener,2}); Equal(listener,listenerExpected,"Unprocessed listener PCM");
            Pass("arranged preview bypasses listener mastering; actual listener buffer unchanged with target gain enabled");
            typeof(BacardiBeatMaker).GetMethod("Stop",Flags).Invoke(maker,null); Check(!meter.PostArrangementPlayback,"STOP failed to restore listener state");
            Pass("STOP restores previous listener playback state");
            meter.PostArrangementPlayback=true; Preview(); typeof(BacardiBeatMaker).GetMethod("Stop",Flags).Invoke(maker,null); Check(meter.PostArrangementPlayback,"Prior true state lost");
            meter.PostArrangementPlayback=false; Preview(); ((AudioSource)Get("arrangedSource")).Stop(); typeof(BacardiBeatMaker).GetMethod("Update",Flags).Invoke(maker,null);
            Check(!meter.PostArrangementPlayback&&!(bool)Get("arrangedPreviewRunning"),"Natural completion did not restore state");
            Pass("prior true state and natural preview completion restore correctly");
            vocals.Placements[0].Vocal.Request.Bpm=104; maker.PreviewArrangedSong();
            Check(Get("arrangedClip")==null&&!(bool)Get("arrangedPreviewRunning")&&!meter.PostArrangementPlayback&&((string)Get("status")).Contains("BPM"),"Tempo mismatch accepted or stale playback retained");
            vocals.Placements[0].Vocal.Request.Bpm=120;
            Pass("tempo mismatch rejected without stale preview playback or listener override");
            string folder=Path.GetFullPath("Tools/ArrangedVocalPreviewValidation/instrument-fixture-"+Guid.NewGuid().ToString("N"));
            Check(maker.ExportArrangedSong("STEREO",folder)!=null,"Instrument export failed"); int rate;
            Equal(BacardiStereoMaster.ReadWav(Path.Combine(folder,"Arranged Stereo.wav"),out rate),instrument,"Original export");
            Pass("original instrumental-only export remains sample-exact with vocals placed");
            Check(JsonUtility.ToJson(Get("appliedSongPlan"))==originalPlan&&(float)Get("bpm")==120f&&(bool)Get("targetLufs"),"Saved plan/tempo/master settings changed");
            Pass("saved arrangement structure, tempo and mastering settings unchanged");
            Check(spy.Calls==0&&!trap.Pending(),"Provider/network called"); Pass("zero provider/API calls or proxy connections"); ok=true;
        } catch(Exception e) { report+="FAIL "+e+"\n"; }
        finally {
            Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL",oldEndpoint); trap.Stop();
            if(maker!=null) UnityEngine.Object.DestroyImmediate(maker.gameObject);
            Directory.CreateDirectory("Tools/ArrangedVocalPreviewValidation");
            File.WriteAllText("Tools/ArrangedVocalPreviewValidation/runtime-results.txt",(ok?"PASS\n":"FAIL\n")+report+"Synthetic offline fixtures only. PART 8 was not exported or mastered.\n");
            EditorApplication.Exit(ok?0:1);
        }
    }
}
