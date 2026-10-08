using System;

// Offline event planning. No Unity, audio IO, provider, tempo or gain processing.
public static class BacardiVariationEvents {
    public const int GeneratorVersion=1;
    public const double OverallMinimum=.45, FamilyMinimum=.25, DropMinimum=.50;
    // Calibrated source gates retain roughly .08 overall and .09 family headroom in the fixed corpus.
    public const double SourceOverallMinimum=.85, SourceFamilyMinimum=.75;
    public static readonly int[] Roots={60,53,48,72,55,64,33,29};
    [Serializable] public class Groove { public int[] notes, velocities; }
    [Serializable] public class Score {
        public double melody, lowEnd, percussion, overall;
        public override string ToString() { return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "melody {0:F3} | low end {1:F3} | percussion {2:F3} | overall {3:F3}",melody,lowEnd,percussion,overall); }
    }
    sealed class Rng {
        uint state;
        public Rng(int seed) { state=unchecked((uint)seed)^0x7315a2d9u; if(state==0) state=1; }
        public int Next(int n) { state^=state<<13; state^=state>>17; state^=state<<5; return (int)(state%(uint)n); }
    }
    public static Groove Capture(bool[,] cells,int[,] notes,int[,] velocities) {
        var g=new Groove {notes=new int[400],velocities=new int[400]};
        for(int id=0;id<25;id++) for(int step=0;step<16;step++) {
            int n=id*16+step; g.notes[n]=notes[id,step]; g.velocities[n]=cells[id,step]?velocities[id,step]:0;
        }
        return g;
    }
    public static bool ValidGroove(Groove g) {
        if(g==null||g.notes==null||g.velocities==null||g.notes.Length!=400||g.velocities.Length!=400) return false;
        for(int n=0;n<400;n++) if(g.notes[n]<-24||g.notes[n]>24||g.velocities[n]<0||g.velocities[n]>127) return false;
        return true;
    }
    public static bool ValidEvents(BacardiSongVariation.Plan p) {
        if(p.generatorVersion!=GeneratorVersion||(p.phraseBars!=2&&p.phraseBars!=4)||p.amount!=2||!ValidGroove(p.sourceGroove)||
            p.eventNotes==null||p.eventVelocities==null||p.eventNotes.Length!=p.bars*400||p.eventVelocities.Length!=p.bars*400) return false;
        for(int n=0;n<p.eventNotes.Length;n++) if(p.eventNotes[n]<-24||p.eventNotes[n]>24||p.eventVelocities[n]<0||p.eventVelocities[n]>127) return false;
        for(int bar=0;bar<p.bars;bar++) for(int id=0;id<25;id++) {
            int mask=0; for(int step=0;step<16;step++) if(p.eventVelocities[bar*400+id*16+step]>0) mask|=1<<step;
            if(p.masks[bar*25+id]!=mask) return false;
        }
        return true;
    }
    public static void Resolve(BacardiSongVariation.Plan p,Groove source,int id,int absoluteStep,out int note,out int velocity) {
        int step=absoluteStep%16;
        if(p!=null&&p.version==2) {
            int n=absoluteStep/16*400+id*16+step; note=p.eventNotes[n]; velocity=p.eventVelocities[n]; return;
        }
        int s=step;
        if(p!=null&&!p.Allows(id,absoluteStep)) {note=velocity=0;return;}
        if(p!=null&&id>=21) { s=-1; for(int k=0;k<16;k++) if(source.velocities[id*16+k]>0) {s=k;break;} }
        if(s<0) {note=velocity=0;return;}
        note=source.notes[id*16+s]; velocity=source.velocities[id*16+s];
    }
    static int Mod(int n,int d) { return (n%d+d)%d; }
    static int Key(Groove g) {
        int[] major={0,2,4,5,7,9,11}; int best=0,bestScore=-1;
        for(int key=0;key<12;key++) {
            int score=0;
            for(int id=0;id<8;id++) for(int s=0;s<16;s++) if(g.velocities[id*16+s]>0) {
                int pitch=Roots[id]+g.notes[id*16+s];
                foreach(int interval in (id==2||id==4?new[]{0,4,7}:new[]{0}))
                    foreach(int degree in major) if(Mod(pitch+interval-key,12)==degree) score+=id<5?3:1;
            }
            if(score>bestScore) {bestScore=score;best=key;}
        }
        return best;
    }
    static int SourceNote(Groove g,int id) { for(int s=0;s<16;s++) if(g.velocities[id*16+s]>0) return g.notes[id*16+s]; return 0; }
    static int ClosestRoot(int id,int key,int degree,int octave,int reference=0) {
        int target=Roots[id]+reference+octave*12, pc=Mod(key+degree,12),best=reference,distance=int.MaxValue;
        for(int offset=Math.Max(-24,reference-12);offset<=Math.Min(24,reference+12);offset++)
            if(Mod(Roots[id]+offset,12)==pc&&Math.Abs(Roots[id]+offset-target)<distance) {best=offset;distance=Math.Abs(Roots[id]+offset-target);}
        return best;
    }
    static void Hit(BacardiSongVariation.Plan p,int bar,int id,int step,int note,int velocity) {
        if(bar<0||bar>=p.bars||step<0||step>=16) return;
        int n=bar*400+id*16+step; p.eventNotes[n]=note; p.eventVelocities[n]=Math.Max(1,Math.Min(127,velocity)); p.masks[bar*25+id]|=1<<step;
    }
    public static BacardiSongVariation.Plan Create(int seed,int personality,Groove source) {
        if(!ValidGroove(source)) throw new ArgumentException("Invalid source groove.");
        // Keep the existing section-length/personality planner; replace only STRONG event realization.
        var p=BacardiSongVariation.Create(seed,personality,2); p.version=2; p.generatorVersion=GeneratorVersion;
        p.sourceGroove=new Groove {notes=(int[])source.notes.Clone(),velocities=(int[])source.velocities.Clone()};
        p.eventNotes=new int[p.bars*400];p.eventVelocities=new int[p.bars*400];Array.Clear(p.masks,0,p.masks.Length);
        var r=new Rng(seed); int key=Key(source),firstLead=new[]{0,1,3}[r.Next(3)],phrase=2+2*r.Next(2);
        p.phraseBars=phrase;
        int[][] motifs={new[]{0,3,6,10,14},new[]{1,4,7,11,15},new[]{0,5,8,11,14},new[]{2,6,9,12,15}};
        int[][] logs={new[]{0,3,7,10,14},new[]{1,4,6,11,15},new[]{0,5,8,10,13},new[]{2,6,9,12,15}};
        int[] degrees={0,2,4,7,9},leadCycle={0,1,3}; int dropPattern=r.Next(4);
        for(int si=0;si<6;si++) {
            var s=p.sections[si];bool drop=si==2||si==4, sparse=si==0||si==3||si==5;
            int lead=leadCycle[(Array.IndexOf(leadCycle,firstLead)+si)%3];
            if(si==4) lead=leadCycle[(Array.IndexOf(leadCycle,p.sections[2].lead)+1)%3];
            s.lead=lead; s.energy=drop?5:si==1?3:1;s.stems=0;
            s.logEntry=drop?s.start+(si==2?0:1):si==1?s.start+Math.Max(0,s.bars-2):-1;
            s.bassEntry=drop?s.start:si==1?s.start+Math.Min(2,s.bars-1):-1;
            int motif=(dropPattern+si)%4,lowPattern=(dropPattern+(si==4?2:0))%4;
            int harmony=lead==1?0:1, harmonyNote=SourceNote(source,harmony);
            int[] phraseNotes=new int[phrase*5];for(int n=0;n<phraseNotes.Length;n++) phraseNotes[n]=degrees[r.Next(degrees.Length)];
            int percA=new[]{13,15,16,18,19}[r.Next(5)],percB=new[]{14,16,17,19,20}[r.Next(5)];
            for(int local=0;local<s.bars;local++) {
                int bar=s.start+local,phase=local%phrase;
                // Repeat a complete 2/4-bar motif, with a controlled phrase-ending answer.
                for(int n=0;n<5;n++) if(!sparse||n==0||n==3&&phase%2==1) {
                    int step=motifs[motif][n];if(phase%2==1) step=Mod(step+2,16);
                    Hit(p,bar,lead,step,ClosestRoot(lead,key,phraseNotes[phase*5+n],si==4&&phase==phrase-1?1:0,SourceNote(source,lead)),sparse?65:85+(n%2)*10);
                }
                if(!sparse||phase==0) {
                    Hit(p,bar,harmony,phase%2==0?0:2,harmonyNote,sparse?45:60);
                    if(drop) Hit(p,bar,harmony,10,harmonyNote,48);
                }
                // Existing chord samples keep their voicing/root; new attack spacing provides contrast.
                if((si==0||si==3)&&phase==0) {int chord=si==3?4:2;Hit(p,bar,chord,0,SourceNote(source,chord),42);}
                if(bar>=s.logEntry&&s.logEntry>=0) for(int n=0;n<logs[lowPattern].Length;n++) {
                    int step=Mod(logs[lowPattern][n]+(phase%2==1?2:0),16);
                    Hit(p,bar,6,step,ClosestRoot(6,key,new[]{0,7,0,4,7}[n],0,SourceNote(source,6)),n%2==0?108:78);
                }
                if(bar>=s.bassEntry&&s.bassEntry>=0) {
                    Hit(p,bar,7,si==4?2:0,ClosestRoot(7,key,0,0,SourceNote(source,7)),84);
                    Hit(p,bar,7,si==4?11:10,ClosestRoot(7,key,phase%2==0?7:0,0,SourceNote(source,7)),68);
                }
                if(drop||si==1) {
                    for(int step=0;step<16;step+=4) if(!(si==4&&phase%2==1&&step==8)) Hit(p,bar,8,step,0,100);
                    Hit(p,bar,9,4,0,84);Hit(p,bar,9,12,0,si==4?96:78);
                }
                int hatStride=drop?2:si==1?4:8;
                for(int step=si==3?3:1;step<16;step+=hatStride) {
                    Hit(p,bar,12,step,0,step%4==1?65:42);
                    if(drop&&step%4==3) Hit(p,bar,10,step,0,50);
                }
                if(si==4) Hit(p,bar,11,14,0,55);
                if(!sparse||phase%2==1) {
                    Hit(p,bar,percA,Mod(3+phase*2,16),0,62);Hit(p,bar,percB,Mod(11+phase*2,16),0,54);
                    if(drop) Hit(p,bar,percA,si==4?14:8,0,45);
                }
                // Phrase fills, a restrained boundary hit, and a single pre-section transition.
                if(!sparse&&phase==phrase-1) {Hit(p,bar,20,13,0,62);Hit(p,bar,20,15,0,78);}
                if(local==0&&drop) Hit(p,bar,si==2?24:21,0,0,55);
                if(local==s.bars-1&&si<5) Hit(p,bar,si%2==0?22:23,12,0,48);
                for(int id=0;id<25;id++) if(p.masks[bar*25+id]!=0) s.stems|=1u<<id;
            }
            s.melody=0;s.percussion=0;for(int id=0;id<5;id++) if((s.stems&(1u<<id))!=0)s.melody++;
            for(int id=13;id<21;id++)if((s.stems&(1u<<id))!=0)s.percussion++;
        }
        return p;
    }
    // Four-bar phrase comparisons at corresponding sections: lengths earn no credit.
    public static Score Compare(BacardiSongVariation.Plan a,BacardiSongVariation.Plan b,Groove source,int sectionA=-1,int sectionB=-1) {
        double[] changed=new double[3],union=new double[3]; int count=sectionA<0?6:1;
        for(int i=0;i<count;i++) {
            int sa=sectionA<0?i:sectionA,sb=sectionB<0?i:sectionB;
            // Body phrases align by phase; transition hits align by section start/end.
            // Do not compare a shifted final motif merely because a section got longer.
            for(int bar=0;bar<4;bar++) for(int id=0;id<25;id++) for(int step=0;step<16;step++) {
                int aa=a==null?step:(a.sections[sa].start+(id>=21?(bar%2==0?0:a.sections[sa].bars-1):bar%a.sections[sa].bars))*16+step;
                int bb=b==null?step:(b.sections[sb].start+(id>=21?(bar%2==0?0:b.sections[sb].bars-1):bar%b.sections[sb].bars))*16+step;
                int na,va,nb,vb;Resolve(a,a!=null&&a.version==2?a.sourceGroove:source,id,aa,out na,out va);
                Resolve(b,b!=null&&b.version==2?b.sourceGroove:source,id,bb,out nb,out vb);
                if(va==0&&vb==0)continue;int family=id<6?0:id<8?1:2;union[family]++;
                if(va==0||vb==0||na!=nb)changed[family]++;
                // Velocity affects playback but cannot qualify an otherwise identical musical family.
            }
        }
        var s=new Score {melody=changed[0]/Math.Max(1,union[0]),lowEnd=changed[1]/Math.Max(1,union[1]),percussion=changed[2]/Math.Max(1,union[2])};
        s.overall=(s.melody+s.lowEnd+s.percussion)/3;return s;
    }
    public static bool Passes(Score score) {return score.overall>=OverallMinimum&&score.melody>=FamilyMinimum&&score.lowEnd>=FamilyMinimum&&score.percussion>=FamilyMinimum;}
    public static bool PassesSource(Score score) {return score.overall>=SourceOverallMinimum&&score.melody>=SourceFamilyMinimum&&score.lowEnd>=SourceFamilyMinimum&&score.percussion>=SourceFamilyMinimum;}
    public static BacardiSongVariation.Plan Generate(int seed,int personality,Groove source,BacardiSongVariation.Plan previous) {
        for(int attempt=0;attempt<32;attempt++) {
            var p=Create(unchecked(seed+attempt*104729),personality,source);
            p.sourceScore=Compare(null,p,source);p.previousScore=previous==null?null:Compare(previous,p,source);
            p.hasPreviousScore=previous!=null;
            p.dropScore=Compare(p,p,source,2,4);
            if(PassesSource(p.sourceScore)&&(p.previousScore==null||Passes(p.previousScore))&&p.dropScore.overall>=DropMinimum) return p;
        }
        throw new InvalidOperationException("STRONG could not meet musical-event thresholds in 32 deterministic attempts. Current arrangement retained.");
    }
}
