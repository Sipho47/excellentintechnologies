using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Uses production Draw and actual mouse events; never invokes generation.
public sealed class BacardiWordOptionsRuntimeUiTest : EditorWindow {
    BacardiAiVocalGenerator vocals;
    GameObject owner;
    Vector2 scroll, origin;
    Rect viewport;
    int stage, scenario, repaint;
    double deadline;
    string baseline, report = "";
    static readonly Vector2[] Sizes = { new Vector2(1920,1080), new Vector2(960,540), new Vector2(640,480) };
    public static void RunCommandLine() {
        var w = CreateInstance<BacardiWordOptionsRuntimeUiTest>();
        w.owner = new GameObject("Offline word options UI test");
        w.vocals = new BacardiAiVocalGenerator(w.owner);
        typeof(BacardiAiVocalGenerator).GetField("providerChoice", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(w.vocals, 1);
        w.vocals.Provider = new ElevenLabsAiVocalProvider();
        w.baseline = JsonUtility.ToJson(w.vocals.CurrentRequest(104));
        w.deadline = EditorApplication.timeSinceStartup + 90;
        w.Show(); w.Configure(); w.Focus(); EditorApplication.update += w.Tick;
    }
    void Configure() {
        minSize = Vector2.zero; maxSize = new Vector2(3000,2000);
        position = new Rect(Vector2.zero, Sizes[scenario]);
        scroll = Vector2.zero; stage = 0; Repaint();
    }
    void OnGUI() {
        if (vocals == null) return;
        origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
        viewport = new Rect(origin, position.size);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Space(900); // Parent panel's content above the generator.
        vocals.Draw(104,"OFFLINE UI TEST", position.width - 32);
        GUILayout.EndScrollView();
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    void Visible(Rect r, string name) {
        Check(r.width > 100 && r.height >= 30, name + " has no usable layout");
        Check(r.xMin >= viewport.xMin && r.xMax <= viewport.xMax && r.yMin >= viewport.yMin && r.yMax <= viewport.yMax, name + " clipped: " + r + " viewport " + viewport);
    }
    void Click(Rect r) {
        // SendEvent uses host-view coordinates including the editor tab header.
        var p = r.center - position.position;
        SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = p });
        SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = p });
    }
    void Request(bool strict, bool slower) {
        var q = vocals.CurrentRequest(104);
        Check(q.StrictLyrics == strict && q.SlowerPhraseDensity == slower,"Mouse input not mapped into CurrentRequest: expected " + strict + "/" + slower + ", got " + q.StrictLyrics + "/" + q.SlowerPhraseDensity);
        var wire = JsonUtility.FromJson<AiVocalRequest>(JsonUtility.ToJson(q));
        Check(wire.StrictLyrics == strict && wire.SlowerPhraseDensity == slower,"Proxy JSON flags lost");
        q.StrictLyrics = false; q.SlowerPhraseDensity = false;
        Check(JsonUtility.ToJson(q) == baseline,"Existing lyrics, pronunciation, BPM or vocal settings changed");
    }
    void Tick() {
        try {
            Check(EditorApplication.timeSinceStartup < deadline,"UI test timed out");
            if (vocals.GuidanceRepaintCount <= repaint) { Repaint(); return; }
            repaint = vocals.GuidanceRepaintCount;
            if (stage == 0) {
                scroll.y += vocals.StrictLyricsRect.yMin - viewport.yMin - 20;
            } else if (stage == 1) {
                Visible(vocals.StrictLyricsRect,"STRICT LYRICS");
                Visible(vocals.SlowerPhraseDensityRect,"SLOWER PHRASE DENSITY");
                Visible(vocals.WordPreservationStatusRect,"Enabled status");
                Check(vocals.GuidancePresetRect.yMax < vocals.StrictLyricsRect.yMin && vocals.StrictLyricsRect.yMax <= vocals.SlowerPhraseDensityRect.yMin && vocals.WordPreservationStatusRect.yMax < vocals.GenerateRect.yMin,"Incorrect control order/overlap");
                Request(false,false); Click(vocals.StrictLyricsRect);
            } else if (stage == 2) {
                Request(true,false);
                Check(vocals.WordPreservationStatus.Contains("STRICT LYRICS: ON"),"Strict ON status missing");
                Click(vocals.SlowerPhraseDensityRect);
            } else if (stage == 3) {
                Request(true,true);
                Check(vocals.WordPreservationStatus == "STRICT LYRICS: ON\nSLOWER PHRASE DENSITY: ON","Both ON indicators missing");
                Click(vocals.StrictLyricsRect);
            } else if (stage == 4) {
                Request(false,true); Click(vocals.SlowerPhraseDensityRect);
            } else if (stage == 5) {
                Request(false,false);
                report += "PASS " + Sizes[scenario] + ": production Draw, visible unclipped 36px controls and status, actual clicks, all four CurrentRequest/JSON flag combinations; existing settings preserved.\n";
                if (++scenario == Sizes.Length) { Finish(true); return; }
                Configure(); return;
            }
            stage++; Repaint();
        } catch (Exception e) { report += e + "\n"; Finish(false); }
    }
    void Finish(bool success) {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Word-Options-Runtime-UI.txt", (success ? "PASS\n" : "FAIL\n") + report + "No generation methods called. No ElevenLabs/network calls.\n");
        vocals.Dispose(); DestroyImmediate(owner); Close(); EditorApplication.Exit(success ? 0 : 1);
    }
}
