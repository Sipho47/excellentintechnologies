using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Offline Play Mode routing tests. REAL_AI fixtures validate identity, not singing quality.
[InitializeOnLoad]
public static class BacardiVocalSourceRuntimeTest {
    const string Key="Bacardi.VocalSourceRuntimeTest";
    static string report="";
    static BacardiVocalSourceRuntimeTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() { SessionState.SetBool(Key,true); EditorApplication.isPlaying=true; }
    sealed class Fixture : IAiVocalProvider {
        public string Name { get { return "Offline routing fixture"; } }
        public AiVocalRequest Received;
        public readonly TaskCompletionSource<AiVocalResult> Completion=new TaskCompletionSource<AiVocalResult>();
        public bool SynchronousFailure;
        public Task<AiVocalResult> GenerateAsync(AiVocalRequest q,IProgress<AiVocalProgress> p,CancellationToken t) {
            Received=q; if(SynchronousFailure) throw new Exception("Offline synchronous failure"); return Completion.Task;
        }
    }
    static void Check(bool value,string text) { if(!value) throw new Exception(text); report+="PASS "+text+"\n"; }
    static AiVocalResult Audio(AiVocalRequest q,bool mock=false) {
        return new AiVocalResult { Request=q,Provider="Offline routing fixture",IsMock=mock,Source=mock?"LOCAL_MOCK":"REAL_AI",Pcm=new[]{.01f,.02f,.03f,.04f} };
    }
    static void Pump(BacardiAiVocalGenerator v) { v.Tick(AudioSettings.dspTime,104,AudioSettings.dspTime,1); }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        SessionState.SetBool(Key,false);
        bool ok=false; GameObject owner=null;
        try {
            owner=new GameObject("Offline vocal routing tests");
            using(var v=new BacardiAiVocalGenerator(owner)) {
                var q=new AiVocalRequest { Lyrics="Come closer\nMove with me\nAll night\nStay with me",Voice="Female",Role="Lead",Key="A",Scale="Minor",Bars=4,Energy=.7f,Emotion=.8f,StrictLyrics=true,SlowerPhraseDensity=true };
                string baseline=JsonUtility.ToJson(q);
                var real=new Fixture(); v.Provider=real;
                Check(v.Result==null&&!v.CanPreview,"provider selection invalidates audio and disables PREVIEW");
                v.Generate(q); Check(v.Result==null&&!v.CanPreview,"REAL AI start invalidates audio and disables PREVIEW");
                var good=Audio(q); real.Completion.SetResult(good); Pump(v);
                Check(v.Result==good&&v.CanPreview,"decoded REAL_AI result is accepted");
                v.Preview(104);
                var preview=(AudioSource)typeof(BacardiAiVocalGenerator).GetField("preview",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(v);
                var pcm=new float[4]; preview.clip.GetData(pcm,0);
                Check(pcm[0]==good.Pcm[0]&&pcm[3]==good.Pcm[3],"PREVIEW AudioClip contains successful REAL_AI PCM");
                Check(JsonUtility.ToJson(real.Received)==baseline,"exact lyrics and all requested settings preserved");
                real=new Fixture(); v.Provider=real; v.Generate(q); v.Cancel();
                Check(v.Result==null&&!v.CanPreview,"cancellation immediately invalidates current audio");
                real.Completion.SetResult(Audio(q)); Pump(v);
                Check(v.Result==null&&!v.CanPreview,"late success after cancellation cannot revive audio");
                foreach(string mode in new[]{"fault","sync","mock","empty","nan"}) {
                    real=new Fixture { SynchronousFailure=mode=="sync" }; v.Provider=real; v.Generate(q);
                    if(mode=="fault") real.Completion.SetException(new Exception("Offline REAL AI failure"));
                    else if(mode!="sync") {
                        var bad=Audio(q,mode=="mock"); if(mode=="empty") bad.Pcm=new float[0]; if(mode=="nan") bad.Pcm[0]=float.NaN;
                        real.Completion.SetResult(bad);
                    }
                    Pump(v); Check(v.Result==null&&!v.CanPreview&&v.State=="ERROR",mode+" failure cannot enable PREVIEW or fall back to mock");
                }
                real=new Fixture(); v.Provider=real; v.Generate(q); v.Provider=new MockAiVocalProvider();
                real.Completion.SetResult(Audio(q)); Pump(v);
                Check(v.Result==null&&!v.CanPreview,"provider switch rejects in-flight old-provider result");
                v.Generate(q);
                var limit=DateTime.UtcNow.AddSeconds(20);
                while(v.Busy&&DateTime.UtcNow<limit) { Thread.Sleep(10); Pump(v); }
                Check(v.CanPreview&&v.Result.Source=="LOCAL_MOCK"&&v.Result.IsMock,"explicit LOCAL MOCK selection still generates previewable audio");
                v.Preview(104); v.Provider=new Fixture();
                Check(v.Result==null&&!v.CanPreview&&preview.clip==null&&!preview.isPlaying,"switching from mock to real detaches old mock preview clip");
                Check(JsonUtility.ToJson(q)==baseline,"routing tests do not mutate request settings");
            }
            ok=true;
        } catch(Exception e) { report+="FAIL "+e+"\n"; }
        finally {
            if(owner!=null) UnityEngine.Object.DestroyImmediate(owner);
            Directory.CreateDirectory("Tools/VocalValidation");
            File.WriteAllText("Tools/VocalValidation/Source-Routing-Runtime.txt",(ok?"PASS\n":"FAIL\n")+report+"Offline fixtures only. No singer API calls. Intelligibility was not tested.\n");
            EditorApplication.Exit(ok?0:1);
        }
    }
}
