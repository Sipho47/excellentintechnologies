using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Synthetic 46-bar fixture only. Never reads or exports the user's active song.
[InitializeOnLoad]
public static class BacardiArrangedVocalMixRuntimeTest {
    const string Key="Bacardi.ArrangedVocalMixRuntime";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static string report="",root;
    static BacardiBeatMaker maker;
    static BacardiAiVocalGenerator vocals;
    static BacardiArrangedVocalMixRuntimeTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() { SessionState.SetBool(Key,true); EditorApplication.isPlaying=true; }
    static object Get(string name) { return typeof(BacardiBeatMaker).GetField(name,Flags).GetValue(maker); }
    static void Set(string name,object value) { typeof(BacardiBeatMaker).GetField(name,Flags).SetValue(maker,value); }
    static void Check(bool ok,string text) { if(!ok) throw new Exception(text); }
    static void Pass(string text) { report+="PASS "+text+"\n"; }
    static BacardiAiVocalGenerator.Placement Placement(int bar,float left,float right) {
        var q=new AiVocalRequest { Lyrics="Come closer\nMove with me\nAll night\nStay with me",Bpm=120,Bars=4,Voice="Female",Role="Lead",Key="A",Scale="Minor" };
        var pcm=new float[(int)Math.Round(q.Seconds*44100)*2];
        for(int n=0;n<pcm.Length;n+=2) { pcm[n]=left; pcm[n+1]=right; }
        return new BacardiAiVocalGenerator.Placement { StartBar=bar,Vocal=new AiVocalResult { Request=q,Source="REAL_AI",IsMock=false,Provider="Offline PCM fixture",Pcm=pcm } };
    }
    static float[] Export(string name,out double gain) {
        string folder=Path.Combine(root,name);
        Check(maker.ExportArrangedStereoWithVocals(folder)!=null,"Export failed: "+Get("status"));
        int rate; var pcm=BacardiStereoMaster.ReadWav(Path.Combine(folder,"Arranged Stereo + Vocals.wav"),out rate);
        Check(rate==44100,"Sample rate changed");
        string text=File.ReadAllText(Path.Combine(folder,"Arranged Stereo + Vocals.txt"));
        string value=Array.Find(text.Split('\n'),s=>s.StartsWith("Safety gain: ")).Substring(13).Split(' ')[0];
        gain=double.Parse(value,CultureInfo.InvariantCulture);
        Check(text.Contains("Included vocal placements: "+vocals.Placements.Count)&&text.Contains("No normalization, limiter, or Spotify mastering"),"Metadata missing");
        foreach(var p in vocals.Placements) Check(text.Contains("Vocal: REAL_AI")&&text.Contains("Start bar: "+p.StartBar)&&text.Contains("Start frame: "+BacardiVocalTimeline.FrameAtBar(p.StartBar,120)),"Placement/source metadata missing");
        return pcm;
    }
    static void Compare(float[] exported,float[] instrument,double gain) {
        Check(exported.Length==instrument.Length,"Song duration changed");
        for(int n=0;n<exported.Length;n++) {
            float sum=instrument[n];
            foreach(var p in vocals.Placements) {
                long offset=n-BacardiVocalTimeline.FrameAtBar(p.StartBar,120)*2;
                if(offset>=0&&offset<p.Vocal.Pcm.Length) sum+=p.Vocal.Pcm[(int)offset];
            }
            Check(exported[n]==(float)(sum*gain),"Mix/channel/placement mismatch at sample "+n);
            Check(Math.Abs(exported[n])<=1,"Combined output clips");
        }
    }
    static void Reject(string name) {
        string folder=Path.Combine(root,name);
        Check(maker.ExportArrangedStereoWithVocals(folder)==null&&!Directory.Exists(folder),"Invalid export wrote files: "+name);
        Pass(name+" rejected before output creation");
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        SessionState.SetBool(Key,false); bool ok=false;
        try {
            root=Path.GetFullPath("Tools/CombinedVocalMixValidation/fixture-"+Guid.NewGuid().ToString("N"));
            maker=new GameObject("Offline combined-mix fixture").AddComponent<BacardiBeatMaker>();
            vocals=(BacardiAiVocalGenerator)Get("aiVocals");
            Reject("unapplied arrangement"); Set("bpm",120f);
            var plan=BacardiSongVariation.Create(123,1,2); plan.bars=46; plan.masks=new int[46*25];
            int start=0; int[] lengths={4,4,12,6,12,8};
            for(int i=0;i<6;i++) { plan.sections[i].start=start; plan.sections[i].bars=lengths[i]; start+=lengths[i]; }
            for(int bar=0;bar<46;bar++) plan.masks[bar*25+8]=1;
            Set("songPlan",plan); maker.ApplyVariationToSong(); Check(maker.ArrangementLength==46,"Fixture not applied");
            var steps=(bool[,])Get("steps"); Array.Clear(steps,0,steps.Length); steps[8,0]=true;
            var sounds=(float[][])Get("sounds"); sounds[8]=new float[128]; for(int i=0;i<128;i++) sounds[8][i]=.05f;
            Set("targetLufs",true); Set("bypassMaster",false);
            var instrumental=(float[])typeof(BacardiBeatMaker).GetMethod("ArrangementMix",Flags).Invoke(maker,null);
            Check(Array.Exists(instrumental,x=>x!=0),"Instrumental fixture silent");
            string planBefore=JsonUtility.ToJson(Get("appliedSongPlan"));
            double gain; var pcm=Export("no-vocals",out gain); Compare(pcm,instrumental,gain); Check(gain==1,"Unnecessary attenuation");
            Pass("zero-placement mix equals instrumental exactly; 46-bar duration retained; no mastering despite enabled target");
            var first=Placement(13,.01f,.02f); vocals.Placements.Add(first);
            pcm=Export("single",out gain); Compare(pcm,instrumental,gain); Check(gain==1,"Single placement unnecessarily attenuated");
            long firstSample=BacardiVocalTimeline.FrameAtBar(13,120)*2;
            Check(pcm[(int)firstSample]-instrumental[(int)firstSample]>.009f,"Vocal absent at Bar 13");
            Pass("instrumental present; REAL_AI PCM begins at Bar 13; stereo channels retained; exact zero vocal contribution outside placement");
            vocals.Placements.Add(Placement(17,.03f,-.04f)); vocals.Placements.Add(Placement(15,-.02f,.03f));
            pcm=Export("multiple-overlap",out gain); Compare(pcm,instrumental,gain); Check(gain==1,"Low-level overlap attenuated");
            Pass("three placements at Bars 13, 17 and 15 sum correctly, including overlaps and right/left channel polarity");
            string legacy=Path.Combine(root,"legacy"); Check(maker.ExportArrangedSong("STEREO",legacy)!=null,"Legacy export failed");
            int rate; var old=BacardiStereoMaster.ReadWav(Path.Combine(legacy,"Arranged Stereo.wav"),out rate);
            Check(old.Length==instrumental.Length,"Legacy duration changed"); for(int n=0;n<old.Length;n++) Check(old[n]==instrumental[n],"Legacy export contains vocals or changed gain");
            Pass("original EXPORT ARRANGED STEREO WAV remains bit-exact instrumental-only with vocals placed");
            vocals.Placements.Clear(); vocals.Placements.Add(Placement(13,.8f,.6f)); vocals.Placements.Add(Placement(13,.8f,.6f));
            pcm=Export("clipping-safety",out gain); Compare(pcm,instrumental,gain); Check(gain<1,"Clipping protection absent");
            Pass("clipping protection uses one common constant attenuation, preserving mix balance and all placement timing");
            Check(maker.ExportArrangedStereoWithVocals(Path.Combine(root,"clipping-safety"))==null,"Existing output overwritten");
            Pass("existing output files protected");
            vocals.Placements.Clear(); first=Placement(13,.01f,.02f); vocals.Placements.Add(first);
            first.Vocal.Request.Bpm=104; Reject("tempo mismatch"); first.Vocal.Request.Bpm=120;
            first.StartBar=0; Reject("start before Bar 1"); first.StartBar=44; Reject("vocal beyond song end"); first.StartBar=43;
            first.Vocal.Pcm=new float[first.Vocal.Pcm.Length+2]; Reject("PCM beyond song end");
            first.StartBar=13; first.Vocal.Pcm=new float[0]; Reject("empty audio");
            first.Vocal.Pcm=new[]{float.NaN,0f}; Reject("invalid PCM");
            first.Vocal.Pcm=new[]{.01f,.02f}; first.Vocal.SampleRate=48000; Reject("wrong sample rate");
            Check(JsonUtility.ToJson(Get("appliedSongPlan"))==planBefore&&(float)Get("bpm")==120f&&(bool)Get("targetLufs"),"Export changed arrangement/BPM/mastering settings");
            Pass("arrangement plan, BPM and mastering settings unchanged"); ok=true;
        } catch(Exception e) { report+="FAIL "+e+"\n"; }
        finally {
            if(maker!=null) { vocals.Placements.Clear(); UnityEngine.Object.DestroyImmediate(maker.gameObject); }
            Directory.CreateDirectory("Tools/CombinedVocalMixValidation");
            File.WriteAllText("Tools/CombinedVocalMixValidation/runtime-results.txt",(ok?"PASS\n":"FAIL\n")+report+"Synthetic offline fixtures only. PART 8 was not read, exported or mastered. No API calls.\n");
            EditorApplication.Exit(ok?0:1);
        }
    }
}
