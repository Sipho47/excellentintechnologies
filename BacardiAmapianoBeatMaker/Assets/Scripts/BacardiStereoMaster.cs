using System;
using System.IO;

// Independent file/session DSP. Never receives stems or arrangement state.
public sealed class BacardiStereoMaster
{
    public sealed class Measurement {
        public double Integrated, ShortTerm, MaxShortTerm, TruePeak, Peak;
        public override string ToString() { return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "Integrated: {0:F2} LUFS | Short-term (last 3 s): {1:F2} LUFS | Max short-term: {2:F2} LUFS | True Peak: {3:F2} dBTP | Peak: {4:F2} dBFS",
            Integrated,ShortTerm,MaxShortTerm,TruePeak,Peak); }
    }
    float[] original, mastered;
    public string SourcePath { get; private set; }
    public int Rate { get; private set; }
    public bool Active { get; set; }
    public bool Loaded { get { return original!=null; } }
    public Measurement Before { get; private set; }
    public Measurement After { get; private set; }
    public double GainDb { get; private set; }
    public double MaxReductionDb { get; private set; }
    public bool TargetReached { get; private set; }
    public string MasterStatus { get; private set; }
    const double ExcessiveReductionDb=9;
    public float[] PlaybackCopy() { return (float[])(Active && mastered!=null ? mastered : original).Clone(); }
    public void Import(string path) {
        int rate; var pcm=ReadWav(path,out rate);
        SourcePath=Path.GetFullPath(path); Rate=rate; original=pcm; mastered=null;
        Before=After=null; Active=false; GainDb=MaxReductionDb=0; TargetReached=false; MasterStatus="Not mastered";
    }
    public Measurement Analyze() { if(!Loaded) throw new InvalidOperationException("Import a stereo WAV first."); return Before=Measure(original,Rate); }
    public static Measurement Measure(float[] pcm,int rate) {
        var meter=new BacardiLufsEngine(rate,new double[]{1,1});
        var peak=new BacardiTruePeak(rate,2); double samplePeak=0, maxS=double.NaN;
        for(int n=0;n<pcm.Length;n+=2048) {
            var block=new float[Math.Min(2048,pcm.Length-n)]; Array.Copy(pcm,n,block,0,block.Length);
            foreach(float x in block) samplePeak=Math.Max(samplePeak,Math.Abs(x));
            meter.Push(block,2); meter.Refresh(); peak.Observe(block,2);
            if(!double.IsNaN(meter.ShortTerm)) maxS=double.IsNaN(maxS)?meter.ShortTerm:Math.Max(maxS,meter.ShortTerm);
        }
        peak.Observe(new float[64],2); // flush reconstruction tail, not loudness window
        return new Measurement {Integrated=meter.Integrated,ShortTerm=meter.ShortTerm,MaxShortTerm=maxS,
            TruePeak=Db(peak.Peak),Peak=Db(samplePeak)};
    }
    static double Db(double x) { return x>0?20*Math.Log10(x):double.NegativeInfinity; }
    public void SetSpotify(double ceiling=-1) {
        if(double.IsNaN(ceiling)||double.IsInfinity(ceiling)||ceiling>0||ceiling< -12) throw new ArgumentException("Ceiling must be between -12 and 0 dBTP.");
        Analyze();
        if(double.IsNaN(Before.Integrated)||Before.Integrated< -50) throw new InvalidOperationException("Source is silent or below -50 LUFS.");
        double gain=Math.Max(-60,Math.Min(18,-14-Before.Integrated));
        mastered=null; After=null; Active=false;
        TargetReached=false; MasterStatus="Target not reached";
        for(int pass=0;pass<12;pass++) {
            var padded=new float[original.Length+BacardiTruePeak.DelayFrames*2]; Array.Copy(original,padded,original.Length);
            // Loudness gain precedes the existing stereo-linked true-peak limiter.
            var limiter=new BacardiTruePeak(Rate,2,ceiling-.1);
            limiter.Process(padded,2,Math.Pow(10,gain/20),true);
            var candidate=new float[original.Length]; Array.Copy(padded,BacardiTruePeak.DelayFrames*2,candidate,0,candidate.Length);
            var measured=Measure(candidate,Rate);
            double trimDb=0;
            // Verify actual reconstructed output, including envelope modulation.
            if(measured.TruePeak>ceiling-.02) {
                trimDb=ceiling-.02-measured.TruePeak;
                double trim=Math.Pow(10,trimDb/20);
                for(int i=0;i<candidate.Length;i++) candidate[i]*=(float)trim;
                measured=Measure(candidate,Rate);
            }
            double reduction=limiter.MaxReductionDb;
            if(reduction>ExcessiveReductionDb) {
                if(pass==0) { mastered=candidate; After=measured; GainDb=gain; MaxReductionDb=reduction; }
                MasterStatus="Target not reached: excessive limiting would be required (over "+ExcessiveReductionDb+" dB peak reduction).";
                break;
            }
            mastered=candidate; After=measured; GainDb=gain; MaxReductionDb=reduction;
            double error=-14-After.Integrated;
            if(Math.Abs(error)<=.15) { TargetReached=true; MasterStatus="Target reached"; break; }
            double next=Math.Max(-60,Math.Min(18,gain+error));
            if(Math.Abs(next-gain)<.01) { MasterStatus="Target not reached: loudness gain limit reached."; break; }
            gain=next;
            MasterStatus="Target not reached after full-song gain/limiter iterations.";
        }
        if(After==null || After.TruePeak>ceiling) { Active=false; throw new InvalidOperationException("True peak verification failed."); }
        Active=true;
    }
    public void Export(string path) {
        if(mastered==null) throw new InvalidOperationException("Set Spotify master first.");
        if(string.Equals(Path.GetFullPath(path),SourcePath,StringComparison.OrdinalIgnoreCase)) throw new IOException("The source WAV cannot be overwritten.");
        // CreateNew also blocks aliases/hard links and accidental overwrites of any existing file.
        WriteWav(path,mastered,Rate);
    }
    public static void WriteWav(string path,float[] pcm,int rate) {
        using(var w=new BinaryWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write))) {
            int size=checked(pcm.Length*3);
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(size+36); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate*6); w.Write((short)6); w.Write((short)24);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(size);
            foreach(float x in pcm) { int v=(int)Math.Round(Math.Max(-1,Math.Min(1,x))*8388607); w.Write((byte)v); w.Write((byte)(v>>8)); w.Write((byte)(v>>16)); }
        }
    }
    public static float[] ReadWav(string path,out int rate) {
        using(var r=new BinaryReader(File.OpenRead(path))) {
            if(new string(r.ReadChars(4))!="RIFF") throw new InvalidDataException("Expected RIFF WAV.");
            r.ReadUInt32(); if(new string(r.ReadChars(4))!="WAVE") throw new InvalidDataException("Expected WAV.");
            int format=0,channels=0,bits=0,align=0; rate=0; byte[] data=null;
            while(r.BaseStream.Position+8<=r.BaseStream.Length) {
                string id=new string(r.ReadChars(4)); uint size=r.ReadUInt32(); long end=r.BaseStream.Position+size;
                if(end>r.BaseStream.Length) throw new InvalidDataException("Truncated WAV chunk.");
                if(id=="fmt ") {
                    if(size<16) throw new InvalidDataException("Invalid WAV format.");
                    format=r.ReadUInt16(); channels=r.ReadUInt16(); rate=r.ReadInt32(); r.ReadInt32(); align=r.ReadUInt16(); bits=r.ReadUInt16();
                    if(format==65534) {
                        if(size<40||r.ReadUInt16()<22) throw new InvalidDataException("Invalid extensible WAV.");
                        int valid=r.ReadUInt16(); r.ReadUInt32(); byte[] guid=r.ReadBytes(16);
                        var sub=new Guid(guid);
                        if(sub==new Guid("00000001-0000-0010-8000-00aa00389b71")) format=1;
                        else if(sub==new Guid("00000003-0000-0010-8000-00aa00389b71")) format=3;
                        if(valid!=bits) throw new InvalidDataException("Packed valid-bit WAV is unsupported; export standard PCM.");
                    }
                } else if(id=="data") { if(size>int.MaxValue) throw new InvalidDataException("WAV too large."); data=r.ReadBytes((int)size); }
                r.BaseStream.Position=end+(size%2);
            }
            if(channels!=2) throw new InvalidDataException("Import requires a finished two-channel stereo mix.");
            if(rate<8000||rate>192000||data==null||data.Length==0||
               !((format==1&&(bits==16||bits==24||bits==32))||(format==3&&bits==32))||align!=bits/8*2||data.Length%align!=0) throw new InvalidDataException("Supported WAV: stereo PCM 16/24/32-bit or float32, 8–192 kHz.");
            var pcm=new float[data.Length/(bits/8)];
            for(int i=0,p=0;i<pcm.Length;i++,p+=bits/8) {
                if(format==3) pcm[i]=BitConverter.ToSingle(data,p);
                else if(bits==16) pcm[i]=BitConverter.ToInt16(data,p)/32768f;
                else if(bits==32) pcm[i]=(float)(BitConverter.ToInt32(data,p)/2147483648.0);
                else { int v=data[p]|data[p+1]<<8|data[p+2]<<16; if((v&0x800000)!=0) v|=unchecked((int)0xff000000); pcm[i]=v/8388608f; }
                if(float.IsNaN(pcm[i])||float.IsInfinity(pcm[i])) throw new InvalidDataException("Non-finite WAV samples.");
            }
            return pcm;
        }
    }
}
