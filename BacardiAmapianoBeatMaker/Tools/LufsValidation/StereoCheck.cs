using System;
using System.IO;
public static class StereoCheck {
    static int count;
    static void Check(bool ok,string name) { if(!ok) throw new Exception(name); Console.WriteLine("PASS: "+name); count++; }
    public static void Main() {
        string dir="Tools/LufsValidation/StereoFixtures"; Directory.CreateDirectory(dir);
        string src=Path.Combine(dir,"source-"+Guid.NewGuid()+".wav");
        var pcm=new float[48000*2*4];
        for(int f=0;f<pcm.Length/2;f++) pcm[f*2]=pcm[f*2+1]=(float)(.1*Math.Sin(2*Math.PI*997*f/48000));
        BacardiStereoMaster.WriteWav(src,pcm,48000); byte[] bytes=File.ReadAllBytes(src);
        var s=new BacardiStereoMaster(); s.Import(src);
        Check(s.Rate==48000&&s.PlaybackCopy().Length==pcm.Length&&!s.Active,"stereo WAV import / bypass");
        var m=s.Analyze(); Check(Math.Abs(m.Integrated+20)<3&&!double.IsNaN(m.ShortTerm),"full song LUFS / short-term analysis");
        Check(Math.Abs(m.Peak+20)<.01&&m.TruePeak>=m.Peak,"sample and reconstructed peak analysis");
        s.SetSpotify(); Check(Math.Abs(s.After.Integrated+14)<.2&&s.GainDb>0,"gain adjustment to -14 LUFS");
        Check(s.After.TruePeak<=-1,"default limiter ceiling");
        string output=Path.Combine(dir,"master-"+Guid.NewGuid()+".wav"); s.Export(output);
        int rate; var exported=BacardiStereoMaster.ReadWav(output,out rate);
        Check(rate==48000&&exported.Length==pcm.Length&&BacardiStereoMaster.Measure(exported,rate).TruePeak<=-1,"24-bit stereo export / ceiling");
        bool rejected=false; try { s.Export(src); } catch(IOException) { rejected=true; }
        Check(rejected,"source overwrite rejected");
        rejected=false; try { s.Export(output); } catch(IOException) { rejected=true; }
        Check(rejected,"existing file overwrite rejected");
        Check(Convert.ToBase64String(bytes)==Convert.ToBase64String(File.ReadAllBytes(src)),"original file byte-for-byte unchanged");
        s.Active=false; Check(Math.Abs(s.PlaybackCopy()[100]-pcm[100])<.000001,"bypass preserves original PCM");
        // High crest-factor fixture exercises limiting and configurable ceilings.
        for(int n=0;n<pcm.Length;n++) pcm[n]*=2;
        for(int n=100;n<pcm.Length;n+=9600) { pcm[n]=.99f; pcm[n+1]=-.99f; }
        string hot=Path.Combine(dir,"hot-"+Guid.NewGuid()+".wav"); BacardiStereoMaster.WriteWav(hot,pcm,48000);
        s.Import(hot); s.SetSpotify(-3); Check(s.After.TruePeak<=-3,"configurable -3 dBTP limiter ceiling with transients");
        s.Export(Path.Combine(dir,"limited-"+Guid.NewGuid()+".wav"));
        // Low integrated loudness with sparse high peaks, like the reported song.
        var crest=new float[48000*2*12];
        double tone=.12;
        for(int pass=0;pass<3;pass++) {
            for(int f=0;f<crest.Length/2;f++) crest[f*2]=crest[f*2+1]=(float)(tone*Math.Sin(2*Math.PI*997*f/48000));
            for(int f=48000;f<crest.Length/2;f+=48000*3) crest[f*2]=crest[f*2+1]=(float)Math.Pow(10,-1.15/20);
            var measured=BacardiStereoMaster.Measure(crest,48000);
            tone*=Math.Pow(10,(-18.8-measured.Integrated)/20);
        }
        string regression=Path.Combine(dir,"Low-LUFS High-Peak Source.wav");
        string regressionMaster=Path.Combine(dir,"Low-LUFS High-Peak Master.wav");
        // Repeatable fixture paths live only in the test artifact folder.
        if(File.Exists(regression)) File.Delete(regression);
        if(File.Exists(regressionMaster)) File.Delete(regressionMaster);
        BacardiStereoMaster.WriteWav(regression,crest,48000);
        byte[] sourceHash; using(var sha=System.Security.Cryptography.SHA256.Create()) sourceHash=sha.ComputeHash(File.ReadAllBytes(regression));
        s.Import(regression); s.SetSpotify(-1);
        Console.WriteLine("Regression BEFORE: "+s.Before+" AFTER: "+s.After+" Gain: "+s.GainDb+" GR: "+s.MaxReductionDb);
        Check(Math.Abs(s.Before.Integrated+18.8)<.3 && s.Before.TruePeak> -1.3,"low-LUFS / high-peak fixture matches reported source");
        Check(s.TargetReached && Math.Abs(s.After.Integrated+14)<=.2 && s.After.TruePeak<=-1,"high-crest song reaches -14 LUFS with -1 dBTP ceiling");
        Check(s.GainDb>4 && s.MaxReductionDb>3,"required gain applied and actual transient limiting exceeds former cap");
        s.Export(regressionMaster);
        using(var sha=System.Security.Cryptography.SHA256.Create()) Check(Convert.ToBase64String(sourceHash)==Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(regression))),"regression source SHA256 unchanged");
        var silence=new float[48000]; Check(double.IsNaN(BacardiStereoMaster.Measure(silence,48000).Integrated),"silence analysis");
        Console.WriteLine(count+" checks passed.");
    }
}

