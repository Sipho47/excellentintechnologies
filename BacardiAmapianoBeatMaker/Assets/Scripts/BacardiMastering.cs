using System;

// 8x, 32-tap windowed-sinc reconstruction. The limiter uses the absolute
// reconstructed peak (rather than a sample clamp) to protect intersample peaks
// with lookahead and a stereo-linked, 120 ms release envelope.
public sealed class BacardiTruePeak {
    const int Taps=32;
    static readonly double[][] Kernel=MakeKernel();
    readonly double[][] history;
    readonly double[] bounds=new double[Taps];
    readonly double release;
    readonly double ceiling;
    int position;
    double envelope=1;
    public double Peak { get; private set; }
    public double InputPeak { get; private set; }
    public double OutputPeak { get; private set; }
    public double MaxReductionDb { get; private set; }
    // Read-only telemetry from the existing envelope; does not affect processing.
    public double CurrentReductionDb { get { return -20*Math.Log10(Math.Max(1e-20,envelope)); } }
    public const int DelayFrames=Taps-1;
    public BacardiTruePeak(int rate,int channels,double ceilingDb=-1.3) {
        ceiling=Math.Pow(10,ceilingDb/20);
        history=new double[channels][]; for(int c=0;c<channels;c++) history[c]=new double[Taps*2]; release=Math.Exp(-1.0/(rate*.12));
    }
    static double[][] MakeKernel() {
        var result=new double[8][];
        for(int p=0;p<8;p++) {
            result[p]=new double[Taps]; double sum=0;
            for(int t=0;t<Taps;t++) {
                double x=t-15-p/8.0;
                double sinc=Math.Abs(x)<1e-12 ? 1 : Math.Sin(Math.PI*x)/(Math.PI*x);
                double window=.42+.5*Math.Cos(Math.PI*x/16)+.08*Math.Cos(2*Math.PI*x/16);
                result[p][t]=sinc*window; sum+=result[p][t];
            }
            for(int t=0;t<Taps;t++) result[p][t]/=sum;
        }
        return result;
    }
    // No allocations, locks or Unity calls on the audio thread.
    public void Process(float[] data,int channels,double gain,bool limit) {
        ProcessRange(data,channels,gain,limit,0,data.Length);
    }
    public void ProcessRange(float[] data,int channels,double gain,bool limit,int offset,int length) {
        for(int i=offset;i<offset+length;i+=channels) {
            double bound=0;
            for(int c=0;c<channels;c++) {
                var channel=history[c];
                double x=data[i+c]*gain;
                channel[position]=channel[position+Taps]=double.IsNaN(x)||double.IsInfinity(x) ? 0 : x;
                bound=Math.Max(bound,Math.Abs(channel[position]));
                for(int p=0;p<8;p++) {
                    double b=0;
                    for(int t=0;t<Taps;t++) b+=channel[position+1+t]*Kernel[p][t];
                    bound=Math.Max(bound,Math.Abs(b));
                }
            }
            bounds[position]=bound;
            InputPeak=Math.Max(InputPeak,bound);
            double maximum=0;
            for(int t=0;t<Taps;t++) maximum=Math.Max(maximum,bounds[t]);
            double desired=maximum>ceiling ? ceiling/maximum : 1;
            envelope=Math.Min(desired,1-(1-envelope)*release);
            if(limit) MaxReductionDb=Math.Max(MaxReductionDb,-20*Math.Log10(Math.Max(1e-20,envelope)));
            if(limit) for(int c=0;c<channels;c++) {
                data[i+c]=(float)(history[c][position+1]*envelope);
                OutputPeak=Math.Max(OutputPeak,Math.Abs(data[i+c]));
            }
            position=(position+1)%Taps;
        }
    }
    // Separate post-processing detector, including the limiter output.
    public void Observe(float[] data,int channels) {
        for(int i=0;i<data.Length;i+=channels) {
            for(int c=0;c<channels;c++) {
                var channel=history[c]; channel[position]=channel[position+Taps]=data[i+c]; Peak=Math.Max(Peak,Math.Abs(data[i+c]));
                for(int p=0;p<8;p++) {
                    double v=0;
                    for(int t=0;t<Taps;t++) v+=channel[position+1+t]*Kernel[p][t];
                    Peak=Math.Max(Peak,Math.Abs(v));
                }
            }
            position=(position+1)%Taps;
        }
    }
}

public static class BacardiMastering {
    public sealed class Result {
        public float[] Audio, ReferenceAudio;
        public double InputLufs, Integrated, TruePeak, GainDb, InputTruePeak, MaxReductionDb, ReferenceIntegrated, ReferenceTruePeak, ReferenceMaxReductionDb, ReferenceInputTruePeak;
        public string Summary { get { return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "TARGET: -14 LUFS | RESULT: {0:F1} LUFS-I | TP: {1:F1} dBTP | Gain: {2:+0.0;-0.0;0.0} dB",Integrated,TruePeak,GainDb) + (Math.Abs(Integrated+14)>.2 ? " / TP LIMITED: transient protection" : ""); } }
    }
    public static double Measure(float[] pcm,int rate) {
        var meter=new BacardiLufsEngine(rate,new double[]{1,1});
        var block=new float[2048];
        for(int i=0;i<pcm.Length;i+=block.Length) {
            int count=Math.Min(block.Length,pcm.Length-i);
            if(count!=block.Length) block=new float[count];
            Array.Copy(pcm,i,block,0,count); meter.Push(block,2); meter.Refresh();
        }
        return meter.Integrated;
    }
    public const double MaxLimiterReductionDb=3;
    public static float[] LoopReference(float[] oneBar,int barFrames,int repeats=16) {
        if(barFrames<=0 || repeats<1) throw new ArgumentException("Invalid loop duration.");
        // Long custom sample tails must reach steady overlap before we set gain.
        repeats=Math.Max(repeats,(int)Math.Ceiling(oneBar.Length/(2.0*barFrames))+4);
        var result=new float[checked(oneBar.Length+barFrames*2*(repeats-1))];
        for(int repeat=0;repeat<repeats;repeat++)
            for(int n=0;n<oneBar.Length;n++) result[repeat*barFrames*2+n]+=oneBar[n];
        return result;
    }
    // New engines on every call: measurement, lookahead, reconstruction, envelope
    // and maximum-reduction state are never carried over from a previous render.
    public static Result ApplyGain(float[] input,int rate,double gainDb) {
        var audio=new float[input.Length+BacardiTruePeak.DelayFrames*2];
        Array.Copy(input,audio,input.Length);
        var limiter=new BacardiTruePeak(rate,2);
        limiter.Process(audio,2,Math.Pow(10,gainDb/20),true);
        var aligned=new float[input.Length];
        Array.Copy(audio,BacardiTruePeak.DelayFrames*2,aligned,0,aligned.Length);
        var detector=new BacardiTruePeak(rate,2); detector.Observe(aligned,2);
        return new Result { Audio=aligned, Integrated=Measure(aligned,rate),
            TruePeak=20*Math.Log10(Math.Max(1e-20,detector.Peak)),
            GainDb=gainDb, MaxReductionDb=limiter.MaxReductionDb };
    }
    static double PeakDb(float[] pcm,int rate) {
        var detector=new BacardiTruePeak(rate,2); detector.Observe(pcm,2);
        return 20*Math.Log10(Math.Max(1e-20,detector.Peak));
    }
    public static Result Render(float[] input,int rate,float[] playbackReference=null) {
        foreach(float sample in input) if(float.IsNaN(sample)||float.IsInfinity(sample)) throw new Exception("Master input contains invalid audio.");
        var reference=playbackReference ?? input;
        foreach(float sample in reference) if(float.IsNaN(sample)||float.IsInfinity(sample)) throw new Exception("Playback reference contains invalid audio.");
        double measured=Measure(input,rate), referenceLufs=Measure(reference,rate);
        if(double.IsNaN(measured)||double.IsInfinity(measured)||measured < -50 || double.IsNaN(referenceLufs))
            throw new Exception("Arrangement is silent or too quiet to target safely (minimum -50 LUFS). Raise MASTER or sample levels.");
        double inputPeak=PeakDb(input,rate), referencePeak=PeakDb(reference,rate);
        // Include overlapping tails. Never use live meter text to calculate gain.
        double maxGain=Math.Min(18,-1.3-Math.Max(inputPeak,referencePeak)+MaxLimiterReductionDb-.02);
        double gain=Math.Max(-60,Math.Min(maxGain,-14-Math.Max(measured,referenceLufs)));
        Result result=null, referenceResult=null;
        for(int pass=0;pass<4;pass++) {
            result=ApplyGain(input,rate,gain);
            referenceResult=playbackReference==null ? result : ApplyGain(reference,rate,gain);
            double reduction=Math.Max(result.MaxReductionDb,referenceResult.MaxReductionDb);
            if(reduction>MaxLimiterReductionDb) {
                gain-=reduction-MaxLimiterReductionDb+.02;
                continue;
            }
            double loudest=Math.Max(result.Integrated,referenceResult.Integrated);
            if(Math.Abs(loudest+14)<=.1) break;
            double next=Math.Max(-60,Math.Min(maxGain,gain-14-loudest));
            if(Math.Abs(next-gain)<.01) break;
            gain=next;
        }
        if(Math.Max(result.MaxReductionDb,referenceResult.MaxReductionDb)>MaxLimiterReductionDb+.001)
            throw new Exception("Master transient-protection verification failed.");
        result.InputLufs=measured; result.InputTruePeak=inputPeak;
        result.ReferenceAudio=referenceResult.Audio; result.ReferenceInputTruePeak=referencePeak;
        result.ReferenceIntegrated=referenceResult.Integrated;
        result.ReferenceTruePeak=referenceResult.TruePeak;
        result.ReferenceMaxReductionDb=referenceResult.MaxReductionDb;
        return result;
    }
}




