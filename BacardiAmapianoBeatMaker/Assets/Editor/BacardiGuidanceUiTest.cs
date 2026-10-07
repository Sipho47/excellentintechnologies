using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class BacardiGuidanceUiTest : EditorWindow {
    BacardiAiVocalGenerator vocals;
    GameObject owner;
    int frames, stage;
    const string Edited = "Edited isiZulu guidance — keep these exact words.";
    Vector2 scroll;
    static double deadline;
    public static void RunCommandLine() {
        var window = GetWindow<BacardiGuidanceUiTest>();
        window.position = new Rect(0, 0, 1920, 1080);
        window.Show();
        deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += window.Pump;
    }
    void Pump() {
        if (EditorApplication.timeSinceStartup > deadline) Finish(false, "UI test timed out");
        Repaint();
    }
    void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    void OnGUI() {
        try {
            if (vocals == null) { owner = new GameObject("Guidance UI test"); vocals = new BacardiAiVocalGenerator(owner); }
            scroll = GUILayout.BeginScrollView(scroll);
            vocals.Draw(104, "UI validation only");
            GUILayout.EndScrollView();
            if (Event.current.type != EventType.Repaint || ++frames < 3) return;
            var field = vocals.GuidanceFieldRect;
            var preset = vocals.GuidancePresetRect;
            var origin = GUIUtility.GUIToScreenPoint(Vector2.zero);
            var viewport = new Rect(origin, position.size);
            Check(vocals.GuidanceRepaintCount > 0 && field.height >= 72 && preset.height >= 30, "Guidance controls missing or height clipped");
            Check(viewport.Contains(field.min) && viewport.Contains(field.max) && viewport.Contains(preset.min) && viewport.Contains(preset.max), "Guidance controls outside visible viewport");
            Check(vocals.LyricsRect.yMax <= vocals.GuidanceLabelRect.yMin && vocals.GuidanceLabelRect.yMax <= field.yMin && field.yMax <= preset.yMin && preset.yMax <= vocals.GenerateRect.yMin, "Incorrect layout order or overlap");
            if (stage == 0) {
                GUI.FocusControl("Bacardi.PronunciationGuidance");
                stage = 1;
                return;
            }
            if (stage == 1) {
                Check(GUI.GetNameOfFocusedControl() == "Bacardi.PronunciationGuidance", "Test must exercise focused guidance editor");
                vocals.ApplyIsiZuluPronunciation();
                stage = 2;
                return;
            }
            string expected = stage == 2 ? BacardiAiVocalGenerator.IsiZuluPronunciation : Edited;
            Check(vocals.DisplayedPronunciationGuidance == expected, "Repaint still draws blank or stale field contents");
            Check(vocals.PronunciationGuidance == expected, "Focused editor overwrote guidance");
            Check(vocals.Lyrics == BacardiAiVocalGenerator.Hook, "Preset changed lyrics");
            Check(vocals.CurrentRequest(104).PronunciationGuidance == vocals.DisplayedPronunciationGuidance, "Request differs from visible field");
            if (stage == 2) {
                vocals.PronunciationGuidance = Edited;
                stage = 3;
                return;
            }
            Finish(true, "PASS 1920x1080 runtime Draw: focused guidance preset handler; NEXT repaint draws exact preset text; subsequent edited contents render and map exactly to CurrentRequest; lyrics unchanged; visible field and button without overlap. No ElevenLabs calls.");
        } catch (Exception e) { Finish(false, e.ToString()); }
    }
    void Finish(bool ok, string message) {
        EditorApplication.update -= Pump;
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Guidance-UI.txt", message);
        if (vocals != null) vocals.Dispose();
        if (owner != null) DestroyImmediate(owner);
        Debug.Log(message);
        EditorApplication.Exit(ok ? 0 : 1);
    }
}



