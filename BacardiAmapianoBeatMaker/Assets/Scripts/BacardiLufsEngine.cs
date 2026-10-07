using System;
using System.Collections.Generic;
using System.Threading;

// BS.1770 K-weighting and EBU R128 windows. No Unity API or allocations in Push.
public sealed class BacardiLufsEngine
{
    struct Biquad {
        public double b0, b1, b2, a1, a2, z1, z2;
        public double Tick(double x) {
            double y = b0*x + z1;
            z1 = b1*x - a1*y + z2; z2 = b2*x - a2*y;
            return y;
        }
    }
    readonly Biquad[] shelf, highpass;
    readonly double[] weights, queue = new double[4096], window = new double[30];
    readonly List<double> blocks = new List<double>();
    readonly int hop;
    int frames, written, read, position, count;
    double energy, absoluteSum;
    public bool Overflowed { get; private set; }
    public double ShortTerm { get; private set; } = double.NaN;
    public double Integrated { get; private set; } = double.NaN;

    public BacardiLufsEngine(int rate, double[] channelWeights) {
        hop = (int)Math.Round(rate * .1);
        weights = (double[])channelWeights.Clone();
        shelf = new Biquad[weights.Length]; highpass = new Biquad[weights.Length];
        // De Man parameterisation reproduces the BS.1770 reference coefficients
        // at 48 kHz and adapts the same response to the actual device sample rate.
        double k = Math.Tan(Math.PI * 1681.974450955533 / rate);
        double vh = Math.Pow(10, 3.999843853973347 / 20), vb = Math.Pow(vh, .4996667741545416);
        double q = .7071752369554196, d = 1 + k/q + k*k;
        Biquad s = new Biquad { b0=(vh+vb*k/q+k*k)/d, b1=2*(k*k-vh)/d,
            b2=(vh-vb*k/q+k*k)/d, a1=2*(k*k-1)/d, a2=(1-k/q+k*k)/d };
        k = Math.Tan(Math.PI * 38.13547087602444 / rate); q = .5003270373238773;
        d = 1 + k/q + k*k;
        Biquad h = new Biquad { b0=1, b1=-2, b2=1,
            a1=2*(k*k-1)/d, a2=(1-k/q+k*k)/d };
        for (int c=0; c<weights.Length; c++) { shelf[c]=s; highpass[c]=h; }
    }
    // Single audio-thread producer; input is observed and never modified.
    public void Push(float[] data, int channels) {
        if (channels != weights.Length) return;
        for (int i=0; i<data.Length; i+=channels) {
            for (int c=0; c<channels; c++) {
                double x = data[i+c];
                if (double.IsNaN(x) || double.IsInfinity(x)) x=0;
                double y = highpass[c].Tick(shelf[c].Tick(x));
                energy += weights[c]*y*y;
            }
            if (++frames != hop) continue;
            int w = written;
            if (w - Volatile.Read(ref read) >= queue.Length) { Overflowed=true; }
            else { queue[w % queue.Length]=energy/hop; Volatile.Write(ref written, w+1); }
            frames=0; energy=0;
        }
    }
    static double Loudness(double power) {
        return power > 1e-20 ? -.691 + 10*Math.Log10(power) : double.NaN;
    }
    // Main-thread consumer. Re-gate the complete session, never average LUFS values.
    public void Refresh() {
        while (read < Volatile.Read(ref written)) {
            window[position] = queue[read % queue.Length];
            Volatile.Write(ref read, read+1);
            position=(position+1)%30; count=Math.Min(count+1,30);
            if (count < 4) continue;
            double power=0;
            for (int n=1;n<=4;n++) power+=window[(position+30-n)%30];
            power/=4;
            if (Loudness(power) > -70) { blocks.Add(power); absoluteSum+=power; }
        }
        if (count==30) {
            double sum=0; for(int i=0;i<30;i++) sum+=window[i];
            ShortTerm=Loudness(sum/30);
        }
        if (blocks.Count>0) {
            double threshold=absoluteSum/blocks.Count*.1, sum=0; int included=0;
            foreach(double power in blocks) if(power>threshold) { sum+=power; included++; }
            Integrated=included>0 ? Loudness(sum/included) : double.NaN;
        }
        if (Overflowed) ShortTerm=Integrated=double.NaN;
    }
}
