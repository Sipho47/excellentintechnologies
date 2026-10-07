using System.Globalization;
using UnityEngine;

[RequireComponent(typeof(AudioListener))]
public sealed class BacardiMasterLufsMeter : MonoBehaviour
{
    sealed class Session {
        public volatile Measurements measurements;
        public BacardiMasterComparison comparison;
        public int channels, sampleRate; public double[] weights;
    }
    sealed class Measurements {
        public BacardiLufsEngine engine;
        public BacardiTruePeak peakMeter;
    }
    sealed class Capture {
        public float[] pcm;
        public double start, analyzed, maxReduction;
        public int written;
        public volatile bool complete;
    }
    volatile Capture capture;
    System.Threading.Tasks.Task<string> pendingReport;
    public string LoopReport { get; private set; } = "";
    public void CancelLoopCheck() { capture=null; pendingReport=null; LoopReport=""; }
    public void BeginLoopCheck(double startDspTime, double seconds, double analyzedLoop) {
        var current=session;
        if(current==null) return;
        pendingReport=null;
        LoopReport="Measuring fresh 16-bar listener output...";
        capture=new Capture { start=startDspTime, analyzed=analyzedLoop,
            pcm=new float[(int)System.Math.Round(seconds*current.sampleRate)*current.channels] };
    }
    Measurements NewMeasurements(Session current) {
        return new Measurements { engine=new BacardiLufsEngine(current.sampleRate,current.weights),
            peakMeter=new BacardiTruePeak(current.sampleRate,current.channels) };
    }
    volatile Session session;
    public volatile bool PostArrangementPlayback;
    int ownerId, uiId, analysisId;
    double analyzedGainDb;
    public double AnalyzedLoopTruePeakDb { get; private set; }
    public double AnalyzedLoopMaxReductionDb { get; private set; }
    long processedBlocks;
    public string RuntimeTelemetry { get; private set; } = "Waiting for listener PCM";
    public void BindUI(int id) { uiId=id; }
    public void RecordAnalysis(BacardiMastering.Result result) {
        double gainDb=result.GainDb;
        analysisId=GetInstanceID(); analyzedGainDb=gainDb;
        AnalyzedLoopTruePeakDb=result.ReferenceTruePeak;
        AnalyzedLoopMaxReductionDb=result.ReferenceMaxReductionDb;
        Debug.Log("[Master routing] Analysis meter="+analysisId+" UI="+uiId+" gain="+gainDb.ToString("F6",CultureInfo.InvariantCulture)+" dB",this);
    }
    float nextRefresh;
    volatile bool targetEnabled;
    volatile float masterGain=1, dryGain=1; volatile bool bypass;

    public void SetTarget(bool enabled,double gainDb) {
        masterGain=(float)System.Math.Pow(10,System.Math.Max(-60,System.Math.Min(18,gainDb))/20);
        targetEnabled=enabled;
    }
    public void SetBypass(bool value) { bypass=value; }
    public void SetDrySafety(double peakDb) { dryGain=(float)System.Math.Min(1,System.Math.Pow(10,(-1.3-peakDb)/20)); }
    public void ResetMeasurements() {
        var current=session;
        if(current==null) return;
        current.measurements=NewMeasurements(current);
        CancelLoopCheck();
        Readout="LIVE LUFS-S: -- | LIVE LUFS-I: -- | True Peak: -- dBTP";
        nextRefresh=0;
    }
    public string GainReductionReadout { get; private set; } = "GR: 0.0 dB";
    public string Readout { get; private set; } = "LIVE LUFS-S: -- | LIVE LUFS-I: -- | True Peak: -- dBTP";

    public void ResetSession() {
        ownerId=GetInstanceID(); processedBlocks=0;
        // Unity interleaving: L, R, C, LFE, rear L/R, side L/R.
        double[] weights;
        switch (AudioSettings.speakerMode) {
            case AudioSpeakerMode.Mono: weights=new double[] {1}; break;
            case AudioSpeakerMode.Quad: weights=new double[] {1,1,1.41,1.41}; break;
            case AudioSpeakerMode.Surround: weights=new double[] {1,1,1,1.41,1.41}; break;
            case AudioSpeakerMode.Mode5point1: weights=new double[] {1,1,1,0,1.41,1.41}; break;
            case AudioSpeakerMode.Mode7point1: weights=new double[] {1,1,1,0,1.41,1.41,1.41,1.41}; break;
            default: weights=new double[] {1,1}; break;
        }
        // Unity's outputSampleRate getter is main-thread-only. Publish the
        // rate with the session so audio callbacks and report workers use it safely.
        int rate=AudioSettings.outputSampleRate;
        var fresh=new Session {
            channels=weights.Length, sampleRate=rate, weights=weights,
            comparison=new BacardiMasterComparison(rate,weights.Length),
        };
        fresh.measurements=NewMeasurements(fresh);
        fresh.comparison.Initialize(targetEnabled,bypass,masterGain,dryGain);
        session=fresh;
        CancelLoopCheck();
        Readout="LIVE LUFS-S: -- | LIVE LUFS-I: -- | True Peak: -- dBTP";
        GainReductionReadout="GR: 0.0 dB";
        nextRefresh=0;
    }
    void OnEnable() { ResetSession(); AudioSettings.OnAudioConfigurationChanged+=ConfigurationChanged; }
    void OnDisable() { AudioSettings.OnAudioConfigurationChanged-=ConfigurationChanged; session=null; }
    void ConfigurationChanged(bool changed) { ResetSession(); }
    void OnAudioFilterRead(float[] data, int channels) {
        var current=session;
        if(current==null || current.channels!=channels) return;
        // This is the audible listener buffer, after both branch limiters,
        // target/safety gains and crossfade. Observers never change this PCM.
        if(!PostArrangementPlayback) current.comparison.Process(data,channels,targetEnabled,bypass,masterGain,dryGain);
        System.Threading.Interlocked.Increment(ref processedBlocks);
        var measurements=current.measurements;
        measurements.peakMeter.Observe(data,channels);
        measurements.engine.Push(data,channels);
        var check=capture;
        if(check==null || check.complete) return;
        int offset=(int)System.Math.Min(data.Length/channels,System.Math.Max(0,
            System.Math.Round((check.start-AudioSettings.dspTime)*current.sampleRate)))*channels;
        int count=System.Math.Min(data.Length-offset,check.pcm.Length-check.written);
        System.Array.Copy(data,offset,check.pcm,check.written,count);
        check.written+=count;
        if(count>0) check.maxReduction=current.comparison.MaxAudibleReductionDb;
        if(check.written==check.pcm.Length) check.complete=true;
    }
    static string Format(double value) {
        return double.IsNaN(value) || double.IsInfinity(value) ? "--" : value.ToString("F1",CultureInfo.InvariantCulture);
    }
    void Update() {
        if(Time.unscaledTime<nextRefresh) return;
        nextRefresh=Time.unscaledTime+.1f;
        var current=session;
        if(current==null) return;
        var measurements=current.measurements;
        measurements.engine.Refresh();
        var processor=current.comparison;
        RuntimeTelemetry=string.Format(CultureInfo.InvariantCulture,
            "Analyzed Gain dB: {0:F3} | Live Gain dB: {1:F3}\nAnalyzed Gain Linear: {2:F6} | Live Gain Linear: {3:F6}\nMastering Processor Active: {4} | Bypass Mix: {5:F3}\nLimiter Input Peak: {6:F6} | Limiter Output Peak (sample): {7:F6}\nCurrent GR: {8:F3} | Maximum GR: {9:F3}\nInstance IDs: analysis meter={10}, live meter={11}, UI owner={12}, processor={13} | PCM blocks: {14}",
            analyzedGainDb,20*System.Math.Log10(processor.LiveGainLinear),System.Math.Pow(10,analyzedGainDb/20),processor.LiveGainLinear,
            targetEnabled&&!bypass&&processor.WetMix>.999&&System.Threading.Interlocked.Read(ref processedBlocks)>0 ? "YES" : "NO",
            1-processor.WetMix,processor.LimiterInputPeak,processor.LimiterOutputPeak,processor.CurrentReductionDb,processor.MaxReductionDb,
            analysisId,ownerId,uiId,System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(processor),System.Threading.Interlocked.Read(ref processedBlocks));
        GainReductionReadout="GR: " + Format(current.comparison.CurrentReductionDb) + " dB";
        Readout="LIVE LUFS-S: " + Format(measurements.engine.ShortTerm) + " | LIVE LUFS-I: " + Format(measurements.engine.Integrated) + " | True Peak: " + Format(20*System.Math.Log10(measurements.peakMeter.Peak)) + " dBTP";
        var check=capture;
        if(check!=null && check.complete) {
            int rate=current.sampleRate;
            capture=null;
            // Full-window reconstruction must not stall the playback scheduler.
            pendingReport=System.Threading.Tasks.Task.Run(()=>BuildReport(check,rate,current));
        }
        if(pendingReport!=null && pendingReport.IsCompleted) {
            if(pendingReport.IsFaulted) {
                LoopReport="Live loop check failed: " + pendingReport.Exception.GetBaseException().Message;
                Debug.LogError(LoopReport,this);
            } else {
                LoopReport=pendingReport.Result;
                Debug.Log("[Live meter] " + LoopReport+"\n"+RuntimeTelemetry,this);
                if(Application.isEditor) System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath,"../Tools/LufsValidation/Live-LUFS-16-bars.txt"),LoopReport+"\n"+RuntimeTelemetry);
            }
            pendingReport=null;
        }
    }
    static string BuildReport(Capture check,int rate,Session current) {
            var engine=new BacardiLufsEngine(rate,current.weights);
            var peak=new BacardiTruePeak(rate,current.channels);
            // Drain in blocks to keep the SPSC queue within capacity.
            var block=new float[1024*current.channels];
            for(int n=0;n<check.pcm.Length;n+=block.Length) {
                int length=System.Math.Min(block.Length,check.pcm.Length-n);
                if(length!=block.Length) block=new float[length];
                System.Array.Copy(check.pcm,n,block,0,length);
                engine.Push(block,current.channels); engine.Refresh(); peak.Observe(block,current.channels);
            }
            return "16 bars / LIVE LUFS-I: " + Format(engine.Integrated) +
                " | ANALYZED LOOP LUFS-I: " + Format(check.analyzed) +
                " | Difference: " + Format(engine.Integrated-check.analyzed) + " LU" +
                " | TP: " + Format(20*System.Math.Log10(peak.Peak)) + " dBTP" +
                " | Max GR: " + Format(check.maxReduction) + " dB" +
                string.Format(CultureInfo.InvariantCulture,"\nCapture precision: LIVE={0:F6}, ANALYZED={1:F6}, DIFFERENCE={2:F6}, TP={3:F6}, MAX_GR={4:F6}",
                    engine.Integrated,check.analyzed,engine.Integrated-check.analyzed,20*System.Math.Log10(peak.Peak),check.maxReduction);
    }
}







