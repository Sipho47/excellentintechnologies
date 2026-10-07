using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
class VocalCheck {
    static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Near(double a, double b, double tolerance, string name) { Assert(Math.Abs(a-b)<=tolerance, name + ": "+a+" vs "+b); }
    static async Task Main() {
        Directory.CreateDirectory("Tools/VocalValidation");
        var report = new StringBuilder();
        var pronunciationRequest = new AiVocalRequest { Lyrics="Original \u00e9 e\u0301 \u65e5\u672c\ud83c\udfb5\r\n  lyrics  ", AiPronunciationText="Manually edited sung words" };
        var pronunciationResult = new AiVocalResult { Request=pronunciationRequest, Provider="OFFLINE metadata fixture" };
        pronunciationRequest.StrictLyrics=true;
        pronunciationRequest.SlowerPhraseDensity=true;
        string pronunciationInfo=AiVocalWav.Info(pronunciationResult);
        Assert(pronunciationInfo.Contains("STRICT LYRICS / WORD PRESERVATION (REAL AI style only): True") && pronunciationInfo.Contains("SLOWER PHRASE DENSITY (REAL AI style only; project BPM unchanged): True"),"Export records word preservation settings separately");
        Assert(pronunciationInfo.Contains("Lyrics:\n"+pronunciationRequest.Lyrics+"\nBPM:"),"Canonical lyrics preserved exactly in lyric export");
        Assert(pronunciationInfo.Contains("AI Pronunciation Text (REAL AI sung words only; original Lyrics preserved above):\n"+pronunciationRequest.AiPronunciationText),"Pronunciation text separately recorded in metadata");
        Assert(pronunciationRequest.Prompt.Contains(pronunciationRequest.Lyrics) && !pronunciationRequest.Prompt.Contains(pronunciationRequest.AiPronunciationText),"Canonical prompt metadata never substitutes original lyrics");
        report.AppendLine("PASS original Unicode lyric export and prompt metadata preserved with independent pronunciation text.");
        var lyricRandom = new Random(104);
        string previousHook = "";
        var uniqueHooks = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 1000; i++) {
            string hook = BacardiHookLyrics.Generate(lyricRandom, previousHook);
            Assert(hook != previousHook, "New lyrics must change the previous hook");
            var lines = hook.Split('\n');
            Assert(lines.Length == 4, "Four-line hook");
            foreach (string line in lines) Assert(!string.IsNullOrWhiteSpace(line), "Non-empty lyric lines");
            uniqueHooks.Add(hook); previousHook = hook;
        }
        Assert(uniqueHooks.Count > 100, "Lyric variety");
        report.AppendLine("PASS local hook lyrics: four non-empty lines, varied hooks, no immediate repeats.");
        var t = new BacardiVocalTimeline(); t.Play(100,104);
        Near(t.DspAtBeat(48)-100,27.6923076923077,1e-9,"Bar 13 DSP");
        Near(t.DspAtBeat(64)-100,36.9230769230769,1e-9,"Bar 17 DSP");
        Near(t.BeatsAt(102),104.0/30,1e-9,"Musical beats");
        t.Pause(102); double held = t.BeatsAt(102);
        Near(t.BeatsAt(900),held,1e-9,"Pause beat freeze");
        Near(t.ElapsedAt(900),2,1e-9,"Pause elapsed freeze");
        t.Play(200,104); Near(t.BeatsAt(201),held+104.0/60,1e-9,"Resume");
        double atBoundary=t.BeatsAt(202); t.SetTempo(120,202);
        Near(t.BeatsAt(203),atBoundary+2,1e-9,"Tempo future segment");
        Near(t.DspAtBeat(48),202+(48-atBoundary)/2,1e-9,"Tempo reschedule");
        t.Stop(); Assert(t.BarAt(999)==1 && t.BeatsAt(999)==0,"Stop reset");
        Assert(BacardiVocalTimeline.FrameAtBar(13,104)==1221231,"Bar 13 sample frame");
        Assert(BacardiVocalTimeline.FrameAtBar(17,104)==1628308,"Bar 17 sample frame");
        report.AppendLine("PASS timeline: DSP mapping, pause/resume, stop, future tempo changes; Bar13=1221231 frames, Bar17=1628308 frames.");
        var provider = new MockAiVocalProvider();
        var progress = new Progress<AiVocalProgress>();
        foreach(string lyrics in new[] {
            "Sondela kimi, ungangidedeli\nHamba nami kancane\nSizodansa ubusuku bonke\nLapho siya khona, sazi thina",
            "Coração, l’été — canción!\r\nNão me deixes…",
            "君と踊ろう\n함께 춤춰요\n一起跳舞\nارقص معي 🎵"
        }) {
            var unicode=await provider.GenerateAsync(new AiVocalRequest { Lyrics=lyrics },progress,CancellationToken.None);
            Assert(unicode.Request.Lyrics==lyrics && AiVocalWav.Info(unicode).Contains(lyrics),"Mock preserves Unicode lyrics and export metadata");
        }
        report.AppendLine("PASS MOCK exact isiZulu, accented Latin and non-Latin lyrics in request/export metadata.");
        var request = new AiVocalRequest { Lyrics="Come closer, don\u2019t let go\nMove with me, nice and slow\nAll night, feel the glow\nWhere we\u2019re going, only we know" };
        var result = await provider.GenerateAsync(request,progress,CancellationToken.None);
        var strictMock=await provider.GenerateAsync(new AiVocalRequest { Lyrics=request.Lyrics,StrictLyrics=true,SlowerPhraseDensity=true },progress,CancellationToken.None);
        Assert(strictMock.Pcm.Length==result.Pcm.Length && strictMock.Request.Bpm==request.Bpm,"REAL AI guidance does not change MOCK duration or tempo");
        for(int sample=0;sample<result.Pcm.Length;sample++) Assert(strictMock.Pcm[sample]==result.Pcm[sample],"REAL AI guidance leaves MOCK PCM unchanged");
        string strictInfo=AiVocalWav.Info(strictMock);
        Assert(strictInfo.Contains("STRICT LYRICS / WORD PRESERVATION (REAL AI style only): True") && strictInfo.Contains("SLOWER PHRASE DENSITY (REAL AI style only; project BPM unchanged): True"),"Export records generation options separately");
        report.AppendLine("PASS strict/density options preserve MOCK PCM, BPM and duration; export records settings separately.");
        Assert(result.Pcm.Length==814154,"4-bar stereo frame count");
        Assert(result.PeakDb < -12 && !double.IsNaN(result.Integrated),"Vocal headroom and loudness");
        AiVocalWav.Write("Tools/VocalValidation/AI_VOCAL_LEAD_104BPM_CMAJOR.wav",result.Pcm);
        var one = AiVocalWav.Aligned(result,13,32,104);
        var two = AiVocalWav.Aligned(result,17,32,104);
        long first=BacardiVocalTimeline.FrameAtBar(13,104)*2, second=BacardiVocalTimeline.FrameAtBar(17,104)*2;
        for(int i=0;i<first;i++) Assert(one[i]==0,"Leading silence");
        for(int i=0;i<result.Pcm.Length;i++) Near(one[first+i],result.Pcm[i],1e-7,"Exact aligned PCM");
        for(long i=first+result.Pcm.Length;i<one.Length;i++) Assert(one[i]==0,"Trailing silence");
        for(int i=0;i<one.Length;i++) one[i]+=two[i];
        AiVocalWav.Write("Tools/VocalValidation/AI_VOCAL_ALIGNED_104BPM.wav",one);
        using(var reader=new BinaryReader(File.OpenRead("Tools/VocalValidation/AI_VOCAL_ALIGNED_104BPM.wav"))) {
            Assert(Encoding.ASCII.GetString(reader.ReadBytes(4))=="RIFF","RIFF");
            reader.BaseStream.Position=22; Assert(reader.ReadInt16()==2,"Stereo");
            Assert(reader.ReadInt32()==44100,"44100 Hz");
            reader.BaseStream.Position=34; Assert(reader.ReadInt16()==24,"24-bit");
            reader.BaseStream.Position=40; Assert(reader.ReadInt32()==one.Length*3,"WAV data length");
            Assert(reader.BaseStream.Length==44+one.Length*3,"WAV file length");
        }
        File.WriteAllText("Tools/VocalValidation/VOCAL_INFO.txt",AiVocalWav.Info(result));
        report.AppendLine("PASS mock Female 104 BPM C Major: 4 bars, peak="+result.PeakDb.ToString("F2",CultureInfo.InvariantCulture)+" dBFS, LUFS-I="+result.Integrated.ToString("F2",CultureInfo.InvariantCulture)+". No loudness normalization.");
        report.AppendLine("PASS 32-bar stereo 44100 Hz / 24-bit aligned WAV: both hooks; leading/trailing silence; exact PCM placement.");
        try { AiVocalWav.Aligned(result,13,32,120); throw new Exception("Mismatched-BPM export must require regeneration."); }
        catch(InvalidOperationException) { report.AppendLine("PASS tempo mismatch rejects pitch-shift conversion; explicit regeneration required."); }
        var fresh=await provider.GenerateAsync(new AiVocalRequest { Lyrics=request.Lyrics,Bpm=120 },progress,CancellationToken.None);
        var adjusted=AiVocalWav.Aligned(fresh,13,32,120);
        Assert(adjusted.Length==(int)BacardiVocalTimeline.FrameAtBar(33,120)*2,"Regenerated-tempo aligned length");
        report.AppendLine("PASS regenerated 120 BPM output aligns without resampling.");
        using(var cancelled=new CancellationTokenSource()) {
            cancelled.Cancel();
            try { await provider.GenerateAsync(request,progress,cancelled.Token); throw new Exception("Cancellation failed"); }
            catch(OperationCanceledException) { report.AppendLine("PASS generation cancellation."); }
        }
        File.WriteAllText("Tools/VocalValidation/Managed-Validation.txt",report.ToString());
        Console.WriteLine(report);
    }
}
