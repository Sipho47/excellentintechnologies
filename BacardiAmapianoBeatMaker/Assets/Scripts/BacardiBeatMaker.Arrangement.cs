using System;
using System.IO;
using UnityEngine;

public partial class BacardiBeatMaker {
    AudioSource arrangedSource;
    AudioClip arrangedClip;
    bool arrangedPreviewRunning;
    bool arrangedListenerOverride, priorUnprocessedPlayback;
    public bool ArrangementApplied { get { return arrangementApplied && appliedSongPlan != null; } }
    int ArrangedFrames { get { return (int)Math.Ceiling(appliedSongPlan.bars * 240.0 / bpm * Rate); } }
    public int ArrangementLength { get { return ArrangementApplied ? appliedSongPlan.bars : 0; } }
    public string ActiveVariation { get { return ArrangementApplied ? BacardiSongVariation.Personalities[appliedSongPlan.personality]+" / "+BacardiSongVariation.Amounts[appliedSongPlan.amount]+" / Seed "+appliedSongPlan.seed : "NONE"; } }
    public readonly System.Collections.Generic.Dictionary<string,bool> VariationControlEnabled = new System.Collections.Generic.Dictionary<string,bool>();
    public void ApplyVariationToSong() {
        if(songPlan==null) {
            try { songPlan=JsonUtility.FromJson<BacardiSongVariation.Plan>(PlayerPrefs.GetString("BacardiSongVariation","")); }
            catch(Exception e) { status="Apply failed: "+e.Message; return; }
        }
        if(!ValidSongPlan(songPlan)) { status="Generate or load a valid variation first."; return; }
        Stop();
        // Freeze a copy of the saved plan, without changing the planning algorithm.
        appliedSongPlan=JsonUtility.FromJson<BacardiSongVariation.Plan>(JsonUtility.ToJson(songPlan));
        arrangementApplied=true;
        vocalSongLengthDetached=false; SyncVocalSongLength();
        status="Arrangement applied: "+songPlan.bars+" bars. Preview before exporting.";
    }
    void StopArrangedPreview() {
        arrangedPreviewRunning=false;
        if(arrangedSource!=null) arrangedSource.Stop();
        if(arrangedSource!=null) arrangedSource.clip=null;
        if(arrangedClip!=null) { Destroy(arrangedClip); arrangedClip=null; }
        if(arrangedListenerOverride) {
            if(lufsMeter!=null) lufsMeter.PostArrangementPlayback=priorUnprocessedPlayback;
            arrangedListenerOverride=false;
        }
    }
    public void PreviewArrangedSong() {
        if(!ArrangementApplied) { status="Apply the variation first."; return; }
        Stop();
        if(aiVocals!=null) aiVocals.StopPreview();
        try {
            var rendered=BuildArrangedVocalMix(); var pcm=rendered.Pcm;
            arrangedClip=AudioClip.Create("Arranged song",pcm.Length/2,2,Rate,false);
            if(!arrangedClip.SetData(pcm,0)) throw new InvalidDataException("Could not load arranged preview PCM.");
            if(arrangedSource==null) { arrangedSource=gameObject.AddComponent<AudioSource>(); arrangedSource.playOnAwake=false; }
            arrangedSource.clip=arrangedClip; arrangedSource.volume=1; arrangedSource.pitch=1; arrangedSource.loop=false;
            if(lufsMeter!=null) {
                priorUnprocessedPlayback=lufsMeter.PostArrangementPlayback;
                arrangedListenerOverride=true; lufsMeter.PostArrangementPlayback=true;
            }
            arrangedSource.spatialBlend=0; arrangedSource.Play(); arrangedPreviewRunning=true; playing=true;
            status="Previewing full "+SongBars+"-bar arrangement with "+rendered.PlacementCount+" vocal placements (unmastered).";
        } catch(Exception e) { StopArrangedPreview(); playing=false; status="Arranged preview failed: "+e.Message; }
    }
    // A zero permission bar is silent, including tails from the preceding section.
    // Within active bars, keep natural sample tails and the planner's note permissions.
    void SilenceInactiveBars(int id,float[] pcm) {
        for(int bar=0;bar<appliedSongPlan.bars;bar++) if(appliedSongPlan.masks[bar*25+id]==0) {
            int start=(int)Math.Round(bar*240.0/bpm*Rate);
            int end=Math.Min(pcm.Length/2,(int)Math.Round((bar+1)*240.0/bpm*Rate));
            Array.Clear(pcm,start*2,(end-start)*2);
        }
    }
    // Float WAV preserves the existing mix exactly, including floating-point headroom.
    // The post-arrangement importer already supports IEEE float32 stereo WAV.
    static void WriteArrangedWav(string path,float[] pcm) {
        using(var w=new BinaryWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write))) {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36+pcm.Length*4);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
            w.Write((short)3); w.Write((short)2); w.Write(Rate); w.Write(Rate*8);
            w.Write((short)8); w.Write((short)32);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(pcm.Length*4);
            foreach(float v in pcm) if(float.IsNaN(v)||float.IsInfinity(v)) throw new InvalidDataException("Invalid audio.");
            var block=new byte[65536];
            for(int offset=0;offset<pcm.Length;offset+=block.Length/4) {
                int count=Math.Min(block.Length,(pcm.Length-offset)*4);
                Buffer.BlockCopy(pcm,offset*4,block,0,count); w.Write(block,0,count);
            }
        }
    }
    public string ExportArrangedSong(string kind,string outputFolder=null) {
        if(!ArrangementApplied) { status="Apply the variation before arranged exports."; return null; }
        try {
            if(kind!="STEMS" && kind!="MIDI" && kind!="STEREO") throw new ArgumentException("Unknown arranged export.");
            string folder=outputFolder ?? ExportFolder(); Directory.CreateDirectory(folder);
            if(kind=="STEMS") for(int id=0;id<25;id++) WriteArrangedWav(Path.Combine(folder,(id+1).ToString("00")+" "+Names[id]+".wav"),RenderSound(id,ArrangedFrames));
            if(kind=="STEREO") WriteArrangedWav(Path.Combine(folder,"Arranged Stereo.wav"),ArrangementMix());
            WriteArrangedMidi(Path.Combine(folder,"Arranged Song.mid"));
            File.WriteAllText(Path.Combine(folder,"SONG_VARIATION.json"),JsonUtility.ToJson(appliedSongPlan,true));
            File.WriteAllText(Path.Combine(folder,"IMPORT.txt"),SongBars+" bars / BPM "+bpm+". Align files at Bar 1. Stereo float32 44100 Hz; original mix gain, no normalization or mastering. Import Arranged Stereo.wav into POST-ARRANGEMENT MASTER TEST. MIDI contains all 25 named stem tracks plus tempo/section markers; assign instruments/samples. Swing is baked in.");
            status="Arranged "+kind+" exported: "+folder; return folder;
        } catch(Exception e) { status="Arranged export failed: "+e.Message; return null; }
    }
    public void ReturnToOneBarGroove() { Stop(); appliedSongPlan=null; arrangementApplied=false; vocalSongLengthDetached=true; SyncVocalSongLength(); status="ONE-BAR GROOVE active. The planned variation is retained for reapplying."; }
    void WriteArrangedMidi(string path) {
        const int ppq=960,barTicks=3840;
        using(var file=new BinaryWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write))) {
            file.Write(System.Text.Encoding.ASCII.GetBytes("MThd")); BigEndian(file,6,4);
            BigEndian(file,1,2); BigEndian(file,26,2); BigEndian(file,ppq,2);
            MidiTrack(file,w=> {
                w.Write(new byte[]{0,255,81,3}); BigEndian(w,(int)Math.Round(60000000.0/bpm),3);
                w.Write(new byte[]{0,255,88,4,4,2,24,8}); int last=0;
                foreach(var section in appliedSongPlan.sections) {
                    int tick=section.start*barTicks; VariableLength(w,tick-last); last=tick;
                    var name=System.Text.Encoding.UTF8.GetBytes(section.name); w.Write(new byte[]{255,6}); VariableLength(w,name.Length); w.Write(name);
                }
                VariableLength(w,SongBars*barTicks-last); w.Write(new byte[]{255,47,0});
            });
            for(int id=0;id<25;id++) {
                int stem=id;
                MidiTrack(file,w=> {
                    var name=System.Text.Encoding.UTF8.GetBytes(Names[stem]); w.Write(new byte[]{0,255,3}); VariableLength(w,name.Length); w.Write(name);
                    int last=0,channel=stem<8 ? stem : 9;
                    int[] drums={36,39,42,46,82,60,61,64,61,54,37,62,41,49,55,81,35};
                    for(int eventStep=0;eventStep<SongSteps;eventStep++) {
                        int source=SongSourceStep(stem,eventStep); if(source<0) continue;
                        int tick=(int)Math.Round((eventStep+(eventStep%2==1 ? swing : 0))*ppq/4.0);
                        int end=(int)Math.Round((eventStep+1+(eventStep%2==0 ? swing : 0))*ppq/4.0);
                        int root=stem<8 ? Roots[stem]+noteOffsets[stem,source] : drums[stem-8];
                        var notes=stem==2 || stem==4 ? new[]{root,root+4,root+7} : new[]{root};
                        foreach(int note in notes) { VariableLength(w,tick-last); last=tick; w.Write((byte)(144+channel)); w.Write((byte)note); w.Write((byte)Mathf.Clamp(velocities[stem,source],1,127)); }
                        foreach(int note in notes) { VariableLength(w,end-last); last=end; w.Write((byte)(128+channel)); w.Write((byte)note); w.Write((byte)0); }
                    }
                    VariableLength(w,SongBars*barTicks-last); w.Write(new byte[]{255,47,0});
                });
            }
        }
    }
    void DrawAppliedSongVariation(GUIStyle style) {
        GUILayout.Label("APPLY / EXPORT SONG VARIATION",label);
        VariationStatus("APPLIED","ARRANGEMENT APPLIED: "+(ArrangementApplied?"YES":"NO"));
        VariationStatus("LENGTH","ARRANGEMENT LENGTH: "+ArrangementLength+" bars");
        VariationStatus("ACTIVE VARIATION","ACTIVE VARIATION: "+ActiveVariation);
        if(GUILayout.Button("APPLY VARIATION TO SONG",GUILayout.Height(36))) ApplyVariationToSong();
        VariationBounds("APPLY VARIATION TO SONG");
        bool enabled=GUI.enabled; GUI.enabled=enabled && ArrangementApplied;
        if(GUILayout.Button("PREVIEW ARRANGED SONG",GUILayout.Height(36))) PreviewArrangedSong();
        VariationBounds("PREVIEW ARRANGED SONG");
        if(Event.current.type==EventType.Repaint) VariationControlEnabled["PREVIEW ARRANGED SONG"]=GUI.enabled;
        foreach(string kind in new[]{"STEMS","MIDI","STEREO WAV"}) {
            string caption="EXPORT ARRANGED "+kind;
            if(GUILayout.Button(caption,GUILayout.Height(36))) ExportArrangedSong(kind=="STEREO WAV"?"STEREO":kind);
            VariationBounds(caption);
            if(Event.current.type==EventType.Repaint) VariationControlEnabled[caption]=GUI.enabled;
        }
        const string vocalExport="EXPORT ARRANGED STEREO + VOCALS WAV";
        if(GUILayout.Button(vocalExport,GUILayout.Height(36))) ExportArrangedStereoWithVocals();
        VariationBounds(vocalExport);
        if(Event.current.type==EventType.Repaint) VariationControlEnabled[vocalExport]=GUI.enabled;
        GUI.enabled=enabled;
    }
}
