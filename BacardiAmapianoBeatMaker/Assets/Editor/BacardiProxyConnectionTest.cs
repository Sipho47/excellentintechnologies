using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Dedicated free connectivity acceptance: no GenerateAsync, jobs or ElevenLabs calls.
public static class BacardiProxyConnectionTest {
    static BacardiAiVocalGenerator vocals;
    static GameObject owner;
    static double deadline;
    public static void RunCommandLine() {
        owner = new GameObject("Proxy health acceptance");
        vocals = new BacardiAiVocalGenerator(owner);
        vocals.Provider = new ElevenLabsAiVocalProvider();
        vocals.TestRealConnection();
        deadline = EditorApplication.timeSinceStartup + 15;
        EditorApplication.update += Tick;
    }
    static void Tick() {
        vocals.Tick(0, 104, 0, 1);
        if (vocals.TestingConnection && EditorApplication.timeSinceStartup < deadline) return;
        bool ok = !vocals.TestingConnection && vocals.ConnectionStatus.StartsWith("PROXY ONLINE") && !vocals.Busy && vocals.Result == null;
        string report = (ok ? "PASS\n" : "FAIL\n") + vocals.ConnectionStatus + "\nUnity TEST REAL AI CONNECTION path; no generation task or audio result.\n";
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllText("Tools/VocalValidation/Unity-Proxy-Connection.txt", report);
        Debug.Log(report);
        vocals.Dispose(); UnityEngine.Object.DestroyImmediate(owner);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
