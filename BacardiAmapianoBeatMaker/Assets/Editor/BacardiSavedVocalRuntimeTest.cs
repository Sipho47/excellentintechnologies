using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BacardiSavedVocalRuntimeTest {
    const string Key="Bacardi.SavedVocalRuntimeTest";
    const string ProviderName="ElevenLabs Music v2.5 / two-stem vocal separation";
    const string Part8="C:/Users/Huawei/AppData/LocalLow/DefaultCompany/SiphoTechMusicVisualizer/Exports/Bacardi-20261007-200059-703-94c9f4/AI Vocals/AI_VOCAL_LEAD_104BPM_AMINOR.wav";
    static string report="",root;
    static BacardiSavedVocalRuntimeTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() { SessionState.SetBool(Key,true); EditorApplication.isPlaying=true; }
    sealed class Spy : IAiVocalProvider {
        public int Calls;
        public string Name { get { return "Offline provider spy"; } }
        public Task<AiVocalResult> GenerateAsync(AiVocalRequest q,IProgress<AiVocalProgress> p,CancellationToken t) { Calls++; throw new Exception("Reload must never invoke generation"); }
    }
    static void Check(bool ok,string text) { if(!ok) throw new Exception(text); }
    static void Pass(string text) { report+="PASS "+text+"\n"; }
    static void Wait(BacardiAiVocalGenerator v) {
        double until=EditorApplication.timeSinceStartup+30;
        while(v.Busy&&EditorApplication.timeSinceStartup<until) { Thread.Sleep(10); v.Tick(AudioSettings.dspTime,104,AudioSettings.dspTime,1); }
        Check(!v.Busy,"Offline vocal operation timed out");
    }
    static string Hash(string file) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))); }
    static AiVocalResult Fixture(bool mock=false) {
        var q=new AiVocalRequest { Lyrics="Come closer\nMove with me\nAll night\nStay with me",Bpm=104,Voice="Female",Role="Lead",Key="A",Scale="Minor",Bars=8,Energy=.45f,Emotion=.6f,StrictLyrics=true,SlowerPhraseDensity=true,PronunciationGuidance="Natural clear vowels",AiPronunciationText="" };
        var pcm=new float[(int)Math.Round(q.Seconds*44100)*2];
        for(int n=0;n<pcm.Length;n+=2) { pcm[n]=(float)(.03*Math.Sin(n*.01)); pcm[n+1]=(float)(.02*Math.Cos(n*.02)); }
        return new AiVocalResult { Request=q,Pcm=pcm,Provider=mock?new MockAiVocalProvider().Name:ProviderName,IsMock=mock,Source=mock?"LOCAL_MOCK":"REAL_AI" };
    }
    static string Write(string name,AiVocalResult result,bool sidecar=false) {
        string folder=Path.Combine(root,name); Directory.CreateDirectory(folder);
        string wav=Path.Combine(folder,"AI_VOCAL_LEAD_104BPM_AMINOR.wav");
        AiVocalWav.Write(wav,result.Pcm); File.WriteAllText(Path.Combine(folder,"VOCAL_INFO.txt"),AiVocalWav.Info(result));
        if(sidecar) BacardiSavedVocalLoader.WriteMetadata(wav,result);
        return wav;
    }
    static void Reject(string path,double bpm,string text) {
        bool rejected=false; try { BacardiSavedVocalLoader.Load(path,bpm); } catch(Exception) { rejected=true; }
        Check(rejected,"Accepted invalid input: "+text); Pass(text+" rejected");
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        SessionState.SetBool(Key,false); bool ok=false; GameObject owner=null;
        var trap=new TcpListener(IPAddress.Loopback,0); string oldEndpoint=Environment.GetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL");
        try {
            trap.Start(); Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL","http://127.0.0.1:"+((IPEndPoint)trap.LocalEndpoint).Port+"/");
            root=Path.GetFullPath("Tools/SavedVocalValidation/fixture-"+Guid.NewGuid().ToString("N"));
            var real=Fixture(); string legacy=Write("legacy",real),sidecar=Write("sidecar",real,true);
            var decoded=ElevenLabsAiVocalProvider.DecodeWav(File.ReadAllBytes(legacy));
            string before=Hash(legacy);
            owner=new GameObject("Offline saved vocal reload");
            using(var v=new BacardiAiVocalGenerator(owner)) {
                var spy=new Spy(); v.Provider=spy;
                v.LoadSavedVocal(legacy,104); Check(v.Result==null&&!v.CanPreview,"PREVIEW enabled during loading"); Wait(v);
                Check(v.Result!=null&&v.CanPreview,"Legacy reload failed: "+v.Message);
                Check(v.Result.Source=="REAL_AI"&&!v.Result.IsMock&&v.Result.Provider==ProviderName,"REAL source not restored");
                Check(JsonUtility.ToJson(v.CurrentRequest(104))==JsonUtility.ToJson(real.Request),"Vocal settings not restored exactly");
                Check(v.Result.SampleRate==44100&&v.Result.Pcm.Length==decoded.Length,"Audio dimensions changed");
                Pass("legacy REAL_AI reload restores Female/Lead/A Minor/8 bars, 104 BPM, energy/emotion, strict/density options, lyrics and pronunciation guidance");
                v.Preview(104); var preview=(AudioSource)typeof(BacardiAiVocalGenerator).GetField("preview",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(v);
                var played=new float[decoded.Length]; preview.clip.GetData(played,0);
                for(int n=0;n<played.Length;n++) Check(played[n]==decoded[n],"Preview PCM changed");
                Pass("PREVIEW enabled and AudioClip matches decoded saved stereo PCM sample-for-sample");
                v.AddToSong(13); Check(v.Placements.Count==1&&v.Placements[0].Vocal==v.Result&&v.Placements[0].StartBar==13,"Add-to-song failed");
                Pass("ADD TO SONG enabled; reloaded REAL_AI result attached at Bar 13 without changing song length");
                v.Save(true,104,13,46); Wait(v); Check(v.State!="ERROR","Stem export failed: "+v.Message);
                string stem=v.Message.Substring("Saved: ".Length); var aligned=ElevenLabsAiVocalProvider.DecodeWav(File.ReadAllBytes(stem));
                int offset=checked((int)BacardiVocalTimeline.FrameAtBar(13,104)*2);
                Check(aligned.Length==BacardiVocalTimeline.FrameAtBar(47,104)*2,"Aligned stem song length changed");
                for(int n=0;n<aligned.Length;n++) {
                    float expected=n>=offset&&n<offset+decoded.Length?decoded[n-offset]:0;
                    Check(Math.Abs(aligned[n]-expected)<1f/8388608f*2,"Aligned stem PCM/timing mismatch");
                }
                Pass("existing EXPORT VOCAL STEM accepts reload; 46-bar stem has correctly aligned audio and silence outside placement");
                v.Save(false,104,13,46); Wait(v); Check(v.State!="ERROR","Isolated save failed");
                string saved=v.Message.Substring("Saved: ".Length); Check(File.Exists(BacardiSavedVocalLoader.MetadataPath(saved)),"New save lacks sidecar");
                var roundtrip=BacardiSavedVocalLoader.Load(saved,104); Check(roundtrip.Source=="REAL_AI","Sidecar source changed");
                Pass("future SAVE VOCAL WAV writes versioned per-WAV metadata/hash; saved vocal reloads successfully");
                v.LoadSavedVocal(sidecar,104); Wait(v); Check(v.CanPreview&&v.Result.Source=="REAL_AI","Sidecar runtime reload failed");
                Pass("JSON sidecar reload preserves REAL_AI source and enables preview");
                string mock=Write("mock",Fixture(true),true); v.LoadSavedVocal(mock,104); Wait(v);
                Check(v.CanPreview&&v.Result.Source=="LOCAL_MOCK"&&v.Result.IsMock&&v.Provider is MockAiVocalProvider,"Mock mislabeled as REAL AI");
                Pass("saved LOCAL_MOCK reload retains mock identity; filename cannot promote it to REAL_AI");
                string bad=Write("malformed",real); File.WriteAllBytes(bad,new byte[]{1,2,3});
                v.LoadSavedVocal(bad,104); Wait(v); Check(v.Result==null&&!v.CanPreview&&v.State=="ERROR","Failed reload retained stale audio");
                Pass("failed reload clears stale result, disables actions, and displays a clear error");
                Check(spy.Calls==0,"Reload invoked provider");
            }
            Check(Hash(legacy)==before,"Loader rewrote saved source WAV"); Pass("source WAV unchanged by loading");
            Reject(sidecar,120,"project-tempo mismatch");
            foreach(string format in new[]{"sample-rate","channels","bit-depth","truncated"}) {
                string wav=Write(format,real); var bytes=File.ReadAllBytes(wav);
                if(format=="sample-rate") Buffer.BlockCopy(BitConverter.GetBytes(48000),0,bytes,24,4);
                else if(format=="channels") bytes[22]=1;
                else if(format=="bit-depth") bytes[34]=16;
                else Array.Resize(ref bytes,bytes.Length-3);
                File.WriteAllBytes(wav,bytes); Reject(wav,104,format);
            }
            string missing=Write("missing-metadata",real); File.Move(Path.Combine(Path.GetDirectoryName(missing),"VOCAL_INFO.txt"),Path.Combine(Path.GetDirectoryName(missing),"unused.txt")); Reject(missing,104,"missing provenance");
            var unknown=Fixture(); unknown.Provider="Unverified local audio"; Reject(Write("unknown-provider",unknown),104,"unknown provider provenance");
            string ambiguous=Write("ambiguous",real); File.Copy(ambiguous,Path.Combine(Path.GetDirectoryName(ambiguous),"other-take.wav")); Reject(ambiguous,104,"ambiguous legacy multi-take metadata");
            var wrongDuration=Fixture(); wrongDuration.Request.Bars=4; Reject(Write("duration",wrongDuration),104,"bar-length/duration mismatch");
            string alignedInfo=Write("aligned-stem",real); File.WriteAllText(Path.Combine(Path.GetDirectoryName(alignedInfo),"VOCAL_INFO.txt"),"ALIGNED VOCAL STEM\n"+AiVocalWav.Info(real)); Reject(alignedInfo,104,"aligned stem as individual take");
            string tampered=Write("hash",real,true); var changed=File.ReadAllBytes(tampered); changed[44]^=1; File.WriteAllBytes(tampered,changed); Reject(tampered,104,"sidecar hash mismatch (no legacy fallback)");
            string malformedJson=Write("bad-json",real,true); File.WriteAllText(BacardiSavedVocalLoader.MetadataPath(malformedJson),"{broken"); Reject(malformedJson,104,"malformed JSON sidecar");
            if(File.Exists(Part8)) {
                string info=Path.Combine(Path.GetDirectoryName(Part8),"VOCAL_INFO.txt"),wavHash=Hash(Part8),infoHash=Hash(info);
                var actual=BacardiSavedVocalLoader.Load(Part8,104);
                Check(actual.Source=="REAL_AI"&&actual.Provider==ProviderName&&!actual.IsMock&&actual.Request.Voice=="Female"&&actual.Request.Role=="Lead"&&actual.Request.Key=="A"&&actual.Request.Scale=="Minor"&&actual.Request.Bars==8,"PART 8 metadata mismatch");
                using(var v=new BacardiAiVocalGenerator(owner)) { v.LoadSavedVocal(Part8,104); Wait(v); Check(v.CanPreview&&v.Result!=null,"PART 8 runtime reload failed: "+v.Message); }
                Check(Hash(Part8)==wavHash&&Hash(info)==infoHash,"PART 8 files modified");
                Pass("existing PART 8 WAV + adjacent VOCAL_INFO.txt validated read-only: REAL_AI, ElevenLabs, Female/Lead/A Minor/8 bars/104 BPM, 44100 Hz stereo; runtime PREVIEW available; files unchanged; no PART 8 export or mastering");
            } else report+="SKIP PART 8 saved WAV not found at the known export location.\n";
            Check(!trap.Pending(),"A reload attempted a proxy/network call");
            Pass("zero provider generation calls and zero loopback proxy connections during reload suite"); ok=true;
        } catch(Exception e) { report+="FAIL "+e+"\n"; }
        finally {
            Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL",oldEndpoint); trap.Stop();
            if(owner!=null) UnityEngine.Object.DestroyImmediate(owner);
            Directory.CreateDirectory("Tools/SavedVocalValidation"); File.WriteAllText("Tools/SavedVocalValidation/runtime-results.txt",(ok?"PASS\n":"FAIL\n")+report+"Offline fixtures. No ElevenLabs/API calls. PART 8 was not exported or mastered.\n");
            EditorApplication.Exit(ok?0:1);
        }
    }
}
