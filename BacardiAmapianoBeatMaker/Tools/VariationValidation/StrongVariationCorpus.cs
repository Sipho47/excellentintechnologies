using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
public static class StrongVariationCorpus {
    static int checks;
    static void Check(bool value,string message) {checks++;if(!value)throw new Exception(message);}
    static BacardiVariationEvents.Groove Fixture(int variant) {
        var g=new BacardiVariationEvents.Groove {notes=new int[400],velocities=new int[400]};
        for(int id=0;id<25;id++) for(int s=0;s<16;s++) {
            bool on=id<5?s==(id*2)%16: id==6?(s==0||s==3||s==6||s==7||s==10||s==14):id==7?s==0||s==10:
                id==8?s%4==0:id==9?s==4||s==12:id==10?s%2==0:id==12?true:s==(id*3)%16;
            if(variant==1&&id<5) on|=s==8||s==12;
            if(variant==2&&id>=13&&id<21)on|=s==5||s==9;
            int n=id*16+s;g.velocities[n]=on?(id<5?65:90):0;
            g.notes[n]=id<8?(variant==1?2:variant==2?-2:0)+(id==6&&s%3==0?7:0):0;
        }
        return g;
    }
    static void Exact(BacardiSongVariation.Plan a,BacardiSongVariation.Plan b) {
        Check(a.seed==b.seed&&a.bars==b.bars,"Replay structure");
        for(int n=0;n<a.eventNotes.Length;n++) if(a.eventNotes[n]!=b.eventNotes[n]||a.eventVelocities[n]!=b.eventVelocities[n]) throw new Exception("Replay events");checks++;
    }
    public static void Main() {
        var csv=new StringBuilder("fixture,personality,requestedSeed,acceptedSeed,subtle,medium,strong,melody,lowEnd,percussion,previous,dropContrast\n");
        double[] sum=new double[3];double min=1,max=0,minMel=1,minLow=1,minPerc=1,minPrev=1,minDrop=1;
        int count=0,retries=0;double maxAttempt=0,minPrevFamily=1;double[,] families=new double[3,3]; var errors=new List<string>();
        for(int fixture=0;fixture<3;fixture++)for(int personality=0;personality<7;personality++) {
            var source=Fixture(fixture);BacardiSongVariation.Plan previous=null;
            for(int i=0;i<16;i++) {
                int seed=unchecked(-1700000000+i*7919+fixture*100003+personality*101);
                var subtle=BacardiSongVariation.Create(seed,personality,0);var medium=BacardiSongVariation.Create(seed,personality,1);
                BacardiSongVariation.Plan p;
                try{p=BacardiVariationEvents.Generate(seed,personality,source,previous);}catch(Exception e){errors.Add(fixture+"/"+personality+"/"+seed+": "+e.Message);continue;}
                Exact(p,BacardiVariationEvents.Create(p.seed,personality,source));Check(BacardiVariationEvents.ValidEvents(p),"Valid events");
                Check(BacardiVariationEvents.PassesSource(p.sourceScore)&&(p.previousScore==null||BacardiVariationEvents.Passes(p.previousScore)),"Accepted score gates");
                Check(p.dropScore.overall>=BacardiVariationEvents.DropMinimum,"Drop contrast gate");
                double a=BacardiVariationEvents.Compare(null,subtle,source).overall,b=BacardiVariationEvents.Compare(null,medium,source).overall,c=p.sourceScore.overall;
                var scores=new[]{BacardiVariationEvents.Compare(null,subtle,source),BacardiVariationEvents.Compare(null,medium,source),p.sourceScore};
                for(int level=0;level<3;level++){families[level,0]+=scores[level].melody;families[level,1]+=scores[level].lowEnd;families[level,2]+=scores[level].percussion;}
                sum[0]+=a;sum[1]+=b;sum[2]+=c;min=Math.Min(min,c);max=Math.Max(max,c);
                minMel=Math.Min(minMel,p.sourceScore.melody);minLow=Math.Min(minLow,p.sourceScore.lowEnd);minPerc=Math.Min(minPerc,p.sourceScore.percussion);
                if(p.previousScore!=null){minPrev=Math.Min(minPrev,p.previousScore.overall);minPrevFamily=Math.Min(minPrevFamily,Math.Min(p.previousScore.melody,Math.Min(p.previousScore.lowEnd,p.previousScore.percussion)));}minDrop=Math.Min(minDrop,p.dropScore.overall);
                long attempt=unchecked(p.seed-seed)/104729;maxAttempt=Math.Max(maxAttempt,attempt+1);if(attempt>0)retries++;
                csv.AppendLine(string.Join(",",fixture,personality,seed,p.seed,a.ToString("F6",CultureInfo.InvariantCulture),b.ToString("F6",CultureInfo.InvariantCulture),c.ToString("F6",CultureInfo.InvariantCulture),p.sourceScore.melody.ToString("F6",CultureInfo.InvariantCulture),p.sourceScore.lowEnd.ToString("F6",CultureInfo.InvariantCulture),p.sourceScore.percussion.ToString("F6",CultureInfo.InvariantCulture),p.previousScore==null?"NA":p.previousScore.overall.ToString("F6",CultureInfo.InvariantCulture),p.dropScore.overall.ToString("F6",CultureInfo.InvariantCulture)));
                previous=p;count++;
            }
        }
        Directory.CreateDirectory("Tools/VariationValidation");File.WriteAllText("Tools/VariationValidation/strong-corpus.csv",csv.ToString());
        string report=string.Format(CultureInfo.InvariantCulture,"Accepted {0}/336; failures {1}; retries {2}; maximum attempts {3}\nMean source score: SUBTLE {4:F6}, MEDIUM {5:F6}, STRONG {6:F6}\nSTRONG source range {7:F6}–{8:F6}; family minima melody {9:F6}, low end {10:F6}, percussion {11:F6}\nPrevious minimum {12:F6}; Drop contrast minimum {13:F6}\nChecks {14}\n",count,errors.Count,retries,maxAttempt,sum[0]/Math.Max(1,count),sum[1]/Math.Max(1,count),sum[2]/Math.Max(1,count),min,max,minMel,minLow,minPerc,minPrev,minDrop,checks);
        report+="Previous family minimum "+minPrevFamily.ToString("F6",CultureInfo.InvariantCulture)+"\n";
        for(int level=0;level<3;level++)report+=string.Format(CultureInfo.InvariantCulture,"{0} family means: melody {1:F6}, low end {2:F6}, percussion {3:F6}\n",BacardiSongVariation.Amounts[level],families[level,0]/count,families[level,1]/count,families[level,2]/count);
        File.WriteAllText("Tools/VariationValidation/strong-corpus-results.txt",report+string.Join("\n",errors));Console.WriteLine(report);
        Check(errors.Count==0,"Corpus generation failures");Check(sum[2]/count>sum[1]/count+.10&&sum[2]/count>sum[0]/count+.10,"STRONG needs substantial source-event separation from MEDIUM/SUBTLE");
        Console.WriteLine("PASS fixed corpus; no audio IO, provider calls, user data or PART 10.");
    }
}
