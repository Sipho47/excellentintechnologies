using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Calls the production runtime IMGUI builder and injects actual mouse events.
// Never calls Generate, TestRealConnection, or any provider/network method.
public sealed class BacardiStrictLyricsUiTest : EditorWindow {
    static readonly Vector2[] Sizes = { new Vector2(1920,1080), new Vector2(960,540), new Vector2(640,480) };
    BacardiAiVocalGenerator vocals;
    GameObject owner;
    Vector2 scroll, origin;
    Rect viewport;
    int scenario, stage, repaint, observed;
    double deadline;
    string baseline, report = "";

    [MenuItem("Bacardi/Validate Strict Lyrics Runtime UI (offline)")]
    public static void Run() { Start(false); }
    public static void RunCommandLine() { Start(true); }
    bool commandLine;
    static void Start(bool cli) {
        var window = CreateInstance<BacardiStrictLyricsUiTest>();
        window.commandLine = cli;
        window.titleContent = new GUIContent("Strict lyrics runtime UI test - OFFLINE");
        window.owner = new GameObject("Offline strict lyrics UI fixture");
        window.vocals = new BacardiAiVocalGenerator(window.owner);
        window.vocals.Provider = new ElevenLabsAiVocalProvider();
        typeof(BacardiAiVocalGenerator).GetField("providerChoice",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(window.vocals,1);
        window.vocals.PronunciationGuidance = "Native isiZulu";
        window.vocals.AiPronunciationText = "  Sondela kimi\r\nHamba nami e\u0301 \ud83c\udfb5  ";
        window.baseline = JsonUtility.ToJson(window.vocals.CurrentRequest(104));
        window.deadline = EditorApplication.timeSinceStartup + 90;
        window.Show(); window.Configure();
        EditorApplication.update += window.Tick;
    }
    void Configure() {
        minSize = Vector2.zero; maxSize = new Vector2(3000,2000);
        var size = Sizes[scenario]; position = new Rect(0,0,size.x,size.y);
        scroll = Vector2.zero; stage = 0; observed = repaint;
        vocals.StrictLyrics = vocals.SlowerPhraseDensity = false;
        Focus(); Repaint();
    }
    void OnGUI() {
        if (vocals == null) return;
        origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
        viewport = new Rect(origin,position.size);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Space(900); // Existing beat/pad UI above the generator.
        vocals.Draw(104,"OFFLINE UI TEST - NO GENERATION");
        GUILayout.EndScrollView();
        if (Event.current.type == EventType.Repaint) repaint++;
    }
    static void Check(bool ok,string reason) { if (!ok) throw new Exception(reason); }
    void Visible(Rect rect,string name,int height) {
        Check(rect.width > 100 && rect.height >= height,name + " missing or too small: " + rect);
        Check(rect.xMin >= viewport.xMin && rect.xMax <= viewport.xMax && rect.yMin >= viewport.yMin && rect.yMax <= viewport.yMax,name + " clipped: " + rect + " viewport " + viewport);
    }
    void Click(Rect rect) {
        var point = rect.center - origin;
        SendEvent(new Event { type=EventType.MouseDown,button=0,mousePosition=point });
        SendEvent(new Event { type=EventType.MouseUp,button=0,mousePosition=point });
    }
    void Verify(bool strict,bool slower) {
        var request = vocals.CurrentRequest(104);
        Check(request.StrictLyrics == strict && request.SlowerPhraseDensity == slower,"Clicked controls must map to CurrentRequest");
        var transmitted = JsonUtility.FromJson<AiVocalRequest>(JsonUtility.ToJson(request));
        Check(transmitted.StrictLyrics == strict && transmitted.SlowerPhraseDensity == slower,"Proxy JSON flags must match controls");
        request.StrictLyrics = request.SlowerPhraseDensity = false;
        Check(JsonUtility.ToJson(request) == baseline,"Clicking toggles altered lyrics, pronunciation text, BPM or another vocal setting");
        Check(vocals.WordPreservationStatus == "STRICT LYRICS: " + (strict?"ON":"OFF") + "\nSLOWER PHRASE DENSITY: " + (slower?"ON":"OFF"),"Exact visible status text");
    }
    void Tick() {
        try {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out waiting for runtime Draw/repaint");
            if (repaint <= observed) { Repaint(); return; }
            observed = repaint;
            if (stage == 0) {
                scroll.y += vocals.StrictLyricsRect.yMin - viewport.yMin - 24;
                stage++;
            } else if (stage == 1) {
                Visible(vocals.StrictLyricsRect,"STRICT LYRICS control",36);
                Visible(vocals.SlowerPhraseDensityRect,"SLOWER PHRASE DENSITY control",36);
                Visible(vocals.WordPreservationStatusRect,"Both status lines",42);
                Check(vocals.GuidancePresetRect.yMax < vocals.StrictLyricsRect.yMin && vocals.StrictLyricsRect.yMax <= vocals.SlowerPhraseDensityRect.yMin && vocals.SlowerPhraseDensityRect.yMax <= vocals.WordPreservationStatusRect.yMin && vocals.WordPreservationStatusRect.yMax < vocals.GenerateRect.yMin,"Controls must follow pronunciation section and precede generation without overlap");
                Verify(false,false); Click(vocals.StrictLyricsRect); Verify(true,false); stage++;
            } else if (stage == 2) {
                Click(vocals.SlowerPhraseDensityRect); Verify(true,true); stage++;
            } else if (stage == 3) {
                Visible(vocals.WordPreservationStatusRect,"Enabled status",42);
                Verify(true,true); Click(vocals.StrictLyricsRect); Verify(false,true); stage++;
            } else {
                Click(vocals.SlowerPhraseDensityRect); Verify(false,false);
                report += "PASS REAL AI production Draw " + Sizes[scenario] + ": both controls created, visible in scroll viewport, 36px clickable rows; both ON statuses; all four request/UTF-8 JSON flag combinations; every existing request setting unchanged.\n";
                scenario++;
                if (scenario == Sizes.Length) { Finish(true); return; }
                Configure();
            }
            Repaint();
        } catch(Exception error) { report += error + "\n"; Finish(false); }
    }
    void Finish(bool ok) {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Strict-Lyrics-UI.txt",(ok?"PASS\n":"FAIL\n") + report + "No provider calls or ElevenLabs generation.\n");
        Debug.Log(report);
        vocals.Dispose(); DestroyImmediate(owner); Close();
        if (commandLine) EditorApplication.Exit(ok?0:1);
    }
}
