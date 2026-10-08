using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// Local file IO only. No provider construction, generation or network calls.
public static class BacardiSavedVocalLoader {
    const string RealProvider="ElevenLabs Music v2.5 / two-stem vocal separation";
    public static string MetadataPath(string wav) { return wav+".vocal.json"; }
    static string Hash(byte[] bytes) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
    public static void WriteMetadata(string wav,AiVocalResult result) {
        var metadata=new AiVocalSavedMetadata { FileName=Path.GetFileName(wav),WavSha256=Hash(File.ReadAllBytes(wav)),
            Source=result.Source,Provider=result.Provider,IsMock=result.IsMock,Frames=result.Pcm.Length/2,Request=result.Request };
        File.WriteAllText(MetadataPath(wav),JsonUtility.ToJson(metadata,true));
    }
    public static AiVocalResult Load(string path,double projectBpm) {
        if(string.IsNullOrWhiteSpace(path)||!File.Exists(path)) throw new FileNotFoundException("Select an existing saved vocal WAV.");
        var file=new FileInfo(path);
        if(file.Length<44||file.Length>64*1024*1024) throw new InvalidDataException("Saved vocal WAV must be non-empty and at most 64 MB.");
        var bytes=File.ReadAllBytes(path); ValidateHeader(bytes);
        var pcm=ElevenLabsAiVocalProvider.DecodeWav(bytes);
        AiVocalSavedMetadata m;
        if(File.Exists(MetadataPath(path))) {
            if(new FileInfo(MetadataPath(path)).Length>65536) throw new InvalidDataException("Vocal sidecar is too large.");
            m=JsonUtility.FromJson<AiVocalSavedMetadata>(File.ReadAllText(MetadataPath(path)));
            if(m==null||m.Version!=1||m.FileName!=Path.GetFileName(path)||m.WavSha256!=Hash(bytes)||
                m.SampleRate!=44100||m.Channels!=2||m.Bits!=24||m.Frames!=pcm.Length/2)
                throw new InvalidDataException("Saved vocal metadata does not match the selected WAV.");
        } else m=ReadLegacy(path,pcm.Length/2);
        ValidateIdentity(m); ValidateRequest(m.Request);
        if(Math.Abs(m.Request.Bpm-projectBpm)>.000001) throw new InvalidDataException("Saved vocal is "+m.Request.Bpm+" BPM; project is "+projectBpm+" BPM. Match the project tempo before loading; no pitch shift or resampling is applied.");
        int expected=checked((int)Math.Round(m.Request.Seconds*44100));
        if(Math.Abs((long)expected-pcm.Length/2)>1) throw new InvalidDataException("WAV duration does not match metadata BPM and bar length. Aligned stems cannot be loaded as individual takes.");
        double peak=0; foreach(float sample in pcm) { if(float.IsNaN(sample)||float.IsInfinity(sample)||Math.Abs(sample)>1) throw new InvalidDataException("Invalid saved vocal audio."); peak=Math.Max(peak,Math.Abs(sample)); }
        return new AiVocalResult { Source=m.Source,Provider=m.Provider,IsMock=m.IsMock,Request=m.Request,Pcm=pcm,SampleRate=44100,
            PeakDb=20*Math.Log10(Math.Max(1e-20,peak)),Integrated=BacardiMastering.Measure(pcm,44100) };
    }
    static void ValidateHeader(byte[] bytes) {
        using(var r=new BinaryReader(new MemoryStream(bytes))) {
            if(Encoding.ASCII.GetString(r.ReadBytes(4))!="RIFF"||r.ReadUInt32()!=bytes.Length-8||Encoding.ASCII.GetString(r.ReadBytes(4))!="WAVE")
                throw new InvalidDataException("Malformed RIFF WAV length/header.");
            int formats=0,data=0;
            while(r.BaseStream.Position<bytes.Length) {
                if(bytes.Length-r.BaseStream.Position<8) throw new InvalidDataException("Truncated WAV chunk header.");
                string id=Encoding.ASCII.GetString(r.ReadBytes(4)); uint size=r.ReadUInt32(); long end=r.BaseStream.Position+size;
                if(end+(size&1)>bytes.Length) throw new InvalidDataException("Truncated WAV chunk.");
                if(id=="fmt ") {
                    if(++formats!=1||size<16||r.ReadUInt16()!=1||r.ReadUInt16()!=2||r.ReadInt32()!=44100||r.ReadInt32()!=44100*6||r.ReadUInt16()!=6||r.ReadUInt16()!=24)
                        throw new InvalidDataException("Saved vocal requires standard 44100 Hz stereo 24-bit PCM WAV. No automatic conversion.");
                } else if(id=="data"&&++data!=1) throw new InvalidDataException("Ambiguous WAV audio chunks.");
                r.BaseStream.Position=end+(size&1);
            }
            if(formats!=1||data!=1) throw new InvalidDataException("Missing WAV format/audio chunk.");
        }
    }
    static void ValidateIdentity(AiVocalSavedMetadata m) {
        if(m.Source=="REAL_AI"&&!m.IsMock&&m.Provider==RealProvider) return;
        if(m.Source=="LOCAL_MOCK"&&m.IsMock&&m.Provider==new MockAiVocalProvider().Name) return;
        throw new InvalidDataException("Unproven or conflicting vocal source. REAL_AI requires matching ElevenLabs metadata; a filename is not evidence.");
    }
    static void ValidateRequest(AiVocalRequest q) {
        if(q==null||string.IsNullOrWhiteSpace(q.Lyrics)||q.Bpm<80||q.Bpm>150||double.IsNaN(q.Bpm)||
            (q.Bars!=2&&q.Bars!=4&&q.Bars!=8&&q.Bars!=16)||
            (q.Voice!="Female"&&q.Voice!="Male")||(q.Role!="Lead"&&q.Role!="Harmony"&&q.Role!="Ad-lib")||
            !((q.Key=="A"&&q.Scale=="Minor")||(q.Key=="C"&&q.Scale=="Major"))||
            float.IsNaN(q.Energy)||q.Energy<0||q.Energy>1||float.IsNaN(q.Emotion)||q.Emotion<0||q.Emotion>1)
            throw new InvalidDataException("Missing or invalid saved vocal settings.");
    }
    static AiVocalSavedMetadata ReadLegacy(string path,int frames) {
        string info=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"VOCAL_INFO.txt");
        if(!File.Exists(info)||new FileInfo(info).Length>65536) throw new InvalidDataException("Matching VOCAL_INFO.txt or WAV sidecar required; cannot infer REAL_AI from filename.");
        string text=File.ReadAllText(info).Replace("\r\n","\n");
        if(text.StartsWith("ALIGNED VOCAL STEM")||text.Contains("Placement Bar ")) throw new InvalidDataException("Aligned/multiple-placement stem metadata is not an individual take.");
        int lyricsStart=text.IndexOf("\nLyrics:\n",StringComparison.Ordinal),lyricsEnd=text.IndexOf("\nBPM: ",StringComparison.Ordinal),prompt=text.IndexOf("\nPrompt:\n",StringComparison.Ordinal);
        if(lyricsStart<0||lyricsEnd<=lyricsStart||prompt<=lyricsEnd) throw new InvalidDataException("Malformed legacy vocal metadata.");
        string header=text.Substring(0,lyricsStart)+text.Substring(lyricsEnd,prompt-lyricsEnd);
        var fields=new Dictionary<string,string>();
        foreach(string line in header.Split('\n')) {
            int colon=line.IndexOf(": ",StringComparison.Ordinal); if(colon<0) continue;
            string key=line.Substring(0,colon); if(fields.ContainsKey(key)) throw new InvalidDataException("Ambiguous legacy field: "+key);
            fields[key]=line.Substring(colon+2);
        }
        string provider=Field(fields,"Provider"),keyValue=Field(fields,"Key"); string[] keyParts=keyValue.Split(' ');
        if(keyParts.Length!=2||Field(fields,"Format")!="44100 Hz, stereo, 24-bit PCM") throw new InvalidDataException("Invalid legacy format/key metadata.");
        bool mock=provider==new MockAiVocalProvider().Name;
        if(mock?!text.Contains("MOCK AUDIO: synthetic vowels, not intelligible sung lyrics."):!text.Contains("\nGenerated isolated vocal.\n")) throw new InvalidDataException("Legacy source description is missing or conflicting.");
        if(!mock&&text.Contains("MOCK AUDIO:")) throw new InvalidDataException("Legacy provenance conflicts with REAL_AI.");
        var q=new AiVocalRequest { Lyrics=text.Substring(lyricsStart+9,lyricsEnd-lyricsStart-9),Bpm=Number(fields,"BPM"),
            Key=keyParts[0],Scale=keyParts[1],Voice=Field(fields,"Voice"),Role=Field(fields,"Role"),Bars=int.Parse(Field(fields,"Bars"),CultureInfo.InvariantCulture),
            Energy=(float)Number(fields,"Energy"),Emotion=(float)Number(fields,"Emotion"),Dry=bool.Parse(Field(fields,"Dry")),RelativeMinor=bool.Parse(Field(fields,"Relative minor flavor")),
            Style=Field(fields,"Style"),Character=Field(fields,"Character") };
        q.StrictLyrics=OptionalBool(text,"STRICT LYRICS / WORD PRESERVATION (REAL AI style only): ");
        q.SlowerPhraseDensity=OptionalBool(text,"SLOWER PHRASE DENSITY (REAL AI style only; project BPM unchanged): ");
        q.PronunciationGuidance=OptionalBlock(text,"Pronunciation / Accent Guidance (REAL AI style only):\n","AI Pronunciation Text (REAL AI sung words only; original Lyrics preserved above):\n");
        q.AiPronunciationText=OptionalBlock(text,"AI Pronunciation Text (REAL AI sung words only; original Lyrics preserved above):\n","STRICT LYRICS / WORD PRESERVATION (REAL AI style only): ");
        string expected="AI_VOCAL_"+q.Role.ToUpperInvariant().Replace("-","").Replace(" ","")+"_"+q.Bpm.ToString("0.##",CultureInfo.InvariantCulture)+"BPM_"+q.Key+q.Scale.ToUpperInvariant()+".wav";
        if(!string.Equals(Path.GetFileName(path),expected,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Legacy metadata does not identify this saved take's filename.");
        int takeCount=0; foreach(string wav in Directory.GetFiles(Path.GetDirectoryName(Path.GetFullPath(path)),"*.wav")) if(!Path.GetFileName(wav).StartsWith("AI_VOCAL_ALIGNED_",StringComparison.OrdinalIgnoreCase)) takeCount++;
        if(takeCount!=1) throw new InvalidDataException("Legacy VOCAL_INFO.txt is ambiguous among multiple takes. Use a matching per-WAV sidecar.");
        return new AiVocalSavedMetadata { Source=mock?"LOCAL_MOCK":"REAL_AI",IsMock=mock,Provider=provider,Request=q,Frames=frames };
    }
    static string Field(Dictionary<string,string> fields,string key) { string value; if(!fields.TryGetValue(key,out value)) throw new InvalidDataException("Missing legacy field: "+key); return value; }
    static double Number(Dictionary<string,string> fields,string key) { return double.Parse(Field(fields,key),CultureInfo.InvariantCulture); }
    static bool OptionalBool(string text,string label) { int i=text.IndexOf("\n"+label,StringComparison.Ordinal); if(i<0) return false; return bool.Parse(text.Substring(i+label.Length+1).Split('\n')[0]); }
    static string OptionalBlock(string text,string label,string next) { int i=text.IndexOf("\n"+label,StringComparison.Ordinal); if(i<0) return ""; i+=label.Length+1; int end=text.IndexOf("\n"+next,i,StringComparison.Ordinal); if(end<0) throw new InvalidDataException("Malformed optional vocal metadata."); return text.Substring(i,end-i); }
    public static string Pick(string current) {
#if UNITY_EDITOR
        return UnityEditor.EditorUtility.OpenFilePanel("Load saved isolated vocal WAV","","wav");
#elif UNITY_STANDALONE_WIN
        var d=new Dialog(); d.size=System.Runtime.InteropServices.Marshal.SizeOf(d); d.filter="WAV\0*.wav\0\0";
        d.file=new StringBuilder(current,32768); d.maxFile=32768; d.flags=0x80000|8|0x800|0x1000; d.extension="wav";
        return Open(d)?d.file.ToString():"";
#else
        return current;
#endif
    }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    sealed class Dialog {
        public int size; public IntPtr owner,instance; public string filter,customFilter; public int maxCustomFilter,filterIndex;
        public StringBuilder file; public int maxFile; public string fileTitle; public int maxFileTitle; public string initialDir,title;
        public int flags; public short fileOffset,extensionOffset; public string extension; public IntPtr customData,hook; public string template;
        public IntPtr reserved; public int reservedInt,flagsEx;
    }
    [System.Runtime.InteropServices.DllImport("comdlg32.dll",EntryPoint="GetOpenFileNameW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool Open([System.Runtime.InteropServices.In,System.Runtime.InteropServices.Out] Dialog d);
#endif
}
