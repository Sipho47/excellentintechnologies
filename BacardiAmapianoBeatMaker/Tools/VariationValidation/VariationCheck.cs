using System;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
public static class VariationCheck {
    static int checks;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); checks++; Console.WriteLine("PASS: "+name); }
    static string Signature(BacardiSongVariation.Plan p) { return p.bars+":"+string.Join(",",p.sections.Select(s=>s.start+"/"+s.bars+"/"+s.stems+"/"+s.lead))+":"+string.Join(",",p.masks); }
    public static void Main() {
        var audio=Directory.GetFiles("Assets","*.wav",SearchOption.AllDirectories).ToDictionary(x=>x,x=>Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(x))));
        var unique=new System.Collections.Generic.HashSet<string>();
        for(int profile=0;profile<7;profile++) for(int amount=0;amount<3;amount++) {
            BacardiSongVariation.Plan previous=null;
            for(int seed=0;seed<40;seed++) {
                var raw=BacardiSongVariation.Create(seed,profile,amount);
                Check(Signature(raw)==Signature(BacardiSongVariation.Create(seed,profile,amount)),"deterministic profile "+profile+" amount "+amount+" seed "+seed);
                double distance;
                var p=BacardiSongVariation.Generate(seed,profile,amount,previous,out distance);
                Check(Signature(p)==Signature(BacardiSongVariation.Create(p.seed,profile,amount)),"accepted seed replays independently of previous plan");
                Check(previous==null || distance >= (amount==2 ? .38 : amount==1 ? .20 : .08),"similarity rejection threshold");
                Check(p.sections[0].start==0 && p.sections.Last().start+p.sections.Last().bars==p.bars && p.masks.Length==p.bars*25,"contiguous sections and complete masks");
                Check(p.sections.All(s=>s.stems != (1u<<25)-1),"never all 25 layers in a section");
                Check(p.sections.Where(s=>s.logEntry>=0).All(s=>Enumerable.Range(s.start,s.logEntry-s.start).All(bar=>p.masks[bar*25+6]==0)),"logdrum delayed entrances");
                Check(p.sections.Where(s=>s.bassEntry>=0).All(s=>Enumerable.Range(s.start,s.bassEntry-s.start).All(bar=>p.masks[bar*25+7]==0)),"bass delayed entrances");
                if(amount==2 && previous!=null) Check(p.bars!=previous.bars || p.sections.Where((s,i)=>s.start!=previous.sections[i].start || s.bars!=previous.sections[i].bars).Any(),"STRONG changes section structure");
                unique.Add(Signature(p)); previous=p;
            }
        }
        Check(unique.Count>750,"multiple seeds create distinct arrangements ("+unique.Count+")");
        Check(audio.All(x=>x.Value==Convert.ToBase64String(SHA256.HashData(File.ReadAllBytes(x.Key)))) ,"all "+audio.Count+" source WAV files unchanged");
        Console.WriteLine(checks+" checks passed; no provider/network calls.");
    }
}
