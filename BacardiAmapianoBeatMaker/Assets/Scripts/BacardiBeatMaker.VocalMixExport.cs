using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public partial class BacardiBeatMaker {
    // Dedicated dry mix export. Existing instrumental/stem/master paths are untouched.
    public string ExportArrangedStereoWithVocals(string outputFolder=null) {
        if(!ArrangementApplied) { status="Apply the variation before exporting stereo + vocals."; return null; }
        try {
            var rendered=BuildArrangedVocalMix();
            var mix=rendered.Pcm; var info=rendered.Metadata;
            string folder=outputFolder ?? ExportFolder();
            string path=Path.Combine(folder,"Arranged Stereo + Vocals.wav"),metadata=Path.Combine(folder,"Arranged Stereo + Vocals.txt");
            if(File.Exists(path)||File.Exists(metadata)) throw new IOException("Existing combined export files are protected; choose a new folder.");
            Directory.CreateDirectory(folder); WriteArrangedWav(path,mix);
            using(var writer=new StreamWriter(new FileStream(metadata,FileMode.CreateNew,FileAccess.Write))) writer.Write(info.ToString());
            status="Arranged stereo + vocals exported ("+rendered.PlacementCount+" placements): "+folder;
            return folder;
        } catch(Exception e) { status="Stereo + vocals export failed: "+e.Message; return null; }
    }
}
