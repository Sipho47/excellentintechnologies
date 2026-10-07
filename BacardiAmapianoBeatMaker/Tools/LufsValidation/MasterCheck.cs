using System;
using System.IO;
public class MasterCheck {
 const int Rate=44100,DefaultVelocity=127;
 readonly bool[,] steps=new bool[25,16]; readonly int[,] velocities=new int[25,16]; readonly int[,] noteOffsets=new int[25,16];
 readonly float[][] sounds=new float[25][]; readonly Clip[] clips=new Clip[25];
 float bpm=104,swing=.16f,volume=.65f;
 static string[] Names=new string[25];
 class Clip { public int channels=1,frequency=Rate,samples; public float length; public bool GetData(float[] d,int n) { return false; } }
 float StepPitch(int i,int s) { return i<8 ? Mathf.Pow(2,noteOffsets[i,s]/12f) : 1; }
 double Duration(int s) { return 60.0/bpm/4*(s%2==0?1+swing:1-swing); }
 public static void Main(string[] args) {
  System.Threading.Thread.CurrentThread.CurrentCulture=System.Globalization.CultureInfo.InvariantCulture;
  var app=new MasterCheck();
  for(int i=0;i<25;i++) { app.sounds[i]=Synthesize(i); app.clips[i]=new Clip{samples=app.sounds[i].Length,length=app.sounds[i].Length/(float)Rate}; }
  if(BacardiMastering.LoopReference(new float[400],10).Length!=860) throw new Exception("Long-tail reference failed");
  if(args.Length>0 && args[0]=="--all-sounds") {
   app.steps[0,3]=true; app.velocities[0,3]=91; app.noteOffsets[0,3]=7;
   app.EnsureAllSoundsUsed();
   if(!app.steps[0,3] || app.velocities[0,3]!=91 || app.noteOffsets[0,3]!=7) throw new Exception("Existing note changed");
   var allMix=app.ArrangementMix();
   for(int i=0;i<25;i++) {
    if(!app.HasSteps(i) || !HasAudiblePcm(app.RenderSound(i,allMix.Length/2))) throw new Exception("Silent stem: "+i);
   }
   Console.WriteLine("All 25 sparse-pattern stems contain audio; existing note, pitch and velocity preserved.");
   return;
  }
  app.Demo(); var input=app.ArrangementMix();
  if(args.Length>0 && args[0]=="--balanced-stems") {
   var sum=new double[input.Length];
   float stemPeak=0;
   var tracks=new float[25][];
   for(int i=0;i<25;i++) {
    tracks[i]=app.RenderSound(i,input.Length/2);
    foreach(float x in tracks[i]) stemPeak=Math.Max(stemPeak,Math.Abs(x));
   }
   float gain=stemPeak>1 ? 1/stemPeak : 1;
   for(int i=0;i<25;i++) {
    string path="Tools/LufsValidation/Balanced-Stem-"+(i+1).ToString("00")+".wav";
    WriteWav(path,tracks[i],gain);
    using(var reader=new BinaryReader(File.OpenRead(path))) {
     reader.BaseStream.Position=44;
     if(reader.BaseStream.Length!=44+input.Length*3) throw new Exception("Stem length/alignment changed");
     for(int n=0;n<input.Length;n++) {
      int value=reader.ReadByte() | reader.ReadByte()<<8 | reader.ReadByte()<<16;
      if((value&0x800000)!=0) value|=unchecked((int)0xff000000);
      sum[n]+=value/8388607.0;
     }
    }
   }
   double max=0,squared=0;
   for(int n=0;n<input.Length;n++) {
    double error=sum[n]-input[n]*gain;
    max=Math.Max(max,Math.Abs(error)); squared+=error*error;
   }
   // Account for 25 independently rounded 24-bit PCM streams and float summation.
   if(max>4e-6) throw new Exception("Balanced stem reconstruction failed: "+max);
   string report=string.Format("25 unity-gain decoded 24-bit stems versus unmastered mix\nCommon gain: {0:R}\nMaximum absolute error: {1:E6}\nRMS error: {2:E6}\nEqual stereo file lengths and alignment verified. No normalization or limiter.\n",gain,max,Math.Sqrt(squared/input.Length));
   File.WriteAllText("Tools/LufsValidation/Balanced-stem-reconstruction.txt",report);
   Console.WriteLine(report);
   return;
  }
  if(args.Length>0 && args[0]=="--stems") {
   using(var report=new StreamWriter("Tools/LufsValidation/Individual-stems.csv")) {
    report.WriteLine("Stem,LUFS-I,TP-dBTP,Gain-dB,Max-GR-dB,TargetReached");
    for(int i=0;i<25;i++) {
     var track=app.RenderSound(i,input.Length/2);
     var result=BacardiMastering.Render(track,Rate);
     if(result.Audio.Length!=track.Length || double.IsNaN(result.Integrated) || result.TruePeak> -1 || result.MaxReductionDb>3.001) throw new Exception("Stem safety/length failed: "+i);
     WriteWav("Tools/LufsValidation/Stem-"+(i+1).ToString("00")+".wav",result.Audio,1);
     report.WriteLine("{0},{1:F6},{2:F6},{3:F6},{4:F6},{5}",i+1,result.Integrated,result.TruePeak,result.GainDb,result.MaxReductionDb,Math.Abs(result.Integrated+14)<=.2);
     Console.WriteLine("Stem {0}: {1:F2} LUFS-I / {2:F2} dBTP",i+1,result.Integrated,result.TruePeak);
    }
   }
   return;
  }
  int bar=(int)Math.Round(60.0/app.bpm*4*Rate);
  var reference=BacardiMastering.LoopReference(input,bar);
  BacardiMastering.Result first=null;
  using(var report=new StreamWriter("Tools/LufsValidation/Deterministic-runs.csv")) {
   report.WriteLine("Run,Program,LUFS-I,TP-dBTP,Gain-dB,Max-GR-dB");
   for(int run=1;run<=3;run++) {
    // Render creates fresh integration, FIR, envelope and lookahead state every time.
    var result=BacardiMastering.Render(input,Rate,reference);
    if(result.MaxReductionDb>3.001 || result.ReferenceMaxReductionDb>3.001) throw new Exception("Reduction budget exceeded");
    if(first!=null) {
     if(result.GainDb!=first.GainDb || result.Integrated!=first.Integrated || result.TruePeak!=first.TruePeak || result.MaxReductionDb!=first.MaxReductionDb || result.ReferenceIntegrated!=first.ReferenceIntegrated || result.ReferenceTruePeak!=first.ReferenceTruePeak || result.ReferenceMaxReductionDb!=first.ReferenceMaxReductionDb) throw new Exception("Analysis is nondeterministic");
     for(int n=0;n<result.Audio.Length;n++) if(result.Audio[n]!=first.Audio[n]) throw new Exception("Export PCM differs");
     for(int n=0;n<result.ReferenceAudio.Length;n++) if(result.ReferenceAudio[n]!=first.ReferenceAudio[n]) throw new Exception("Loop PCM differs");
    } else first=result;
    WriteWav("Tools/LufsValidation/Safe Run "+run+" Master.wav",result.Audio,1);
    WriteWav("Tools/LufsValidation/Loop Run "+run+" Master.wav",result.ReferenceAudio,1);
    report.WriteLine("{0},finite,{1:F6},{2:F6},{3:F6},{4:F6}",run,result.Integrated,result.TruePeak,result.GainDb,result.MaxReductionDb);
    report.WriteLine("{0},16-bar loop,{1:F6},{2:F6},{3:F6},{4:F6}",run,result.ReferenceIntegrated,result.ReferenceTruePeak,result.GainDb,result.ReferenceMaxReductionDb);
    Console.WriteLine("Run {0}: finite I={1:F3}, TP={2:F3}, gain={3:F3}, GR={4:F3}; loop I={5:F3}, TP={6:F3}, GR={7:F3}",run,result.Integrated,result.TruePeak,result.GainDb,result.MaxReductionDb,result.ReferenceIntegrated,result.ReferenceTruePeak,result.ReferenceMaxReductionDb);
   }
  }
  // Forced version is a validation fixture only, never a UI mode.
  double forcedGain=-14-BacardiMastering.Measure(input,Rate);
  BacardiMastering.Result forced=null;
  for(int pass=0;pass<6;pass++) {
   forced=BacardiMastering.ApplyGain(input,Rate,forcedGain);
   if(Math.Abs(forced.Integrated+14)<=.1) break;
   forcedGain+=-14-forced.Integrated;
  }
  WriteWav("Tools/LufsValidation/Forced Master.wav",forced.Audio,1);
  Console.WriteLine("Forced finite: I={0:F3}, TP={1:F3}, gain={2:F3}, GR={3:F3}",forced.Integrated,forced.TruePeak,forced.GainDb,forced.MaxReductionDb);
  File.WriteAllText("Tools/LufsValidation/Forced-result.txt",string.Format("I={0:F6}, TP={1:F6}, gain={2:F6}, maxGR={3:F6}",forced.Integrated,forced.TruePeak,forced.GainDb,forced.MaxReductionDb));
  double dryGain=Math.Min(1,Math.Pow(10,(-1.3-first.ReferenceInputTruePeak)/20));
  // Meter the final audible buffer in streaming blocks; observers must leave it intact.
  var audible=new BacardiMasterComparison(Rate,2);
  audible.Initialize(true,false,Math.Pow(10,first.GainDb/20),dryGain);
  var listenerMeter=new BacardiLufsEngine(Rate,new double[]{1,1});
  var listenerPeak=new BacardiTruePeak(Rate,2);
  var listenerPcm=new float[reference.Length+BacardiTruePeak.DelayFrames*2];
  Array.Copy(reference,listenerPcm,reference.Length);
  for(int n=0;n<listenerPcm.Length;n+=2048) {
   var block=new float[Math.Min(2048,listenerPcm.Length-n)]; Array.Copy(listenerPcm,n,block,0,block.Length);
   audible.Process(block,2,true,false,Math.Pow(10,first.GainDb/20),dryGain);
   var untouched=(float[])block.Clone();
   listenerMeter.Push(block,2); listenerMeter.Refresh(); listenerPeak.Observe(block,2);
   for(int k=0;k<block.Length;k++) if(block[k]!=untouched[k]) throw new Exception("Observer changed audible PCM");
  }
  if(Math.Abs(listenerMeter.Integrated-first.ReferenceIntegrated)>.03) throw new Exception("Post-master streaming meter mismatch");
  Console.WriteLine("Streaming post-master fixture: I={0:F3}, analyzed loop={1:F3}, difference={2:F3} LU, TP={3:F3}, max GR={4:F3} (not Unity playback)",listenerMeter.Integrated,first.ReferenceIntegrated,listenerMeter.Integrated-first.ReferenceIntegrated,20*Math.Log10(listenerPeak.Peak),audible.MaxAudibleReductionDb);
  // Fresh live sessions, initialized to analyzed gain, match fixed offline processing.
  for(int run=0;run<3;run++) {
   var session=new BacardiMasterComparison(Rate,2); session.Initialize(true,false,Math.Pow(10,first.GainDb/20),dryGain);
   var pcm=new float[input.Length+BacardiTruePeak.DelayFrames*2]; Array.Copy(input,pcm,input.Length);
   for(int n=0;n<pcm.Length;n+=2048) {
    int length=Math.Min(2048,pcm.Length-n); var block=new float[length]; Array.Copy(pcm,n,block,0,length);
    session.Process(block,2,true,false,Math.Pow(10,first.GainDb/20),dryGain); Array.Copy(block,0,pcm,n,length);
   }
   for(int n=0;n<first.Audio.Length;n++) if(Math.Abs(pcm[n+BacardiTruePeak.DelayFrames*2]-first.Audio[n])>1e-6) throw new Exception("Fresh live reset differs");
  }
  foreach(float scale in new[]{0f,0.000001f}) {
   var quiet=new float[Rate*8]; for(int n=0;n<quiet.Length;n++) quiet[n]=scale;
   bool rejected=false; try { BacardiMastering.Render(quiet,Rate); } catch(Exception) { rejected=true; }
   if(!rejected) throw new Exception("Quiet guard failed");
  }
  // Continuously warm paths still crossfade when toggling, without resetting.
  var toggle=new BacardiMasterComparison(Rate,2); double maxStep=0; float previous=0;
  for(int block=0;block<500;block++) {
   var pcm=new float[512]; for(int n=0;n<pcm.Length;n+=2) { pcm[n]=.2f; pcm[n+1]=.1f; }
   toggle.Process(pcm,2,block%100<75,block%50<25,1.5,.8);
   for(int n=0;n<pcm.Length;n+=2) {
    if(block>10) { maxStep=Math.Max(maxStep,Math.Abs(pcm[n]-previous)); if(Math.Abs(pcm[n]-2*pcm[n+1])>1e-6) throw new Exception("Stereo balance changed"); }
    previous=pcm[n];
   }
  }
  if(maxStep>.0001) throw new Exception("Toggle discontinuity");
  Console.WriteLine("Three fresh live sessions, bit-identical analysis/export/loop reruns, quiet guards, balance and toggles passed. Max toggle step={0:E3}",maxStep);
 }    static float VelocityGain(int velocity) { return Mathf.Clamp(velocity, 1, 127) / 127f; }
    void Clear() {
        Array.Clear(steps, 0, steps.Length);
        Array.Clear(noteOffsets, 0, noteOffsets.Length);
        for (int i = 0; i < 25; i++) for (int s = 0; s < 16; s++) velocities[i, s] = DefaultVelocity;
    }
    void Demo()
    {
        Clear();
        for (int s = 0; s < 16; s++) { steps[8,s] = s % 4 == 0; steps[10,s] = s % 2 == 0; steps[12,s] = s % 2 == 1; steps[9,s] = s == 4 || s == 12; }
        foreach (int s in new[] { 0, 3, 6, 7, 10, 11, 14, 15 }) steps[6,s] = true;
        int[] logNotes = { 0, 0, 0, 7, 0, 0, 0, 12, 0, 0, 7, 0, 0, 0, 0, 7 };
        for (int s = 0; s < 16; s++) {
            noteOffsets[6,s] = logNotes[s];
            velocities[6,s] = s % 2 == 0 ? 118 : s == 7 || s == 15 ? 76 : 98;
            steps[12,s] = true;
        }
        steps[11,6] = steps[11,14] = steps[18,3] = steps[18,11] = true;
        steps[0,0] = steps[1,8] = steps[15,7] = steps[16,15] = true;
        for (int s = 0; s < 16; s++) {
            velocities[10,s] = s % 4 == 0 ? 105 : 65;
            velocities[12,s] = s % 4 == 1 ? 80 : 48;
        }
        velocities[15,7] = 65; velocities[16,15] = 50;
        EnsureAllSoundsUsed();
    }

    // Fill unused parts with sparse accents; preserve every existing note and velocity.
    static readonly int[] AccentSteps = { 0, 8, 0, 6, 8, 11, 2, 0, 0, 4, 2, 14, 1, 3, 10, 7, 15, 5, 12, 9, 14, 0, 13, 12, 0 };
    void EnsureAllSoundsUsed()
    {
        for (int i = 0; i < 25; i++) {
            if (HasSteps(i)) continue;
            int s = AccentSteps[i];
            steps[i,s] = true;
            velocities[i,s] = i >= 21 ? 32 : i < 8 ? 48 : 55;
            noteOffsets[i,s] = 0;
        }
    }

    bool HasSteps(int id) {
        for (int s = 0; s < 16; s++) if (steps[id, s]) return true;
        return false;
    }

    static bool HasAudiblePcm(float[] data, float gain = 1) {
        foreach (float value in data) {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new Exception("Audio contains invalid sample values.");
            if (Math.Abs(value * gain) >= .5f / 8388607) return true;
        }
        return false;
    }

    float[] RenderSound(int id, int frames) {
        float[] data = sounds[id];
        int channels = 1, sampleRate = Rate;
        if (data == null) {
            channels = clips[id].channels; sampleRate = clips[id].frequency;
            data = new float[clips[id].samples * channels];
            if (!clips[id].GetData(data, 0)) throw new Exception("Use Decompress On Load for sample " + Names[id]);
        }
        float[] result = new float[frames * 2];
        double time = 0;
        int sourceFrames = data.Length / channels;
        if (sourceFrames == 0 || channels > 2) throw new Exception("Use a non-empty mono or stereo sample for " + Names[id]);
        for (int step = 0; step < 16; step++) {
            if (steps[id, step]) {
                int start = (int)Math.Round(time * Rate);
                double pitch=StepPitch(id,step);
                int count = (int)Math.Ceiling(sourceFrames * (double)Rate / sampleRate / pitch);
                for (int n = 0; n < count && start + n < frames; n++) {
                    double position = n * (double)sampleRate / Rate * pitch;
                    int a = Math.Min(sourceFrames - 1, (int)position), b = Math.Min(sourceFrames - 1, a + 1);
                    float fraction = (float)(position - a);
                    for (int c = 0; c < 2; c++) {
                        int channel = channels == 1 ? 0 : c;
                        result[(start + n) * 2 + c] += Mathf.Lerp(data[a * channels + channel], data[b * channels + channel], fraction) * volume * VelocityGain(velocities[id,step]);
                    }
                }
            }
            time += Duration(step);
        }
        return result;
    }

    static void WriteWav(string path, float[] data, float gain) {
        using (var w = new BinaryWriter(File.Create(path))) {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data.Length * 3);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)1); w.Write((short)2); w.Write(Rate); w.Write(Rate * 6);
            w.Write((short)6); w.Write((short)24);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(data.Length * 3);
            foreach (float value in data) {
                int sample = (int)Math.Round(Mathf.Clamp(value * gain, -1, 1) * 8388607);
                w.Write((byte)sample); w.Write((byte)(sample >> 8)); w.Write((byte)(sample >> 16));
            }
        }

    }

    float[] ArrangementMix() {
        double end=60.0/bpm*4,time=0;
        for(int step=0;step<16;step++) {
            for(int i=0;i<25;i++) if(steps[i,step]) end=Math.Max(end,time+clips[i].length/StepPitch(i,step));
            time+=Duration(step);
        }
        var mix=new float[(int)Math.Ceiling((end+.1)*Rate)*2];
        for(int i=0;i<25;i++) if(HasSteps(i)) {
            var track=RenderSound(i,mix.Length/2);
            for(int n=0;n<mix.Length;n++) mix[n]+=track[n];
        }
        return mix;
    }
    static float[] Synthesize(int id) {
        if (id == 6 || id == 7 || id == 8 || id == 9 || id == 10 || id == 11 || id == 12)
            return SynthesizeRhythm(id);
        float duration=id==2 || id==4 || id>=21 ? 2.4f : id<8 ? 1.3f : id==11 ? .5f : .32f;
        float[] data=new float[(int)(Rate*duration)]; var random=new System.Random(1200+id); float filtered=0;
        float[] pitches={261.63f,174.61f,130.81f,523.25f,196,330,55,43.65f,48,180,8000,6500,5500,380,620,220,330,7000,900,160,85,4000,4000,120,55};
        double phase=0;
        for(int n=0;n<data.Length;n++) {
            float t=(float)n/Rate, noise=(float)(random.NextDouble()*2-1), f=pitches[id];
            if(id==8 || id==20 || id==24) f+=140*Mathf.Exp(-t*35);
            if(id==23) f=120+2200*t/duration;
            phase+=2*Math.PI*f/Rate; float sine=(float)Math.Sin(phase), v;
            filtered=filtered*.85f+noise*.15f;
            if(id<5) { v=sine*.6f+(float)Math.Sin(phase*2)*.2f+(float)Math.Sin(phase*3)*.1f; if(id==2 || id==4) v=(sine+(float)Math.Sin(phase*1.2599)+(float)Math.Sin(phase*1.4983))*.25f; }
            else if(id==5) v=(float)Math.Sin(phase+3*Math.Sin(phase*.5))* (.6f+.4f*Mathf.Sin(t*35));
            else if(id==6) v=(float)Math.Sin(phase+5*Math.Exp(-t*12)*Math.Sin(phase*2));
            else if(id==7 || id==8 || id==20) v=sine;
            else if(id==9) v=noise*(.35f+.65f*Mathf.Abs(Mathf.Sin(t*180)));
            else if(id==10 || id==11 || id==12 || id==17 || id==21 || id==22) v=noise-filtered;
            else if(id==23) v=sine*.4f+noise*.4f;
            else if(id==24) v=sine*.7f+noise*.3f;
            else v=sine*.7f+(float)Math.Sin(phase*1.73)*.2f+noise*.1f;
            float decay=id==2 || id==4 ? 1.7f : id==0 || id==1 ? 4 : id==6 ? 7 : id==7 ? 5 : id==10 || id==18 ? 38 : id>=21 ? 2 : 15;
            float env=Mathf.Min(1,t*500)*Mathf.Exp(-t*decay)*Mathf.Min(1,(duration-t)*100);
            if(id==22 || id==23) env=Mathf.Pow(t/duration,1.5f)*Mathf.Min(1,(duration-t)*100);
            data[n]=v*env*.55f;
        }
        return data;
    }

    // Tuned low end, short transients and contrasting noise envelopes leave room
    // for the syncopated logdrum. These samples also feed WAV and stem exports.
    static float[] SynthesizeRhythm(int id)
    {
        double duration = id == 6 ? .65 : id == 7 ? .7 : id == 11 ? .28 : id == 9 ? .22 : id == 8 ? .24 : .10;
        var data = new float[(int)(Rate * duration)];
        var random = new System.Random(2400 + id);
        double phase = 0, lowNoise = 0, previousNoise = 0;
        for (int n = 0; n < data.Length; n++) {
            double t = (double)n / Rate;
            double noise = random.NextDouble() * 2 - 1;
            lowNoise += .22 * (noise - lowNoise);
            double bright = (noise - previousNoise) * .5;
            previousNoise = noise;
            double value;
            if (id == 6) {
                // Settle rapidly onto A1 so per-step pitch and MIDI agree.
                phase += 2 * Math.PI * (55 + 105 * Math.Exp(-t * 85)) / Rate;
                double body = Math.Sin(phase + 3.8 * Math.Exp(-t * 18) * Math.Sin(phase * 2));
                value = Math.Tanh(body * 1.65) * Math.Exp(-t * 8) * .72
                    + bright * Math.Exp(-t * 180) * .12;
            } else if (id == 7) {
                phase += 2 * Math.PI * 43.65353 / Rate;
                value = (Math.Sin(phase) + .12 * Math.Sin(phase * 2)) * Math.Exp(-t * 5) * .48;
            } else if (id == 8) {
                phase += 2 * Math.PI * (48 + 145 * Math.Exp(-t * 65)) / Rate;
                value = Math.Tanh(Math.Sin(phase) * 1.4) * Math.Exp(-t * 19) * .67
                    + bright * Math.Exp(-t * 220) * .15;
            } else if (id == 9) {
                double bursts = 0;
                for (int b = 0; b < 3; b++) {
                    double age = t - b * .011;
                    if (age >= 0) bursts += Math.Exp(-age * 150);
                }
                value = (noise - lowNoise) * (bursts * .25 + Math.Exp(-t * 24) * .3);
            } else {
                double decay = id == 11 ? 18 : id == 12 ? 62 : 85;
                double metal = (Math.Sin(2 * Math.PI * 7310 * t) + Math.Sin(2 * Math.PI * 4963 * t)) * .07;
                value = (bright * .65 + metal) * Math.Exp(-t * decay) * (id == 12 ? .30 : .38);
            }
            double edge = Math.Min(1, t * 1200) * Math.Min(1, (duration - t) * 200);
            data[n] = (float)(value * edge);
        }
        return data;
    }
}
static class Mathf {
 public static float Pow(float x,float y)=>(float)Math.Pow(x,y);
 public static float Exp(float x)=>(float)Math.Exp(x);
 public static float Sin(float x)=>(float)Math.Sin(x);
 public static float Abs(float x)=>Math.Abs(x);
 public static float Min(float x,float y)=>Math.Min(x,y);
 public static float Clamp(float x,float a,float b)=>Math.Max(a,Math.Min(b,x));
 public static int Clamp(int x,int a,int b)=>Math.Max(a,Math.Min(b,x));
 public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
}





