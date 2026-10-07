using System;

// Both branches have identical lookahead delay. A 50 ms linear crossfade avoids
// phase discontinuities; both remain warm while switching. Safety attenuation
// on the dry branch protects an original master that already exceeds the ceiling.
public sealed class BacardiMasterComparison {
    readonly BacardiTruePeak dryLimiter, wetLimiter;
    readonly float[] dry=new float[8], wet=new float[8];
    readonly double increment;
    double blend, currentGain=1, currentDryGain=1;
    public double WetMix { get { return blend; } }
    public double LiveGainLinear { get { return currentGain; } }
    public double LimiterInputPeak { get { return wetLimiter.InputPeak; } }
    public double LimiterOutputPeak { get { return wetLimiter.OutputPeak; } }
    // During the crossfade display the greater active-branch reduction.
    public double CurrentReductionDb { get {
        if(blend<=0) return dryLimiter.CurrentReductionDb;
        if(blend>=1) return wetLimiter.CurrentReductionDb;
        return Math.Max(dryLimiter.CurrentReductionDb,wetLimiter.CurrentReductionDb);
    } }
    public double MaxReductionDb { get { return wetLimiter.MaxReductionDb; } }
    public double MaxAudibleReductionDb { get; private set; }
    public BacardiMasterComparison(int rate,int channels) {
        dryLimiter=new BacardiTruePeak(rate,channels); wetLimiter=new BacardiTruePeak(rate,channels);
        increment=1.0/(rate*.05);
    }
    public void Initialize(bool enabled,bool bypass,double gain,double dryGain) {
        // Called only on a freshly constructed session, before its first PCM.
        blend=enabled&&!bypass ? 1 : 0; currentGain=gain; currentDryGain=dryGain;
    }
    public void Process(float[] data,int channels,bool enabled,bool bypass,double gain,double dryGain) {
        if(channels>dry.Length) return; // Unity supports at most 7.1 output.
        // Process frame by frame through fixed buffers via the limiter's offset API.
        double target=enabled&&!bypass ? 1 : 0;
        for(int i=0;i<data.Length;i+=channels) {
            blend += Math.Max(-increment,Math.Min(increment,target-blend));
            // Smooth gain edits without changing the fixed settled target.
            double step=Math.Max(1,gain)*increment;
            currentGain += Math.Max(-step,Math.Min(step,gain-currentGain));
            currentDryGain += Math.Max(-increment,Math.Min(increment,dryGain-currentDryGain));
            for(int c=0;c<channels;c++) dry[c]=wet[c]=data[i+c];
            dryLimiter.ProcessRange(dry,channels,currentDryGain,true,0,channels);
            wetLimiter.ProcessRange(wet,channels,currentGain,true,0,channels);
            for(int c=0;c<channels;c++) data[i+c]=(float)(dry[c]*(1-blend)+wet[c]*blend);
            MaxAudibleReductionDb=Math.Max(MaxAudibleReductionDb,CurrentReductionDb);
        }
    }
}





