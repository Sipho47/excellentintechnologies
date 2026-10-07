using System;
using System.IO;
using System.Reflection;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs the actual AudioSources and listener callback, never offline PCM injection.
[InitializeOnLoad]
public static class BacardiRuntimeRoutingTest {
    const string Key="Bacardi.RuntimeRoutingTest";
    static double deadline;
    static int stage;
    static BacardiBeatMaker maker;
    static BacardiMasterLufsMeter meter;
    static BacardiRuntimeRoutingTest() { EditorApplication.update+=Tick; }
    [MenuItem("Bacardi/Validate Runtime Master Routing (16 bars)")]
    public static void Run() {
        if(EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Stop Play Mode before starting routing validation.");
        if(UnityEngine.Object.FindFirstObjectByType<BacardiBeatMaker>()==null)
            throw new Exception("Open the Bacardi scene first.");
        SessionState.SetBool(Key,true); SessionState.SetBool(Key+"cli",false);
        stage=0; deadline=EditorApplication.timeSinceStartup+180;
        EditorApplication.isPlaying=true;
    }
    public static void RunCommandLine() {
        EditorSceneManager.OpenScene("Assets/Scenes/BacardiBeatMaker.unity");
        Run(); SessionState.SetBool(Key+"cli",true);
    }
    static void Invoke(string name) {
        typeof(BacardiBeatMaker).GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(maker,null);
    }
    static void Finish(string result,int code) {
        Directory.CreateDirectory("Tools/LufsValidation");
        File.WriteAllText("Tools/LufsValidation/Unity-Routing-Test.txt",result);
        Debug.Log(result);
        SessionState.SetBool(Key,false);
        bool cli=SessionState.GetBool(Key+"cli",false);
        if(cli) EditorApplication.Exit(code); else EditorApplication.isPlaying=false;
    }
    static double Metric(string key) {
        var match=Regex.Match(meter.LoopReport,@"\b"+key+@"=([-+0-9.]+)");
        if(!match.Success) throw new Exception("Missing capture metric "+key);
        return double.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture);
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)) return;
        if(deadline==0) deadline=EditorApplication.timeSinceStartup+180;
        if(EditorApplication.timeSinceStartup>deadline) { Finish("FAIL: timed out waiting for actual Unity listener capture",1); return; }
        if(!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try {
            if(stage==0) {
                maker=UnityEngine.Object.FindFirstObjectByType<BacardiBeatMaker>();
                meter=UnityEngine.Object.FindFirstObjectByType<BacardiMasterLufsMeter>();
                if(maker==null || meter==null) return;
                Debug.Log("[Routing test] Unity="+Application.unityVersion+" output="+AudioSettings.outputSampleRate+" mode="+AudioSettings.speakerMode+" listener volume="+AudioListener.volume+" listeners="+UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length+" meters="+UnityEngine.Object.FindObjectsByType<BacardiMasterLufsMeter>(FindObjectsSortMode.None).Length);
                Invoke("AnalyzeMaster"); Invoke("Stop"); Invoke("StartPlayback");
                stage=1;
            } else if(meter.LoopReport.StartsWith("16 bars /")) {
                double difference=Metric("DIFFERENCE"), peak=Metric("TP"), reduction=Metric("MAX_GR");
                bool passed=Math.Abs(difference)<=.2 && peak<=-1 &&
                    Math.Abs(peak-meter.AnalyzedLoopTruePeakDb)<=.2 &&
                    Math.Abs(reduction-meter.AnalyzedLoopMaxReductionDb)<=.5 &&
                    meter.RuntimeTelemetry.Contains("Mastering Processor Active: YES | Bypass Mix: 0.000");
                Finish((passed ? "PASS" : "FAIL")+": actual Unity 16-bar routing acceptance (LUFS tolerance 0.2, TP tolerance 0.2 dB, GR tolerance 0.5 dB)\n"+
                    meter.LoopReport+"\n"+meter.RuntimeTelemetry,passed ? 0 : 1);
            }
        } catch(Exception e) { Finish("FAIL: "+e,1); }
    }
}
