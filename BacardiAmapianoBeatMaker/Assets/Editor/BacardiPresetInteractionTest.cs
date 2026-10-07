using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Production Draw and native IMGUI events, with no generation or service calls.
public sealed class BacardiPresetInteractionTest : EditorWindow {
    BacardiAiVocalGenerator vocals;
    GameObject owner;
    int stage, repaint, warmup;
    double deadline;
    Vector2 scroll;
    string original;
    bool applyPreset, focusField;
    string nativeEditorText;
    public static void RunCommandLine() {
        var window = CreateInstance<BacardiPresetInteractionTest>();
        window.owner = new GameObject("Preset interaction verification");
        window.vocals = new BacardiAiVocalGenerator(window.owner);
        window.original = window.vocals.Lyrics;
        window.position = new Rect(0, 0, 1920, 1080);
        window.deadline = EditorApplication.timeSinceStartup + 60;
        window.Show(); window.Focus();
        EditorApplication.update += window.Tick;
    }
    void OnGUI() {
        if (vocals == null) return;
        scroll = GUILayout.BeginScrollView(scroll);
        vocals.Draw(104, "No generation: preset interaction verification");
        GUILayout.EndScrollView();
        if (Event.current.type == EventType.Repaint) {
            if (focusField) { GUI.FocusControl("Bacardi.PronunciationGuidance"); focusField = false; }
            if (GUI.GetNameOfFocusedControl() == "Bacardi.PronunciationGuidance")
                nativeEditorText = ((TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl)).text;
            if (applyPreset) {
                vocals.ApplyIsiZuluPronunciation();
                Check(vocals.PronunciationGuidance == BacardiAiVocalGenerator.IsiZuluPronunciation, "Handler must immediately set exact preset");
                applyPreset = false;
            }
        }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    void Verify(string expected) {
        Check(vocals.DisplayedPronunciationGuidance == expected, "Repaint displays stale/blank text");
        Check(vocals.PronunciationGuidance == expected, "Model differs from displayed field");
        Check(vocals.CurrentRequest(104).PronunciationGuidance == expected, "Request differs from displayed field");
        Check(vocals.Lyrics == original, "Lyrics changed");
    }
    void Tick() {
        try {
            Check(EditorApplication.timeSinceStartup < deadline, "Timed out");
            if (vocals.GuidanceRepaintCount <= repaint) { Repaint(); return; }
            repaint = vocals.GuidanceRepaintCount;
            // The OS clamps a newly shown window; let IMGUI relayout at its final size.
            if (++warmup < 4) { Repaint(); return; }
            if (stage == 0) {
                Verify("");
                applyPreset = true;
            } else if (stage == 1) {
                // Wait for the handler repaint, then a repaint of the populated field.
                if (applyPreset || vocals.DisplayedPronunciationGuidance == "") { Repaint(); return; }
                Verify(BacardiAiVocalGenerator.IsiZuluPronunciation);
                focusField = true;
            } else if (stage == 2) {
                if (focusField) { Repaint(); return; }
                SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control });
                SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.X, character = 'x' });
            } else if (stage == 3) {
                Verify("x");
                applyPreset = true;
            } else if (stage == 4) {
                if (applyPreset || vocals.DisplayedPronunciationGuidance == "x") { Repaint(); return; }
                Verify(BacardiAiVocalGenerator.IsiZuluPronunciation);
                // Refocus to verify Unity's native editor buffer, not just model telemetry.
                focusField = true;
            } else {
                if (focusField) { Repaint(); return; }
                Verify(BacardiAiVocalGenerator.IsiZuluPronunciation);
                Check(nativeEditorText == BacardiAiVocalGenerator.IsiZuluPronunciation, "Native editor contains stale text");
                Finish(true, "PASS preset click handler from blank and focused edited field; immediate exact value; next and subsequent repaint display exact preset; native TextEditor buffer matches; user keyboard editing works; CurrentRequest matches visible contents; lyrics unchanged. No generation invoked. No ElevenLabs calls.");
                return;
            }
            stage++; Repaint();
        } catch (Exception error) { Finish(false, error.ToString()); }
    }
    void Finish(bool success, string message) {
        EditorApplication.update -= Tick;
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Preset-Interaction.txt", (success ? "PASS\n" : "FAIL\n") + message);
        vocals.Dispose(); DestroyImmediate(owner); Close();
        EditorApplication.Exit(success ? 0 : 1);
    }
}
