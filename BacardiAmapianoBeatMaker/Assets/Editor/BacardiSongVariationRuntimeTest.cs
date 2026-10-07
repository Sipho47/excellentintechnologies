using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Offline Unity integration. Synthesized local sounds only; never generates vocals.
[InitializeOnLoad]
public static class BacardiSongVariationRuntimeTest {
    const string Key="Bacardi.VariationValidation";
    static BacardiBeatMaker maker;
    static string report="";
    static double deadline;
    static int stage;
    static BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static BacardiSongVariationRuntimeTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() {
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
    static object Field(string name) { return typeof(BacardiBeatMaker).GetField(name,Flags).GetValue(maker); }
    static void Set(string name,object value) { typeof(BacardiBeatMaker).GetField(name,Flags).SetValue(maker,value); }
    static object Call(string name,params object[] args) { return typeof(BacardiBeatMaker).GetMethod(name,Flags).Invoke(maker,args); }
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); report+="PASS: "+name+"\n"; }
    static void Finish(int code) {
        Directory.CreateDirectory("Tools/VariationValidation"); File.WriteAllText("Tools/VariationValidation/unity-results.txt",report);
        Debug.Log(report); SessionState.SetBool(Key,false); EditorApplication.Exit(code);
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if(deadline==0) deadline=EditorApplication.timeSinceStartup+1800;
        if(EditorApplication.timeSinceStartup>deadline) { report+="FAIL: timeout"; Finish(1); return; }
        try {
            if(stage==0) {
                maker=new GameObject("Offline variation acceptance").AddComponent<BacardiBeatMaker>();
                // Use a short authentic seed to keep the 25-file roundtrip practical.
                int seed=0;
                while(BacardiSongVariation.Create(seed,0,2).bars!=56) seed++;
                Set("variationSeed",seed.ToString()); Set("variationPersonality",0); Set("variationAmount",2);
                var cells=(bool[,])Field("steps"); var saved=(bool[,])cells.Clone();
                var velocities=(int[,])((int[,])Field("velocities")).Clone();
                maker.GenerateNewSongVariation();
                var plan=(BacardiSongVariation.Plan)Field("songPlan");
                Check((float)Field("bpm")==104,"variation preserves 104 BPM");
                for(int i=0;i<25;i++) for(int s=0;s<16;s++) CheckCell(cells[i,s]==saved[i,s] && ((int[,])Field("velocities"))[i,s]==velocities[i,s]);
                Check(true,"variation preserves existing groove notes and velocities");
                string json=JsonUtility.ToJson(plan); maker.GenerateNewSongVariation(true);
                Check(json==JsonUtility.ToJson(Field("songPlan")),"seed replay exact in Unity");
                string oldSave=PlayerPrefs.GetString("BacardiPattern","");
                try {
                    Call("Save"); Set("songPlan",null); Call("Load");
                    Check(json==JsonUtility.ToJson(Field("songPlan")),"SAVE/LOAD restores complete plan");
                } finally { PlayerPrefs.SetString("BacardiPattern",oldSave); PlayerPrefs.Save(); }
                Check(!maker.ArrangementApplied && maker.ExportArrangedSong("STEREO")==null,"exports blocked before Apply");
                maker.ApplyVariationToSong();
                Check(maker.ArrangementApplied,"explicit Apply activates the saved 56-bar plan");
                var mix=(float[])Call("ArrangementMix");
                Check(mix.Length/2==(int)Math.Ceiling(56*240.0/104*44100),"PCM exact 56-bar duration");
                foreach(var section in plan.sections) for(int id=0;id<25;id++) for(int s=0;s<16;s++) {
                    int absolute=section.start*16+s;
                    if(!plan.Allows(id,absolute)) CheckCell((int)Call("SongSourceStep",id,absolute)==-1);
                }
                Check(true,"section boundaries and transition permissions match saved plan");
                int silent=0;
                for(int i=0;i<25;i++) {
                    var track=(float[])Call("RenderSound",i,mix.Length/2);
                    bool any=false;
                    for(int n=0;n<track.Length;n++) any |= track[n]!=0;
                    for(int bar=0;bar<56;bar++) if(plan.masks[bar*25+i]==0) {
                        int a=(int)Math.Round(bar*240.0/104*44100), b=(int)Math.Round((bar+1)*240.0/104*44100);
                        for(int n=a*2;n<b*2;n++) CheckCell(track[n]==0);
                    }
                    if(!any) silent++;
                }
                Check(silent>0,"unselected stems remain silent");
                string arranged=maker.ExportArrangedSong("STEREO");
                Check(arranged!=null,"arranged stereo export completes");
                var imported=new BacardiStereoMaster(); imported.Import(Path.Combine(arranged,"Arranged Stereo.wav"));
                Check(imported.Loaded,"POST-ARRANGEMENT MASTER TEST imports rendered float WAV");
                int stereoRate; var stereo=BacardiStereoMaster.ReadWav(Path.Combine(arranged,"Arranged Stereo.wav"),out stereoRate);
                Check(stereo.Length==mix.Length && stereoRate==44100,"stereo duration matches plan");
                for(int n=0;n<mix.Length;n++) CheckCell(mix[n]==stereo[n]);
                Check(true,"stereo retains exact mix without mastering or gain changes");
                string stemFolder=maker.ExportArrangedSong("STEMS");
                Check(stemFolder!=null,"arranged stems export completes after Apply");
                ValidateMidi(Path.Combine(stemFolder,"Arranged Song.mid"),56);
                for(int id=0;id<25;id++) {
                    int rate; var pcm=BacardiStereoMaster.ReadWav(Path.Combine(stemFolder,(id+1).ToString("00")+" "+BacardiBeatMaker.Names[id]+".wav"),out rate);
                    CheckCell(pcm.Length==mix.Length && rate==44100);
                    var expected=(float[])Call("RenderSound",id,mix.Length/2);
                    for(int n=0;n<pcm.Length;n++) CheckCell(pcm[n]==expected[n]);
                }
                Check(true,"all 25 arranged stems align from Bar 1 to Bar 56");
                Check(Field("aiVocals")!=null && Field("stereoPanel")!=null,"REAL vocal and POST-ARRANGEMENT MASTER TEST components retained");
                Call("StartPlayback"); stage=1;
            } else {
                Check(((AudioSource)Field("arrangedSource")).isPlaying && ((AudioClip)Field("arrangedClip")).samples==(int)Math.Ceiling(56*240.0/104*44100),"preview plays whole arranged render");
                Call("Stop"); Check((int)Field("songStep")==0,"STOP resets arrangement to first bar");
                maker.ReturnToOneBarGroove(); Check(((float[])Call("ArrangementMix")).Length<44100*2*6,"legacy one-bar render still works with plan retained");
                report+="No provider calls; no source audio writes.\n"; Finish(0);
            }
        } catch(Exception e) { report+="FAIL: "+e; Finish(1); }
    }
    static void CheckCell(bool ok) { if(!ok) throw new Exception("event/PCM invariant failed"); }
    static int ReadBig(BinaryReader r,int count) { int value=0; while(count-->0) value=value*256+r.ReadByte(); return value; }
    static int ReadVariable(BinaryReader r) { int value=0,b; do { b=r.ReadByte(); value=(value<<7)|(b&127); } while((b&128)!=0); return value; }
    static void ValidateMidi(string path,int bars) {
        using(var r=new BinaryReader(File.OpenRead(path))) {
            CheckCell(new string(r.ReadChars(4))=="MThd" && ReadBig(r,4)==6);
            ReadBig(r,2); CheckCell(ReadBig(r,2)==26 && ReadBig(r,2)==960);
            for(int track=0;track<26;track++) {
                CheckCell(new string(r.ReadChars(4))=="MTrk");
                int length=ReadBig(r,4),tick=0,noteOns=0; long end=r.BaseStream.Position+length;
                var expectedTicks=new System.Collections.Generic.List<int>();
                if(track>0) for(int s=0;s<bars*16;s++) if((int)Call("SongSourceStep",track-1,s)>=0) {
                    int count=track-1==2 || track-1==4 ? 3 : 1;
                    while(count-->0) expectedTicks.Add((int)Math.Round((s+(s%2==1?(float)Field("swing"):0))*240));
                }
                while(r.BaseStream.Position<end) {
                    tick+=ReadVariable(r); int type=r.ReadByte();
                    if(type==255) { r.ReadByte(); int n=ReadVariable(r); r.ReadBytes(n); }
                    else { int note=r.ReadByte(), velocity=r.ReadByte(); if((type&240)==144 && velocity>0) { CheckCell(noteOns<expectedTicks.Count && tick==expectedTicks[noteOns]); noteOns++; } }
                }
                CheckCell(tick==bars*3840);
                if(track>0) {
                    int expected=0,id=track-1;
                    for(int s=0;s<bars*16;s++) if((int)Call("SongSourceStep",id,s)>=0) expected+=id==2 || id==4 ? 3 : 1;
                    CheckCell(noteOns==expected);
                }
            }
        }
        Check(true,"MIDI section duration and every melodic event match PCM/playback resolver");
    }
}
