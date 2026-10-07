using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Real Draw/repaint and keyboard events. CaptureProvider never opens a socket.
public sealed class BacardiPronunciationTextTest : EditorWindow {
    sealed class CaptureProvider : IAiVocalProvider {
        public string Name { get { return "OFFLINE request capture"; } }
        public AiVocalRequest Request;
        public int Calls;
        public Task<AiVocalResult> GenerateAsync(AiVocalRequest request, IProgress<AiVocalProgress> progress, CancellationToken token) {
            Request = request; Calls++;
            return Task.FromResult(new AiVocalResult { Request=request, Provider=Name, Pcm=new float[882], IsMock=true });
        }
    }
    const string Original = "  Sondela kimi\r\n\u00e9 e\u0301 \u65e5\u672c \ud83c\udfb5\n\n  ";
    BacardiAiVocalGenerator vocals;
    CaptureProvider capture;
    GameObject owner;
    int stage, repaint;
    bool copy, focus;
    Vector2 scroll;
    double deadline;
    public static void RunCommandLine() {
        var window=CreateInstance<BacardiPronunciationTextTest>();
        window.owner=new GameObject("Offline pronunciation text test");
        window.vocals=new BacardiAiVocalGenerator(window.owner);
        typeof(BacardiAiVocalGenerator).GetField("lyrics",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window.vocals,Original);
        window.capture=new CaptureProvider(); window.vocals.Provider=window.capture;
        window.position=new Rect(0,0,1280,1000); window.Show(); window.Focus();
        window.deadline=EditorApplication.timeSinceStartup+60;
        EditorApplication.update+=window.Tick;
    }
    void OnGUI() {
        scroll=GUILayout.BeginScrollView(scroll);
        vocals.Draw(104,"Offline only");
        GUILayout.EndScrollView();
        if(Event.current.type==EventType.Repaint) {
            if(focus) { GUI.FocusControl("Bacardi.AiPronunciationText"); focus=false; }
            if(copy) { vocals.CopyOriginalLyricsToPronunciationText(); copy=false; }
        }
    }
    static void Check(bool ok,string reason) { if(!ok) throw new Exception(reason); }
    void Capture(bool regenerate,string expected) {
        vocals.Generate(vocals.CurrentRequest(104),regenerate);
        vocals.Tick(0,104,0,1);
        Check(capture.Request.Lyrics==Original && capture.Request.AiPronunciationText==expected,"Generate/Regenerate altered original or lost override");
        Check(vocals.Lyrics==Original,"UI original altered");
        var json=JsonUtility.FromJson<AiVocalRequest>(JsonUtility.ToJson(capture.Request));
        Check(json.Lyrics==Original && json.AiPronunciationText==expected,"Unity Unicode JSON fields not preserved");
        Check(AiVocalWav.Info(vocals.Result).Contains("Lyrics:\n"+Original+"\nBPM:"),"Original lyric export altered");
    }
    void Tick() {
        try {
            Check(EditorApplication.timeSinceStartup<deadline,"Timed out");
            if(vocals.GuidanceRepaintCount<=repaint) { Repaint(); return; }
            repaint=vocals.GuidanceRepaintCount;
            if(stage==0) {
                Check(vocals.AiPronunciationText=="" && vocals.GenerationLyricsSource=="ORIGINAL LYRICS","Optional default/source");
                Capture(false,""); copy=true;
            } else if(stage==1) {
                if(copy || vocals.DisplayedAiPronunciationText=="") { Repaint(); return; }
                Check(vocals.DisplayedAiPronunciationText==Original && vocals.Lyrics==Original,"Copy must visibly preserve exact Unicode original");
                Check(vocals.GenerationLyricsSource=="PRONUNCIATION TEXT","REAL source indicator");
                focus=true;
            } else if(stage==2) {
                if(focus) { Repaint(); return; }
                SendEvent(new Event { type=EventType.KeyDown,keyCode=KeyCode.A,modifiers=EventModifiers.Control });
                SendEvent(new Event { type=EventType.KeyDown,keyCode=KeyCode.X,character='x' });
            } else if(stage==3) {
                Check(vocals.AiPronunciationText=="x" && vocals.DisplayedAiPronunciationText=="x","Optional field keyboard editing/rendering");
                Capture(true,"x");
                vocals.AddToSong(13);
                Check(vocals.Placements[0].Vocal.Request.Lyrics==Original && vocals.Placements[0].Vocal.Request.AiPronunciationText=="x","Arrangement must preserve both representations");
                vocals.AiPronunciationText=" \r\n\t";
                Check(vocals.GenerationLyricsSource=="ORIGINAL LYRICS","Whitespace fallback indicator");
                vocals.Provider=new MockAiVocalProvider(); vocals.AiPronunciationText="manual words";
                Check(vocals.GenerationLyricsSource=="ORIGINAL LYRICS","MOCK ignores REAL-only override");
                Check(capture.Calls==2,"Exactly two offline capture calls");
                Finish(true,"PASS original Unicode preserved in UI, Unity JSON, generation/regeneration requests, arrangement and lyric export; optional default/fallback; copy handler populates visible editable field; actual keyboard editing; REAL/MOCK source indicators. No ElevenLabs or network requests."); return;
            }
            stage++; Repaint();
        } catch(Exception error) { Finish(false,error.ToString()); }
    }
    void Finish(bool success,string message) {
        EditorApplication.update-=Tick;
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Pronunciation-Text-UI.txt",(success?"PASS\n":"FAIL\n")+message);
        vocals.Dispose(); DestroyImmediate(owner); Close(); EditorApplication.Exit(success?0:1);
    }
}
