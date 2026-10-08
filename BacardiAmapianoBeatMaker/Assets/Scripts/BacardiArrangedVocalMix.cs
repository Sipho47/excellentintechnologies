using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

public partial class BacardiBeatMaker {
    sealed class ArrangedVocalMix { public float[] Pcm; public StringBuilder Metadata; public int PlacementCount; }
    ArrangedVocalMix BuildArrangedVocalMix() {
            var placements=aiVocals==null ? new List<BacardiAiVocalGenerator.Placement>() :
                new List<BacardiAiVocalGenerator.Placement>(aiVocals.Placements);
            foreach(var p in placements) {
                var v=p==null ? null : p.Vocal;
                if(v==null || v.Request==null || v.SampleRate!=Rate || v.Pcm==null || v.Pcm.Length==0 || v.Pcm.Length%2!=0)
                    throw new InvalidDataException("Placed vocal must contain decoded 44100 Hz stereo audio.");
                if(v.Source!=(v.IsMock?"LOCAL_MOCK":"REAL_AI")) throw new InvalidDataException("Placed vocal has an invalid source identity.");
                AiVocalWav.RequireTempo(v,bpm);
                if(p.StartBar<1 || v.Request.Bars<1 || (long)p.StartBar-1+v.Request.Bars>SongBars)
                    throw new ArgumentException("Vocal placement extends outside the current song. Move/remove it before export.");
                foreach(float sample in v.Pcm) if(float.IsNaN(sample)||float.IsInfinity(sample)||Math.Abs(sample)>1)
                    throw new InvalidDataException("Placed vocal contains invalid audio.");
                long end=BacardiVocalTimeline.FrameAtBar(p.StartBar,bpm)+v.Pcm.Length/2;
                if(end>BacardiVocalTimeline.FrameAtBar(SongBars+1,bpm))
                    throw new ArgumentException("Complete vocal audio must fit inside the current song; no truncation is allowed.");
            }
            var mix=ArrangementMix();
            var info=new StringBuilder("ARRANGED STEREO + VOCALS\n");
            info.AppendLine("Song bars: "+SongBars+" | BPM: "+bpm.ToString("R",CultureInfo.InvariantCulture));
            info.AppendLine("Stereo 44100 Hz float32 | Frames: "+mix.Length/2);
            info.AppendLine("Included vocal placements: "+placements.Count);
            foreach(var p in placements) {
                // Reuse the aligned stem's bar-to-frame mapping, without its separate stem trim.
                var aligned=AiVocalWav.Aligned(p.Vocal,p.StartBar,SongBars,bpm);
                for(int n=0;n<aligned.Length;n++) mix[n]+=aligned[n];
                info.AppendLine("Vocal: "+p.Vocal.Source+" | Provider: "+p.Vocal.Provider+
                    " | Start bar: "+p.StartBar+" | Bars: "+p.Vocal.Request.Bars+
                    " | Start frame: "+BacardiVocalTimeline.FrameAtBar(p.StartBar,bpm)+
                    " | PCM frames: "+p.Vocal.Pcm.Length/2);
            }
            double peak=0;
            foreach(float sample in mix) {
                if(float.IsNaN(sample)||float.IsInfinity(sample)) throw new InvalidDataException("Combined mix contains invalid audio.");
                peak=Math.Max(peak,Math.Abs((double)sample));
            }
            // Same constant sample-peak headroom approach as the unmastered mix exporter.
            double gain=peak>1 ? 1/peak : 1;
            if(gain<1) for(int n=0;n<mix.Length;n++) mix[n]=(float)(mix[n]*gain);
            info.AppendLine("Combined sample peak before safety: "+peak.ToString("R",CultureInfo.InvariantCulture));
            info.AppendLine("Safety gain: "+gain.ToString("R",CultureInfo.InvariantCulture)+" ("+
                (20*Math.Log10(gain)).ToString("F6",CultureInfo.InvariantCulture)+" dB)");
            info.AppendLine("One common attenuation only if sample peak exceeds full scale. No normalization, limiter, or Spotify mastering.");
        return new ArrangedVocalMix { Pcm=mix, Metadata=info, PlacementCount=placements.Count };
    }
}
