using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;

[Serializable]
public sealed class AiVocalRequest {
    public string Lyrics, Voice = "Female", Role = "Lead", Key = "C", Scale = "Major";
    public string Style = "Smooth Amapiano / Afro-house";
    public string PronunciationGuidance = "";
    public string AiPronunciationText = "";
    public bool StrictLyrics, SlowerPhraseDensity;
    public bool UsesPronunciationText { get { return !string.IsNullOrWhiteSpace(AiPronunciationText); } }
    public string Character = "warm, intimate, airy, clear, soulful, hypnotic";
    public bool RelativeMinor = true, Dry = true;
    public double Bpm = 104;
    public int Bars = 4;
    public float Energy = .45f, Emotion = .6f;
    public double Seconds { get { return Bars * 4 * 60 / Bpm; } }
    public string Prompt { get {
        return string.Format(CultureInfo.InvariantCulture,
            "{0} {1} singer, {2:0.##} BPM, {3} {4}{5}, {6}, energy {7:0.00}, emotion {8:0.00}. {9} part, {10} bars in 4/4. Short catchy phrases with space between lines, emotional but relaxed, South African Amapiano feel. Isolated sung vocals without drums, bass, piano or instrumental backing. Simple memorable melody rhythmically loose around the groove. {11}. Lyrics:\n{12}",
            Style, Voice, Bpm, Key, Scale, RelativeMinor && Key == "C" && Scale == "Major" ? " with A minor flavor" : "",
            Character, Energy, Emotion, Role, Bars, Dry ? "Dry vocal" : "Light ambience", Lyrics);
    } }
}
public sealed class AiVocalProgress {
    public readonly string State;
    public readonly float Fraction;
    public AiVocalProgress(string state, float fraction) { State = state; Fraction = fraction; }
}
public sealed class AiVocalResult {
    public string Source = "REAL_AI";
    public AiVocalRequest Request;
    public string Provider;
    public bool IsMock;
    public float[] Pcm; // Interleaved stereo PCM; owns its immutable generation settings.
    public int SampleRate = 44100;
    public double PeakDb, Integrated;
}
[Serializable]
public sealed class AiVocalSavedMetadata {
    public int Version=1, SampleRate=44100, Channels=2, Bits=24, Frames;
    public string FileName, WavSha256, Source, Provider;
    public bool IsMock;
    public AiVocalRequest Request;
}
public interface IAiVocalProvider {
    string Name { get; }
    Task<AiVocalResult> GenerateAsync(AiVocalRequest request, IProgress<AiVocalProgress> progress, CancellationToken cancellation);
}

// No service, API keys, payment or network access. A vowel-like melody, NOT sung lyrics.
public sealed class MockAiVocalProvider : IAiVocalProvider {
    public string Name { get { return "LOCAL MOCK (synthetic vowels; does not sing lyrics)"; } }
    public Task<AiVocalResult> GenerateAsync(AiVocalRequest r, IProgress<AiVocalProgress> progress, CancellationToken token) {
        return Task.Run(() => {
            if (string.IsNullOrWhiteSpace(r.Lyrics)) throw new ArgumentException("Enter lyrics first.");
            if (r.Bpm < 80 || r.Bpm > 150 || (r.Bars != 2 && r.Bars != 4 && r.Bars != 8 && r.Bars != 16))
                throw new ArgumentException("Choose 80-150 BPM and 2, 4, 8 or 16 bars.");
            progress.Report(new AiVocalProgress("GENERATING...", .05f));
            int frames = checked((int)Math.Round(r.Seconds * 44100));
            float[] pcm = new float[frames * 2];
            int[] major = { 0, 4, 7, 9, 7, 4, 2, 0 };
            int[] minor = { 0, 3, 7, 10, 7, 3, 2, 0 };
            var notes = r.Scale == "Minor" ? minor : major;
            double root = r.Key == "A" ? 220 : 261.625565;
            if (r.Voice == "Male") root /= 2;
            if (r.Role == "Harmony") root *= Math.Pow(2, 4.0 / 12);
            double phase = 0, peak = 0;
            for (int n = 0; n < frames; n++) {
                if (n % 8192 == 0) {
                    token.ThrowIfCancellationRequested();
                    progress.Report(new AiVocalProgress("GENERATING...", .05f + .75f * n / frames));
                }
                double t = n / 44100.0, beat = t * r.Bpm / 60;
                int phrase = (int)(beat / 4);
                double within = beat % 4, syllable = within % .75;
                double envelope = within < 2.8 ? Math.Sin(Math.PI * Math.Min(1, syllable / .65)) : 0;
                if (syllable > .65) envelope = 0;
                if (r.Role == "Ad-lib" && phrase % 2 == 0) envelope = 0;
                double frequency = root * Math.Pow(2, notes[(int)(beat / .75) % notes.Length] / 12.0);
                phase += 2 * Math.PI * frequency * (1 + .003 * r.Emotion * Math.Sin(t * 32)) / 44100;
                double vowel = Math.Sin(phase) + .25 * Math.Sin(phase * 2) + .13 * Math.Sin(phase * 3);
                double edge = Math.Min(1, t * 100) * Math.Min(1, (r.Seconds - t) * 100);
                double value = vowel * envelope * edge * (.08 + .06 * r.Energy);
                pcm[n * 2] = pcm[n * 2 + 1] = (float)value;
            }
            if (!r.Dry) {
                int delay = (int)(.12 * 44100) * 2;
                for (int i = pcm.Length - 1; i >= delay; i--) pcm[i] += pcm[i - delay] * .12f;
            }
            progress.Report(new AiVocalProgress("PROCESSING...", .85f));
            foreach (float value in pcm) peak = Math.Max(peak, Math.Abs(value));
            // Peak-only attenuation for mixing headroom; no LUFS normalization.
            double gain = peak > .25 ? .25 / peak : 1;
            for (int i = 0; i < pcm.Length; i++) pcm[i] *= (float)gain;
            token.ThrowIfCancellationRequested();
            return new AiVocalResult { Pcm = pcm, Request = r, Provider = Name, IsMock = true, Source = "LOCAL_MOCK",
                PeakDb = 20 * Math.Log10(Math.Max(1e-20, peak * gain)),
                Integrated = BacardiMastering.Measure(pcm, 44100) };
        }, token);
    }
}
public static class AiVocalWav {
    public static float[] Aligned(AiVocalResult vocal, int startBar, int songBars, double bpm) {
        long start = BacardiVocalTimeline.FrameAtBar(startBar, bpm);
        int outputFrames = checked((int)BacardiVocalTimeline.FrameAtBar(songBars + 1, bpm));
        RequireTempo(vocal, bpm);
        int frames = vocal.Pcm.Length / 2;
        if (start + frames > outputFrames) throw new ArgumentException("Song length must include the complete vocal.");
        var output = new float[checked(outputFrames * 2)];
        Array.Copy(vocal.Pcm, 0, output, checked((int)start * 2), vocal.Pcm.Length);
        return output;
    }
    public static void RequireTempo(AiVocalResult vocal, double bpm) {
        if (Math.Abs(vocal.Request.Bpm - bpm) > .000001)
            throw new InvalidOperationException("Vocal was generated at " + vocal.Request.Bpm + " BPM. Regenerate at " + bpm + " BPM; pitch shifting is disabled.");
    }
    public static void Write(string path, float[] pcm) {
        if (pcm.Length % 2 != 0) throw new ArgumentException("Stereo PCM required.");
        foreach (float value in pcm)
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 1)
                throw new ArgumentException("Invalid or clipping vocal PCM; export aborted.");
        using (var w = new BinaryWriter(File.Create(path))) {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(checked(36 + pcm.Length * 3));
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)2); w.Write(44100); w.Write(44100 * 6);
            w.Write((short)6); w.Write((short)24);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(checked(pcm.Length * 3));
            foreach (float x in pcm) {
                int sample = (int)Math.Round(x * 8388607);
                w.Write((byte)sample); w.Write((byte)(sample >> 8)); w.Write((byte)(sample >> 16));
            }
        }
    }
    public static string Info(AiVocalResult r) {
        var q = r.Request;
        return string.Format(CultureInfo.InvariantCulture,
            "Provider: {0}\n{17}\nLyrics:\n{1}\nBPM: {2}\nKey: {3} {4}\nStyle: {5}\nCharacter: {6}\nVoice: {7}\nRole: {8}\nBars: {9}\nEnergy: {10}\nEmotion: {11}\nDry: {12}\nRelative minor flavor: {13}\nVocal sample peak: {14:F2} dBFS\nVocal LUFS-I: {15:F2}\nFormat: 44100 Hz, stereo, 24-bit PCM\nNo vocal LUFS normalization. -14 LUFS is a MASTER target.\nPrompt:\n{16}\n",
            r.Provider, q.Lyrics, q.Bpm, q.Key, q.Scale, q.Style, q.Character, q.Voice, q.Role,
            q.Bars, q.Energy, q.Emotion, q.Dry, q.RelativeMinor, r.PeakDb, r.Integrated, q.Prompt, r.IsMock ? "MOCK AUDIO: synthetic vowels, not intelligible sung lyrics." : "Generated isolated vocal.")
            + "Pronunciation / Accent Guidance (REAL AI style only):\n" + q.PronunciationGuidance + "\n"
            + "AI Pronunciation Text (REAL AI sung words only; original Lyrics preserved above):\n" + q.AiPronunciationText + "\n"
            + "STRICT LYRICS / WORD PRESERVATION (REAL AI style only): " + q.StrictLyrics + "\n"
            + "SLOWER PHRASE DENSITY (REAL AI style only; project BPM unchanged): " + q.SlowerPhraseDensity + "\n";
    }
}
