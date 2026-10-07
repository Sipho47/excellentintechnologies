using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Exercises the production IMGUI builder, real layout/repaint, and real input events.
// A capturing cancellation fixture guarantees generation buttons cannot call a service.
public sealed class BacardiPronunciationUiTest : EditorWindow {
    sealed class CaptureProvider : IAiVocalProvider {
        public string Name { get { return "OFFLINE UI FIXTURE"; } }
        public AiVocalRequest Request; public int Calls;
        public Task<AiVocalResult> GenerateAsync(AiVocalRequest request, IProgress<AiVocalProgress> progress, CancellationToken token) {
            Request = request; Calls++;
            return Task.FromCanceled<AiVocalResult>(new CancellationToken(true));
        }
    }
    BacardiAiVocalGenerator vocals;
    GameObject owner;
    Vector2 scroll, origin;
    Rect viewport;
    int stage, scenario, lastRepaint;
    double deadline;
    bool real;
    CaptureProvider capture;
    string original, report = "";
    static readonly Vector2[] Sizes = { new Vector2(1920,1080), new Vector2(960,540), new Vector2(640,480) };
    public static void RunCommandLine() {
        var window = CreateInstance<BacardiPronunciationUiTest>();
        window.titleContent = new GUIContent("Bacardi pronunciation runtime UI test");
        window.owner = new GameObject("Offline pronunciation UI test");
        window.vocals = new BacardiAiVocalGenerator(window.owner);
        window.original = window.vocals.Lyrics;
        window.deadline = EditorApplication.timeSinceStartup + 90;
        window.Show(); window.Configure();
        EditorApplication.update += window.Tick;
    }
    void Configure() {
        real = scenario % 2 == 1;
        vocals.Provider = real ? (IAiVocalProvider)new ElevenLabsAiVocalProvider() : new MockAiVocalProvider();
        typeof(BacardiAiVocalGenerator).GetField("providerChoice",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(vocals,real ? 1 : 0);
        var size = Sizes[scenario / 2];
        minSize = Vector2.zero; maxSize = new Vector2(3000,2000);
        position = new Rect(0,0,size.x,size.y);
        scroll = Vector2.zero; stage = 0; lastRepaint = vocals.GuidanceRepaintCount;
        Repaint();
    }
    void OnGUI() {
        if (vocals == null) return;
        origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
        viewport = new Rect(origin,position.size);
        scroll = GUILayout.BeginScrollView(scroll);
        // Match the existing parent panel's scrollable content above the generator.
        GUILayout.Space(900);
        vocals.Draw(104,"OFFLINE UI TEST");
        GUILayout.EndScrollView();
    }
    static void Assert(bool value,string message) { if (!value) throw new Exception(message); }
    void Visible(Rect rect,string name) {
        Assert(rect.width > 100 && rect.height >= 20,name + " was not created at usable size");
        Assert(rect.xMin >= viewport.xMin && rect.xMax <= viewport.xMax && rect.yMin >= viewport.yMin && rect.yMax <= viewport.yMax,name + " is clipped/outside viewport: " + rect + " / " + viewport);
    }
    void Click(Rect rect) {
        var point = rect.center - origin;
        SendEvent(new Event { type=EventType.MouseDown,button=0,mousePosition=point });
        SendEvent(new Event { type=EventType.MouseUp,button=0,mousePosition=point });
    }
    void Tick() {
        try {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out waiting for actual IMGUI layout/repaint");
            if (vocals.GuidanceRepaintCount <= lastRepaint) { Repaint(); return; }
            lastRepaint = vocals.GuidanceRepaintCount;
            if (stage == 0) {
                scroll.y += vocals.LyricsRect.yMin - viewport.yMin - 12;
                stage = 1;
            } else if (stage == 1) {
                Visible(vocals.GuidanceLabelRect,"Guidance label"); Visible(vocals.GuidanceFieldRect,"Editable multiline guidance field"); Visible(vocals.GuidancePresetRect,"isiZulu preset button"); Visible(vocals.GenerateRect,"Generate button");
                Assert(vocals.GuidanceFieldRect.height >= 72,"Multiline field height");
                Assert(vocals.LyricsRect.yMax <= vocals.GuidanceLabelRect.yMin && vocals.GuidanceLabelRect.yMax <= vocals.GuidanceFieldRect.yMin && vocals.GuidanceFieldRect.yMax <= vocals.GuidancePresetRect.yMin && vocals.GuidancePresetRect.yMax <= vocals.GenerateRect.yMin,"Controls ordered below lyrics and above generation without overlap");
                Click(vocals.GuidanceFieldRect);
                SendEvent(new Event { type=EventType.KeyDown,keyCode=KeyCode.A,modifiers=EventModifiers.Control });
                SendEvent(new Event { type=EventType.KeyDown,keyCode=KeyCode.X,character='x' });
                Assert(vocals.PronunciationGuidance=="x","Guidance field is actually editable");
                Click(vocals.GuidancePresetRect);
                Assert(vocals.PronunciationGuidance==BacardiAiVocalGenerator.IsiZuluPronunciation,"Preset button input populates exact guidance");
                Assert(vocals.Lyrics==original,"Preset must never alter lyrics");
                capture = new CaptureProvider(); vocals.Provider = capture;
                stage = 2;
            } else if (stage == 2) {
                Click(vocals.GenerateRect);
                Assert(capture.Calls==1 && capture.Request.PronunciationGuidance==BacardiAiVocalGenerator.IsiZuluPronunciation && capture.Request.Lyrics==original,"Generate reads separate guidance and unchanged lyrics");
                vocals.Tick(0,104,0,1); stage = 3;
            } else if (stage == 3) {
                Click(vocals.RegenerateRect);
                Assert(capture.Calls==2 && capture.Request.PronunciationGuidance==vocals.PronunciationGuidance && capture.Request.Lyrics==original,"Regenerate reads field without changing lyrics");
                vocals.Tick(0,104,0,1);
                report += "PASS production runtime Draw: " + Sizes[scenario/2] + " " + (real ? "REAL AI" : "MOCK") + "; visible label/72px editable field/30px preset; no clipping/overlap; actual field and preset input; Generate/Regenerate capture separate guidance and exact lyrics.\n";
                scenario++;
                if (scenario==Sizes.Length*2) { Finish(true); return; }
                Configure();
            }
            Repaint();
        } catch(Exception error) { report += error + "\n"; Finish(false); }
    }
    void Finish(bool success) {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Pronunciation-UI.txt",(success ? "PASS\n" : "FAIL\n")+report+"No network generation; cancellation fixture only.\n");
        vocals.Dispose(); DestroyImmediate(owner); Close();
        EditorApplication.Exit(success ? 0 : 1);
    }
}
