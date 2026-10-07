using System;
using System.Collections.Generic;

// Musical position is independent of the repeating 16-step pattern.
// All transport times are AudioSettings.dspTime, never frame/Time.time accumulation.
public sealed class BacardiVocalTimeline
{
    struct Segment { public double Time, Beat, Bpm; }
    readonly List<Segment> segments = new List<Segment>();
    double heldBeat, heldSeconds, epoch, initialSeconds;
    public bool Running { get; private set; }
    public double Bpm { get; private set; } = 104;
    public double BeatsAt(double dsp) {
        if (!Running) return heldBeat;
        if (dsp <= epoch) return heldBeat;
        Segment s = segments[0];
        foreach (var candidate in segments) {
            if (candidate.Time > dsp) break;
            s = candidate;
        }
        return s.Beat + Math.Max(0, dsp - s.Time) * s.Bpm / 60;
    }
    public double TempoAt(double dsp) {
        if (!Running) return Bpm;
        double tempo = segments[0].Bpm;
        foreach (var s in segments) { if (s.Time > dsp) break; tempo = s.Bpm; }
        return tempo;
    }
    public double ElapsedAt(double dsp) {
        return Running ? initialSeconds + Math.Max(0, dsp - epoch) : heldSeconds;
    }
    public int BarAt(double dsp) { return (int)Math.Floor(BeatsAt(dsp) / 4) + 1; }
    public double BeatInBarAt(double dsp) { return BeatsAt(dsp) % 4 + 1; }
    public void Play(double dspStart, double bpm) {
        ValidateTempo(bpm);
        epoch = dspStart; initialSeconds = heldSeconds; Bpm = bpm;
        segments.Clear();
        segments.Add(new Segment { Time = dspStart, Beat = heldBeat, Bpm = bpm });
        Running = true;
    }
    public void Pause(double dsp) {
        heldBeat = BeatsAt(dsp); heldSeconds = ElapsedAt(dsp); Running = false;
    }
    public void Stop() {
        Running = false; heldBeat = heldSeconds = initialSeconds = 0; segments.Clear();
    }
    public void SetTempo(double bpm, double dspBoundary) {
        ValidateTempo(bpm);
        if (Math.Abs(bpm - Bpm) < .000001) return;
        if (Running) {
            // Replacing a not-yet-audible tempo boundary is handled by caller using
            // the next unscheduled beat time, so queued beat audio stays unchanged.
            double beat = BeatsAt(dspBoundary);
            segments.Add(new Segment { Time = dspBoundary, Beat = beat, Bpm = bpm });
        }
        Bpm = bpm;
    }
    public double DspAtBeat(double beat) {
        if (!Running) throw new InvalidOperationException("Timeline is paused.");
        Segment s = segments[0];
        foreach (var candidate in segments) {
            if (candidate.Beat > beat) break;
            s = candidate;
        }
        return s.Time + (beat - s.Beat) * 60 / s.Bpm;
    }
    public static long FrameAtBar(int bar, double bpm, int rate = 44100) {
        ValidateTempo(bpm);
        if (bar < 1) throw new ArgumentOutOfRangeException(nameof(bar));
        return checked((long)Math.Round((bar - 1) * 4 * 60.0 / bpm * rate));
    }
    static void ValidateTempo(double bpm) {
        if (double.IsNaN(bpm) || double.IsInfinity(bpm) || bpm <= 0)
            throw new ArgumentOutOfRangeException(nameof(bpm));
    }
}

