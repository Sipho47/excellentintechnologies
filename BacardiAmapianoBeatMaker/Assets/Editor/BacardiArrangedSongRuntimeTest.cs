using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// Isolated local fixture only. No provider invocation, source writes or user exports.
[InitializeOnLoad]
public static class BacardiArrangedSongRuntimeTest {
    const string Key="Bacardi.ArrangedSongValidation";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static string report="",folder;
    static BacardiBeatMaker maker;
    static bool ran;
    static BacardiArrangedSongRuntimeTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() { SessionState.SetBool(Key,true); EditorApplication.isPlaying=true; }
    static object Get(string key) { return typeof(BacardiBeatMaker).GetField(key,Flags).GetValue(maker); }
    static void Set(string key,object value) { typeof(BacardiBeatMaker).GetField(key,Flags).SetValue(maker,value); }
    static object Call(string key,params object[] args) { return typeof(BacardiBeatMaker).GetMethod(key,Flags).Invoke(maker,args); }
    static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
    static void Pass(string text) { report+="PASS: "+text+"\n"; }
    static Dictionary<string,string> HashSources() {
        var hashes=new Dictionary<string,string>();
        string root=Path.GetFullPath("../../Assets");
        using(var sha=SHA256.Create()) foreach(string file in Directory.GetFiles(root,"*.wav",SearchOption.AllDirectories))
            using(var stream=File.OpenRead(file)) hashes[file]=BitConverter.ToString(sha.ComputeHash(stream));
        return hashes;
    }
    static void Tick() {
        if(ran || !SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        ran=true;
        int exit=0; string oldSave=PlayerPrefs.GetString("BacardiSongVariation",""); bool hadSave=PlayerPrefs.HasKey("BacardiSongVariation");
        bool paused=AudioListener.pause; AudioListener.pause=true;
        try {
            var hashes=HashSources(); Assert(hashes.Count>0,"No source WAVs checked");
            folder=Path.GetFullPath("Tools/ArrangementValidation/fixture-export"); Directory.CreateDirectory(folder);
            maker=new GameObject("Offline 56-bar arrangement acceptance").AddComponent<BacardiBeatMaker>();
            Assert(maker.ExportArrangedSong("STEREO",Path.Combine(folder,"blocked"))==null && !Directory.Exists(Path.Combine(folder,"blocked")),"Unapplied export wrote files");
            Pass("arranged exports blocked before Apply, without creating files");
            int seed=0; BacardiSongVariation.Plan plan;
            do { plan=BacardiSongVariation.Create(seed++,4,2); } while(plan.bars!=56 && seed<10000);
            Assert(plan.bars==56,"56-bar fixture unavailable");
            PlayerPrefs.SetString("BacardiSongVariation",JsonUtility.ToJson(plan)); Set("songPlan",null);
            float volume=(float)Get("volume"),tempo=(float)Get("bpm"),swing=(float)Get("swing");
            var cells=(bool[,])((bool[,])Get("steps")).Clone(); var velocities=(int[,])((int[,])Get("velocities")).Clone();
            maker.ApplyVariationToSong();
            Assert(maker.ArrangementApplied && maker.ArrangementLength==56,"Apply did not load complete saved plan");
            Assert(maker.ActiveVariation=="PRIVATE SCHOOL / STRONG / Seed "+plan.seed,"Wrong active identity");
            Assert(JsonUtility.ToJson(Get("appliedSongPlan"))==JsonUtility.ToJson(plan),"Apply changed planner data");
            Assert(!ReferenceEquals(Get("songPlan"),Get("appliedSongPlan")),"Applied plan is not an independent copy");
            for(int id=0;id<25;id++) for(int s=0;s<16;s++) Assert(cells[id,s]==((bool[,])Get("steps"))[id,s] && velocities[id,s]==((int[,])Get("velocities"))[id,s],"Apply changed groove/mix");
            Assert(volume==(float)Get("volume") && tempo==(float)Get("bpm") && swing==(float)Get("swing"),"Apply changed mix/tempo/swing");
            Pass("saved PRIVATE SCHOOL + STRONG plan applied as an independent full 56-bar copy, preserving notes, velocities, master level, BPM and swing; seed "+plan.seed);
            // Independent expected event list: permissions intersect the source groove.
            int boundaries=0,transitionEvents=0;
            foreach(var section in plan.sections) {
                Assert(section.start>=0 && section.start+section.bars<=56,"Section outside song"); boundaries++;
                for(int bar=section.start;bar<section.start+section.bars;bar++) {
                    if(section.logEntry<0 || bar<section.logEntry) Assert(plan.masks[bar*25+6]==0,"Logdrum entered early");
                    if(section.bassEntry<0 || bar<section.bassEntry) Assert(plan.masks[bar*25+7]==0,"Bass entered early");
                }
            }
            for(int id=0;id<25;id++) for(int step=0;step<56*16;step++) {
                int expected=-1;
                if(plan.Allows(id,step)) {
                    if(id<21) { if(cells[id,step%16]) expected=step%16; }
                    else for(int s=0;s<16;s++) if(cells[id,s]) { expected=s; break; }
                }
                Assert((int)Call("SongSourceStep",id,step)==expected,"Event differs from section mask");
                if(id>=21 && expected>=0) transitionEvents++;
            }
            Assert(transitionEvents>0 && boundaries==6,"No transitions exercised");
            Pass("all 22,400 stem/step permissions match section boundaries, entrances, exits, pauses, leads, density and transitions; "+transitionEvents+" transition events");
            int frames=(int)Math.Ceiling(56*240.0/tempo*44100);
            // Tail longer than the song must stop at the first inactive bar and never
            // reappear when the stem later re-enters. Test actual production renderer.
            var sounds=(float[][])Get("sounds"); var original=sounds[0];
            var longTail=new float[44100*12]; for(int i=0;i<longTail.Length;i++) longTail[i]=.001f;
            sounds[0]=longTail;
            var tail=(float[])Call("RenderSound",0,frames); VerifySilence(tail,plan,0,tempo); sounds[0]=original;
            Pass("long sample tails are cut at inactive bars without altering source samples");
            string stemFolder=maker.ExportArrangedSong("STEMS",Path.Combine(folder,"stems")); Assert(stemFolder!=null,(string)Get("status"));
            Assert(Directory.GetFiles(stemFolder,"*.wav").Length==25,"Expected all 25 aligned stems");
            var sum=new float[frames*2]; int silentBars=0;
            for(int id=0;id<25;id++) {
                int rate; var pcm=BacardiStereoMaster.ReadWav(Path.Combine(stemFolder,(id+1).ToString("00")+" "+BacardiBeatMaker.Names[id]+".wav"),out rate);
                Assert(rate==44100 && pcm.Length==frames*2,"Stem length/alignment mismatch");
                silentBars+=VerifySilence(pcm,plan,id,tempo);
                var expected=(float[])Call("RenderSound",id,frames);
                for(int n=0;n<pcm.Length;n++) { Assert(pcm[n]==expected[n],"Stem gain/PCM changed"); sum[n]+=pcm[n]; }
            }
            Pass("25 unity-gain stems span Bar 1 through Bar 56, preserve renderer PCM exactly, and contain exact silence in "+silentBars+" inactive stem-bars");
            ValidateMidi(Path.Combine(stemFolder,"Arranged Song.mid"),plan,cells,tempo,swing);
            string midiFolder=maker.ExportArrangedSong("MIDI",Path.Combine(folder,"midi")); Assert(midiFolder!=null,"MIDI export failed");
            Assert(Directory.GetFiles(midiFolder,"*.wav").Length==0,"MIDI export rendered audio");
            // Enable existing mastering flags deliberately: the new path must remain dry.
            Set("targetLufs",true); Set("bypassMaster",false);
            string stereoFolder=maker.ExportArrangedSong("STEREO",Path.Combine(folder,"stereo")); Assert(stereoFolder!=null,(string)Get("status"));
            string stereo=Path.Combine(stereoFolder,"Arranged Stereo.wav"); int stereoRate;
            var mix=BacardiStereoMaster.ReadWav(stereo,out stereoRate);
            Assert(mix.Length==frames*2 && stereoRate==44100,"Stereo duration mismatch");
            for(int n=0;n<mix.Length;n++) Assert(mix[n]==sum[n],"Stereo differs from unmastered stem sum");
            Pass("stereo WAV equals arranged stem sum exactly with mastering enabled in the old UI; duration "+(frames/44100.0).ToString("F6")+" s (56 bars at "+tempo+" BPM), no normalization/trim/mastering");
            var panel=(BacardiStereoMasterPanel)Get("stereoPanel");
            var session=(BacardiStereoMaster)typeof(BacardiStereoMasterPanel).GetField("session",Flags).GetValue(panel);
            session.Import(stereo); Assert(session.Loaded && !session.Active && session.Rate==44100,"POST-ARRANGEMENT MASTER TEST import failed");
            var copy=session.PlaybackCopy(); Assert(copy.Length==mix.Length,"Master importer changed duration");
            for(int n=0;n<copy.Length;n++) Assert(copy[n]==mix[n],"Master import altered PCM");
            Pass("existing POST-ARRANGEMENT MASTER TEST session imports the float stereo WAV, retains PCM/duration, and starts with Spotify mastering inactive");
            maker.PreviewArrangedSong();
            var preview=(AudioClip)Get("arrangedClip"); var source=(AudioSource)Get("arrangedSource");
            Assert(preview!=null && preview.samples==frames && preview.channels==2 && !source.loop && source.volume==1 && source.pitch==1,"Preview is not the full arrangement");
            var previewPcm=new float[mix.Length]; preview.GetData(previewPcm,0);
            for(int n=0;n<mix.Length;n++) Assert(previewPcm[n]==mix[n],"Preview and export differ");
            Call("Stop"); Pass("arranged preview uses the same full-song stereo PCM as export, plays once at unity gain, and stops through the existing transport");
            maker.ReturnToOneBarGroove(); Assert(!maker.ArrangementApplied && (int)typeof(BacardiBeatMaker).GetProperty("SongBars",Flags).GetValue(maker)==1,"One-bar mode not restored");
            Assert(Get("songPlan")!=null && maker.ExportArrangedSong("MIDI",Path.Combine(folder,"blocked-again"))==null,"One-bar return failed to disable arranged exports");
            maker.ApplyVariationToSong(); Assert(maker.ArrangementLength==56,"Retained plan cannot be reapplied");
            Pass("ONE-BAR GROOVE restores one bar, disables arranged exports, retains the plan and supports reapplying it");
            var after=HashSources(); foreach(var entry in hashes) Assert(after[entry.Key]==entry.Value,"Source WAV changed: "+entry.Key);
            Pass("SHA-256 unchanged for all "+hashes.Count+" project source WAVs");
            report+="Test fixture exports only: "+folder+"\nNo ElevenLabs or paid API calls. Production source WAVs and user arrangement/export files untouched.\n";
        } catch(Exception e) { exit=1; report+="FAIL: "+e+"\n"; }
        finally {
            AudioListener.pause=paused;
            if(hadSave) PlayerPrefs.SetString("BacardiSongVariation",oldSave); else PlayerPrefs.DeleteKey("BacardiSongVariation");
            PlayerPrefs.Save(); SessionState.SetBool(Key,false);
            Directory.CreateDirectory("Tools/ArrangementValidation"); File.WriteAllText("Tools/ArrangementValidation/runtime-results.txt",report); Debug.Log(report); EditorApplication.Exit(exit);
        }
    }
    static int VerifySilence(float[] pcm,BacardiSongVariation.Plan plan,int id,float tempo) {
        int count=0;
        for(int bar=0;bar<plan.bars;bar++) if(plan.masks[bar*25+id]==0) {
            int start=(int)Math.Ceiling(bar*240.0/tempo*44100),end=Math.Min(pcm.Length/2,(int)Math.Ceiling((bar+1)*240.0/tempo*44100));
            for(int n=start*2;n<end*2;n++) Assert(pcm[n]==0,"Inactive stem-bar contains PCM: stem "+id+" bar "+(bar+1)); count++;
        }
        return count;
    }
    static int Big(BinaryReader r,int bytes) { int value=0; while(bytes-->0) value=value*256+r.ReadByte(); return value; }
    static int Variable(BinaryReader r) { int value=0,b; do { b=r.ReadByte(); value=(value<<7)|(b&127); } while((b&128)!=0); return value; }
    static void ValidateMidi(string path,BacardiSongVariation.Plan plan,bool[,] cells,float tempo,float swing) {
        using(var r=new BinaryReader(File.OpenRead(path))) {
            Assert(new string(r.ReadChars(4))=="MThd" && Big(r,4)==6 && Big(r,2)==1 && Big(r,2)==26 && Big(r,2)==960,"MIDI header");
            for(int track=0;track<26;track++) {
                Assert(new string(r.ReadChars(4))=="MTrk","MIDI track missing"); int length=Big(r,4),tick=0,marker=0; long end=r.BaseStream.Position+length;
                var expected=new List<int>(); int id=track-1;
                if(id>=0) for(int step=0;step<plan.bars*16;step++) {
                    bool hit=plan.Allows(id,step) && (id<21 ? cells[id,step%16] : Any(cells,id));
                    if(hit) for(int chord=0;chord<(id==2 || id==4 ? 3 : 1);chord++) expected.Add((int)Math.Round((step+(step%2==1 ? swing : 0))*240));
                }
                int noteOn=0;
                while(r.BaseStream.Position<end) {
                    tick+=Variable(r); int type=r.ReadByte();
                    if(type==255) {
                        int meta=r.ReadByte(),size=Variable(r); var bytes=r.ReadBytes(size);
                        if(meta==6) { Assert(tick==plan.sections[marker++].start*3840,"Section marker off planned bar"); }
                        if(meta==81) Assert((bytes[0]*65536+bytes[1]*256+bytes[2])==(int)Math.Round(60000000.0/tempo),"MIDI tempo changed");
                    } else { r.ReadByte(); int velocity=r.ReadByte(); if((type&240)==144 && velocity>0) Assert(noteOn<expected.Count && tick==expected[noteOn++],"MIDI note differs from swung PCM onset"); }
                }
                Assert(tick==plan.bars*3840 && noteOn==expected.Count,"MIDI duration/note count mismatch");
                if(track==0) Assert(marker==6,"Missing section markers");
            }
        }
        Pass("all 25 MIDI stem tracks and six section markers align with the planned bar boundaries and every swung PCM event; tempo and 56-bar end match");
    }
    static bool Any(bool[,] cells,int id) { for(int s=0;s<16;s++) if(cells[id,s]) return true; return false; }
}
