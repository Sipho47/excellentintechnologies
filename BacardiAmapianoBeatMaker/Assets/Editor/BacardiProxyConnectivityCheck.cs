using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Exercises the same health-only action as the in-game connection button.
[InitializeOnLoad]
public static class BacardiProxyConnectivityCheck
{
    static BacardiAiVocalGenerator generator;
    static GameObject owner;
    static double deadline;
    static bool commandLine;
    static readonly string RequestPath = "Tools/VocalValidation/Request-Proxy-Health.txt";
    static readonly string ResultPath = "Tools/VocalValidation/Unity-Proxy-Health.txt";

    static BacardiProxyConnectivityCheck() {
        EditorApplication.delayCall += () => { if (File.Exists(RequestPath)) { File.Delete(RequestPath); Run(); } };
    }

    [MenuItem("Bacardi/TEST REAL AI CONNECTION (health only)")]
    public static void Run() {
        if (generator != null) return;
        owner = new GameObject("Proxy health check") { hideFlags = HideFlags.HideAndDontSave };
        generator = new BacardiAiVocalGenerator(owner);
        generator.TestRealConnection();
        deadline = EditorApplication.timeSinceStartup + 10;
        EditorApplication.update += Poll;
    }

    public static void RunCommandLine() { commandLine = true; Run(); }

    static void Poll() {
        generator.Tick(AudioSettings.dspTime, 104, AudioSettings.dspTime, 1);
        if (generator.TestingConnection && EditorApplication.timeSinceStartup < deadline) return;
        string result = generator.TestingConnection ? "PROXY CONNECTION ERROR: Unity health check exceeded 10 seconds" : generator.ConnectionStatus;
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
        File.WriteAllText(ResultPath, "Unity " + Application.unityVersion + "\n" + result + "\n");
        Debug.Log(result);
        EditorApplication.update -= Poll;
        UnityEngine.Object.DestroyImmediate(owner);
        generator.Dispose(); generator = null;
        if (commandLine) EditorApplication.Exit(result.StartsWith("PROXY ONLINE", StringComparison.Ordinal) ? 0 : 1);
    }
}
