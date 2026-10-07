using System;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Globalization;
using UnityEngine;

public static class BacardiLoudness {
    // Verify the completed WAV independently through FFmpeg when installed.
    // Export does not rely on a dynamically compressing loudnorm stage.
    public static string Verify(string path) {
        string tools=Path.GetFullPath(Path.Combine(Application.dataPath,"..","Tools"));
        string[] executables=Directory.Exists(tools) ? Directory.GetFiles(tools,"ffmpeg.exe",SearchOption.AllDirectories) : new string[0];
        if(executables.Length==0) return VerifyPcm(path);
        using(var process=new Process()) {
            process.StartInfo=new ProcessStartInfo(executables[0],"-hide_banner -nostdin -i \""+path+"\" -af ebur128=peak=true -f null -") {
                UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true
            };
            process.Start(); string log=process.StandardError.ReadToEnd(); process.WaitForExit();
            if(process.ExitCode!=0) throw new Exception("WAV verification failed: "+log);
            int summaryStart=log.LastIndexOf("Summary:",StringComparison.Ordinal);
            if(summaryStart<0) throw new Exception("WAV verification returned no summary.");
            var summary=log.Substring(summaryStart);
            var i=Regex.Match(summary,@"I:\s*([-+\d.]+) LUFS");
            var tp=Regex.Match(summary,@"Peak:\s*([-+\d.]+) dBFS");
            if(!i.Success||!tp.Success) throw new Exception("WAV verification returned no measurement.");
            double integrated=double.Parse(i.Groups[1].Value,CultureInfo.InvariantCulture);
            double peak=double.Parse(tp.Groups[1].Value,CultureInfo.InvariantCulture);
            if(peak > -1) throw new Exception("Export exceeded -1 dBTP: "+peak);
            return string.Format(CultureInfo.InvariantCulture,"Export LUFS-I: {0:F1} | TP: {1:F1} dBTP{2}",integrated,peak,
                Math.Abs(integrated+14)>.2 ? " / safety-limited; target not reached" : "");
        }
    }
    static string VerifyPcm(string path) {
        using(var reader=new BinaryReader(File.OpenRead(path))) {
            reader.BaseStream.Position=44;
            var pcm=new float[(reader.BaseStream.Length-44)/3];
            for(int n=0;n<pcm.Length;n++) {
                int value=reader.ReadByte() | reader.ReadByte()<<8 | reader.ReadByte()<<16;
                if((value&0x800000)!=0) value|=unchecked((int)0xff000000);
                pcm[n]=value/8388607f;
            }
            var peak=new BacardiTruePeak(44100,2); peak.Observe(pcm,2);
            double tp=20*Math.Log10(Math.Max(1e-20,peak.Peak));
            if(tp > -1) throw new Exception("Export exceeded -1 dBTP.");
            double integrated=BacardiMastering.Measure(pcm,44100);
            return string.Format(CultureInfo.InvariantCulture,"Export LUFS-I: {0:F1} | TP: {1:F1} dBTP (internal verification){2}",integrated,tp,
                Math.Abs(integrated+14)>.2 ? " / safety-limited; target not reached" : "");
        }
    }
}


