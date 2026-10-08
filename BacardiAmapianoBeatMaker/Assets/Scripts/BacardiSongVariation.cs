using System;
using System.Text;

// Pure offline planning: no audio IO, gain, tempo, provider or Unity dependencies.
public static class BacardiSongVariation
{
    public static readonly string[] Personalities = { "DEEP / MINIMAL", "VOCAL AMAPIANO", "LOGDRUM HEAVY", "SOULFUL", "PRIVATE SCHOOL", "CLUB / ENERGY", "AFRO-HOUSE FUSION" };
    public static readonly string[] Amounts = { "SUBTLE", "MEDIUM", "STRONG" };
    [Serializable] public class Section {
        public string name;
        public int start, bars, lead, energy, percussion, melody, logEntry, bassEntry;
        public uint stems;
    }
    [Serializable] public class Plan {
        public int version = 1, seed, personality, amount, bars;
        public Section[] sections;
        // Per bar, per stem sixteenth-note permission masks. Notes/levels stay in the groove.
        public int[] masks;
        // v1 masks are interpreted exactly as before. v2 stores complete resolved events.
        public int generatorVersion;
        public int phraseBars;
        public BacardiVariationEvents.Groove sourceGroove;
        public int[] eventNotes, eventVelocities;
        public BacardiVariationEvents.Score sourceScore, previousScore, dropScore;
        public bool hasPreviousScore;
        public bool Allows(int stem, int absoluteStep) {
            return absoluteStep >= 0 && absoluteStep < bars * 16 &&
                (masks[absoluteStep / 16 * 25 + stem] & (1 << (absoluteStep % 16))) != 0;
        }
        public string Preview(string[] names) {
            var text = new StringBuilder();
            foreach (var s in sections) {
                text.Append(s.name).Append(": Bars ").Append(s.start + 1).Append("–").Append(s.start + s.bars)
                    .Append(" | Lead: ").Append(names[s.lead]).Append(" | Energy: ").Append(s.energy)
                    .Append(" | Percussion: ").Append(s.percussion).Append(" | Melodic layers: ").Append(s.melody)
                    .Append(" | Log/Bass enter: ").Append(s.logEntry < 0 ? "off" : (s.logEntry + 1).ToString())
                    .Append('/').Append(s.bassEntry < 0 ? "off" : (s.bassEntry + 1).ToString()).Append('\n');
                text.Append("  Layers: ");
                for (int id = 0; id < 25; id++) if ((s.stems & (1u << id)) != 0) text.Append(names[id]).Append(", ");
                text.Append('\n');
            }
            return text.ToString();
        }
    }
    // Versioned integer PRNG is stable across Unity/.NET versions.
    sealed class Rng {
        uint state;
        public Rng(int seed) { state = unchecked((uint)seed) ^ 0x9e3779b9u; if (state == 0) state = 1; }
        public int Next(int max) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (int)(state % (uint)max); }
    }
    public static Plan Create(int seed, int personality, int amount) {
        if (personality < 0 || personality >= 7 || amount < 0 || amount >= 3) throw new ArgumentOutOfRangeException();
        var r = new Rng(seed);
        var p = new Plan { seed = seed, personality = personality, amount = amount, sections = new Section[6] };
        string[] names = { "Intro", "Build", "Drop 1", "Breakdown", "Drop 2", "Outro" };
        int[] defaults = { 4, 4, 8, 4, 8, 4 };
        int[][] favored = { new[]{1,2}, new[]{0,1,3}, new[]{3,0}, new[]{1,0,4}, new[]{1,2,3}, new[]{3,0}, new[]{0,3,4} };
        int[][] favoredPercussion = { new[]{18,13}, new[]{17,15,18}, new[]{19,20,14}, new[]{15,16,18}, new[]{13,18,17}, new[]{14,17,20}, new[]{15,16,19} };
        int baseLead = favored[personality][r.Next(favored[personality].Length)];
        for (int i = 0; i < 6; i++) {
            bool drop = i == 2 || i == 4;
            int length = amount == 0 ? defaults[i] : amount == 1 ? (drop ? 8 + 4 * r.Next(2) : 4 + 4 * r.Next(2)) :
                (drop ? new[]{4,8,12,16}[r.Next(4)] : new[]{2,4,6,8,12}[r.Next(5)]);
            var s = new Section { name = names[i], start = p.bars, bars = length,
                lead = amount == 0 ? baseLead : favored[personality][r.Next(favored[personality].Length)],
                energy = drop ? 3 + r.Next(3) : i == 1 ? 2 + r.Next(2) : 1 + r.Next(2) };
            s.percussion = Math.Min(8, (personality == 0 ? 1 : personality >= 5 ? 4 : 2) + (drop ? 1 : 0) + r.Next(amount + 1));
            s.melody = Math.Min(4, (personality == 0 || personality == 2 ? 1 : 2) + r.Next(amount + 1));
            s.stems = 1u << s.lead;
            for (int n = 1; n < s.melody; n++) {
                int id; do { id = r.Next(5); } while ((s.stems & (1u << id)) != 0);
                s.stems |= 1u << id;
            }
            if (personality == 1 || (personality != 0 && r.Next(3) == 0)) s.stems |= 1u << 5;
            if (i == 1 || drop || (i == 5 && r.Next(2) == 0)) s.stems |= (1u << 8) | (1u << 12);
            if (drop) s.stems |= (1u << 9) | (1u << (10 + r.Next(2)));
            for (int n = 0; n < s.percussion; n++) {
                int id; do { id = r.Next(3) == 0 ? 13 + r.Next(8) : favoredPercussion[personality][r.Next(favoredPercussion[personality].Length)]; } while ((s.stems & (1u << id)) != 0);
                s.stems |= 1u << id;
            }
            s.logEntry = drop ? s.start + (personality == 2 ? 0 : r.Next(Math.Min(length, 5))) : i == 1 && r.Next(2) == 0 ? s.start + length - 2 : -1;
            s.bassEntry = drop || i == 1 || (i == 3 && r.Next(2) == 0) ? s.start + r.Next(Math.Min(length, 4)) : -1;
            if (s.logEntry >= 0) s.stems |= 1u << 6;
            if (s.bassEntry >= 0) s.stems |= 1u << 7;
            // Transition types are chosen, never all played together every bar.
            s.stems |= 1u << (drop ? (r.Next(2) == 0 ? 21 : 24) : (r.Next(2) == 0 ? 22 : 23));
            p.sections[i] = s; p.bars += length;
        }
        p.masks = new int[p.bars * 25];
        foreach (var s in p.sections) {
            int pauseBar = amount == 2 ? s.start + r.Next(s.bars) : -1;
            for (int bar = s.start; bar < s.start + s.bars; bar++) for (int id = 0; id < 25; id++) {
                if ((s.stems & (1u << id)) == 0 || id == 6 && bar < s.logEntry || id == 7 && bar < s.bassEntry) continue;
                int mask = 65535;
                if (id >= 21) mask = id == 21 || id == 24 ? (bar == s.start ? 1 : 0) : (bar == s.start + s.bars - 1 ? 1 << 12 : 0);
                else {
                    // Build layers progressively; outro removes them. Lead remains an anchor.
                    if (id != s.lead && (s.name == "Build" && bar - s.start < r.Next(Math.Min(4,s.bars)) ||
                        s.name == "Outro" && bar - s.start > s.bars / 2 && id % 2 == 0)) mask = 0;
                    for (int step = 0; step < 16; step++) {
                        int keep = id < 6 ? 65 + s.melody * 8 : 45 + s.energy * 10;
                        if (id == s.lead || id == 8 && step % 4 == 0) keep = 100;
                        if (r.Next(100) >= keep) mask &= ~(1 << step);
                    }
                    if (bar == pauseBar && id != s.lead) mask &= 255; // half-bar breath, preserves tails
                    if (id == 6 && personality == 2) mask |= 0x4449;
                }
                p.masks[bar * 25 + id] = mask;
            }
        }
        return p;
    }
    static double Ratio(double a, double b) { return Math.Abs(a-b) / Math.Max(1, Math.Max(a,b)); }
    static int Bits(uint n) { int c = 0; while(n != 0) { c++; n &= n-1; } return c; }
    // Weighted distance in [0,1], includes section boundaries AND realized masks/density.
    public static double Difference(Plan a, Plan b) {
        if (a == null || b == null) return 1;
        double layers=0, boundaries=Ratio(a.bars,b.bars), drums=0, melody=0, low=0, rhythm=0;
        for (int i=0;i<6;i++) {
            var x=a.sections[i]; var y=b.sections[i];
            layers += (double)Bits(x.stems ^ y.stems) / Math.Max(1,Bits(x.stems | y.stems));
            boundaries += .5*Ratio(x.start,y.start)+.5*Ratio(x.bars,y.bars);
            drums += Ratio(x.percussion,y.percussion); melody += Ratio(x.melody,y.melody) + (x.lead == y.lead ? 0 : 1);
            low += (x.logEntry < 0 != (y.logEntry < 0) ? 1 : Ratio(x.logEntry-x.start,y.logEntry-y.start));
            low += (x.bassEntry < 0 != (y.bassEntry < 0) ? 1 : Ratio(x.bassEntry-x.start,y.bassEntry-y.start));
            for (int id=0;id<25;id++) {
                uint mx=(uint)a.masks[(x.start+x.bars/2)*25+id], my=(uint)b.masks[(y.start+y.bars/2)*25+id];
                rhythm += (double)Bits(mx^my)/Math.Max(1,Bits(mx|my));
            }
        }
        return .30*layers/6 + .25*boundaries/7 + .10*drums/6 + .10*melody/12 + .15*low/12 + .10*rhythm/150;
    }
    public static string DifferenceLabel(double d) { return d >= .38 ? "HIGH" : d >= .20 ? "MEDIUM" : "LOW"; }
    public static Plan Generate(int seed, int personality, int amount, Plan previous, out double difference) {
        double threshold = amount == 2 ? .38 : amount == 1 ? .20 : .08;
        for (int attempt=0;attempt<1024;attempt++) {
            // Return/persist the accepted seed, making replay independent of history.
            var plan=Create(unchecked(seed+attempt*104729),personality,amount);
            difference=Difference(previous,plan);
            bool structureChanged = previous == null || plan.bars != previous.bars;
            if(previous != null) for(int i=0;i<6;i++) structureChanged |= plan.sections[i].start != previous.sections[i].start || plan.sections[i].bars != previous.sections[i].bars;
            if (previous == null || difference >= threshold && (amount != 2 || structureChanged)) return plan;
        }
        difference=0;
        throw new InvalidOperationException("No sufficiently different plan found; current arrangement retained. Try another personality or seed.");
    }
}
