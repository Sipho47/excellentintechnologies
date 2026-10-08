using System;
using UnityEngine;

public partial class BacardiBeatMaker
{
    BacardiSongVariation.Plan songPlan;
    BacardiSongVariation.Plan appliedSongPlan;
    bool arrangementApplied;
    bool vocalSongLengthDetached;
    void SyncVocalSongLength() {
        if(aiVocals==null) return;
        int bars=ArrangementApplied?appliedSongPlan.bars:!vocalSongLengthDetached&&ValidSongPlan(songPlan)?songPlan.bars:0;
        aiVocals.SyncSongBars(bars);
    }
    int songStep, variationPersonality, variationAmount = 2;
    string variationSeed = "104", variationDifference = "HIGH (first plan)";
    int SongBars { get { return appliedSongPlan == null ? 1 : appliedSongPlan.bars; } }
    int SongSteps { get { return SongBars * 16; } }

    static bool ValidSongPlan(BacardiSongVariation.Plan p) {
        if(p == null || (p.version != 1 && p.version != 2) || p.personality < 0 || p.personality > 6 || p.amount < 0 || p.amount > 2 ||
            p.bars < 1 || p.bars > 100 || p.sections == null || p.sections.Length != 6 || p.masks == null || p.masks.Length != p.bars * 25) return false;
        int start=0;
        foreach(var s in p.sections) {
            if(s == null || s.start != start || s.bars < 1 || s.lead < 0 || s.lead > 4) return false;
            start += s.bars;
        }
        return start == p.bars && (p.version==1 || BacardiVariationEvents.ValidEvents(p));
    }
    // Single event resolver shared by live playback, PCM, duration and MIDI.
    int SongSourceStep(int id, int absoluteStep) {
        int step = absoluteStep % 16;
        if(appliedSongPlan == null) return steps[id,step] ? step : -1;
        if(appliedSongPlan.version==2) return appliedSongPlan.eventVelocities[absoluteStep/16*400+id*16+step]>0?step:-1;
        if(!appliedSongPlan.Allows(id,absoluteStep)) return -1;
        if(id < 21) return steps[id,step] ? step : -1;
        // Reposition transition hits, preserving their original pitch and velocity.
        for(int s=0;s<16;s++) if(steps[id,s]) return s;
        return -1;
    }
    int SongNote(int id,int absoluteStep,int source) {
        return appliedSongPlan!=null&&appliedSongPlan.version==2 ? appliedSongPlan.eventNotes[absoluteStep/16*400+id*16+absoluteStep%16] : noteOffsets[id,source];
    }
    int SongVelocity(int id,int absoluteStep,int source) {
        return appliedSongPlan!=null&&appliedSongPlan.version==2 ? appliedSongPlan.eventVelocities[absoluteStep/16*400+id*16+absoluteStep%16] : velocities[id,source];
    }
    float SongPitch(int id,int absoluteStep,int source) {return id<8?Mathf.Pow(2,SongNote(id,absoluteStep,source)/12f):1;}
    string EventScoreText(BacardiSongVariation.Plan p) {
        var source=BacardiVariationEvents.Capture(steps,noteOffsets,velocities);
        // Old v1 JSON has no recorded scores; Unity may deserialize missing nested objects as zeros.
        var sourceScore=p.version==2&&p.sourceScore!=null?p.sourceScore:BacardiVariationEvents.Compare(null,p,source);
        var dropScore=p.version==2&&p.dropScore!=null?p.dropScore:BacardiVariationEvents.Compare(p,p,source,2,4);
        return "\nSOURCE: "+sourceScore+"\nPREVIOUS: "+(!p.hasPreviousScore?"N/A (not recorded)":p.previousScore.ToString())+"\nDROP CONTRAST: "+dropScore;
    }
    double SongEnd() {
        if(appliedSongPlan != null) return 60.0/bpm*4*SongBars;
        double end=60.0/bpm*4*SongBars, time=0;

        for(int eventStep=0;eventStep<SongSteps;eventStep++) {
            for(int id=0;id<25;id++) {
                int source=SongSourceStep(id,eventStep);
                if(source >= 0) end=Math.Max(end,time+clips[id].length/StepPitch(id,source));
            }
            time += Duration(eventStep%16);
        }
        return end;
    }
    public void GenerateNewSongVariation(bool reuse = false) {
        int seed;
        if(!int.TryParse(variationSeed,out seed)) { status="SEED must be a signed 32-bit integer."; return; }
        try {
            double difference;
            bool same = songPlan != null && seed == songPlan.seed && variationPersonality == songPlan.personality && variationAmount == songPlan.amount;
            var source=BacardiVariationEvents.Capture(steps,noteOffsets,velocities);
            BacardiSongVariation.Plan next;
            if((reuse||same)&&songPlan!=null&&seed==songPlan.seed&&variationPersonality==songPlan.personality&&variationAmount==songPlan.amount)
                next=JsonUtility.FromJson<BacardiSongVariation.Plan>(JsonUtility.ToJson(songPlan));
            else if(variationAmount==2) next=BacardiVariationEvents.Generate(seed,variationPersonality,source,songPlan);
            else next=reuse||same?BacardiSongVariation.Create(seed,variationPersonality,variationAmount):BacardiSongVariation.Generate(seed,variationPersonality,variationAmount,songPlan,out difference);
            // Canonicalize Unity's nested null-object serialization once at creation, not on replay.
            if(next.version==2) next=JsonUtility.FromJson<BacardiSongVariation.Plan>(JsonUtility.ToJson(next));
            difference = BacardiVariationEvents.Compare(songPlan,next,source).overall;
            if(next.version==1&&!reuse&&!same) {next.sourceScore=BacardiVariationEvents.Compare(null,next,source);next.previousScore=songPlan==null?null:BacardiVariationEvents.Compare(songPlan,next,source);next.hasPreviousScore=songPlan!=null;next.dropScore=BacardiVariationEvents.Compare(next,next,source,2,4);}
            bool resume=playing; Stop(); appliedSongPlan=null; arrangementApplied=false; songPlan=next;
            vocalSongLengthDetached=false; SyncVocalSongLength();
            variationSeed=next.seed.ToString();
            variationDifference=(difference>=.45?"HIGH":difference>=.20?"MEDIUM":"LOW")+(reuse || same ? " (seed replay)" : "")+EventScoreText(next);
            if(resume) StartPlayback();
            status="Song variation planned: "+songPlan.bars+" bars / seed "+variationSeed+". Apply the variation to enable arranged playback and exports.";
        } catch(Exception e) { status=e.Message; }
    }
    public readonly System.Collections.Generic.Dictionary<string,Rect> VariationControls = new System.Collections.Generic.Dictionary<string,Rect>();
    public int VariationRepaints { get; private set; }
    public Rect VariationViewport { get; private set; }
    public readonly System.Collections.Generic.Dictionary<string,string> VariationDisplayedText = new System.Collections.Generic.Dictionary<string,string>();
    public readonly System.Collections.Generic.Dictionary<string,Color> VariationButtonColors = new System.Collections.Generic.Dictionary<string,Color>();
    public static readonly Color VariationSelectedColor = new Color(.1f,.9f,.65f);
    public static readonly Color VariationUnselectedColor = new Color(.28f,.32f,.4f);
    public string VariationPreview { get { return songPlan == null ? "Current arrangement: ONE-BAR GROOVE (no variation planned)." : songPlan.Preview(Names); } }
    float SongVariationHeight(float width) {
        return 914 + Mathf.Max(0,label.CalcHeight(new GUIContent("VARIATION DIFFERENCE: "+variationDifference),width-24)-24) + Mathf.Ceil(7f/(width<760 ? 2 : 4))*38 + Mathf.Max(100,label.CalcHeight(new GUIContent(VariationPreview),width-24));
    }
    bool VariationSelector(string key,string text,bool selected,GUIStyle style,params GUILayoutOption[] options) {
        Color previous=GUI.backgroundColor;
        Color highlight=selected ? VariationSelectedColor : VariationUnselectedColor;
        GUI.backgroundColor=highlight;
        string caption=(selected ? "\u2713 " : "")+text;
        bool next=GUILayout.Toggle(selected,caption,style,options);
        VariationBounds(key);
        if(Event.current.type==EventType.Repaint) { VariationButtonColors[key]=highlight; VariationDisplayedText[key]=caption; }
        GUI.backgroundColor=previous;
        return next;
    }
    void VariationStatus(string key,string text) {
        GUILayout.Label(text,label,GUILayout.Height(24));
        VariationBounds(key);
        if(Event.current.type==EventType.Repaint) VariationDisplayedText[key]=text;
    }
    void VariationBounds(string key) {
        if(Event.current.type==EventType.Repaint) {
            Rect r=GUILayoutUtility.GetLastRect();
            VariationControls[key]=new Rect(GUIUtility.GUIToScreenPoint(r.position),r.size);
        }
    }
    void DrawSongVariation(float width) {
        GUILayout.BeginVertical(GUI.skin.box,GUILayout.Width(width),GUILayout.Height(SongVariationHeight(width)));
        GUILayout.Label("SONG VARIATION",label);
        VariationBounds("SONG VARIATION");
        GUILayout.Label("SONG PERSONALITY",label);
        var buttonStyle=new GUIStyle(GUI.skin.button) { wordWrap=true };
        int columns=width<760 ? 2 : 4;
        for(int row=0;row<(7+columns-1)/columns;row++) {
            GUILayout.BeginHorizontal();
            for(int col=0;col<columns;col++) {
                int i=row*columns+col;
                if(i<7) {
                    if(VariationSelector("PERSONALITY "+i,BacardiSongVariation.Personalities[i],variationPersonality==i,buttonStyle,GUILayout.Width((width-24)/columns),GUILayout.Height(36))) variationPersonality=i;
                }
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.Label("VARIATION AMOUNT",label);
        GUILayout.BeginHorizontal();
        for(int i=0;i<3;i++) {
            if(VariationSelector("AMOUNT "+i,BacardiSongVariation.Amounts[i],variationAmount==i,buttonStyle,GUILayout.Height(36))) variationAmount=i;
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("SEED",label);
        variationSeed=GUILayout.TextField(variationSeed,GUILayout.Height(32));
        VariationBounds("SEED");
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("NEW SEED",buttonStyle,GUILayout.Height(36))) variationSeed=BitConverter.ToInt32(Guid.NewGuid().ToByteArray(),0).ToString();
        VariationBounds("NEW SEED");
        if(GUILayout.Button("REUSE CURRENT SEED",buttonStyle,GUILayout.Height(36))) {
            if(songPlan!=null) variationSeed=songPlan.seed.ToString();
            GenerateNewSongVariation(true);
        }
        VariationBounds("REUSE CURRENT SEED");
        GUILayout.EndHorizontal();
        GUILayout.Space(6);
        VariationStatus("SELECTED PERSONALITY","SELECTED PERSONALITY: "+BacardiSongVariation.Personalities[variationPersonality]);
        VariationStatus("SELECTED AMOUNT","VARIATION AMOUNT: "+BacardiSongVariation.Amounts[variationAmount]);
        VariationStatus("CURRENT SEED","CURRENT SEED: "+variationSeed);
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("GENERATE NEW SONG VARIATION",GUILayout.Height(32))) {
            // Repeated generate clicks request a new plan; explicit replay retains the seed.
            int seed;
            if(songPlan != null && int.TryParse(variationSeed,out seed) && seed == songPlan.seed)
                variationSeed=unchecked(seed+104729).ToString();
            GenerateNewSongVariation();
        }
        VariationBounds("GENERATE NEW SONG VARIATION");
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        if(GUILayout.Button("SAVE VARIATION",GUILayout.Height(36))) {
            if(songPlan==null) status="Generate a variation before saving.";
            else { PlayerPrefs.SetString("BacardiSongVariation",JsonUtility.ToJson(songPlan)); PlayerPrefs.Save(); status="Song variation saved."; }
        }
        VariationBounds("SAVE VARIATION");
        if(GUILayout.Button("LOAD VARIATION",GUILayout.Height(36))) {
            try {
                var loaded=JsonUtility.FromJson<BacardiSongVariation.Plan>(PlayerPrefs.GetString("BacardiSongVariation",""));
                if(!ValidSongPlan(loaded)) status="No valid saved variation found.";
                else { Stop(); appliedSongPlan=null; arrangementApplied=false; songPlan=loaded; vocalSongLengthDetached=false; SyncVocalSongLength(); variationSeed=loaded.seed.ToString(); variationPersonality=loaded.personality; variationAmount=loaded.amount; variationDifference="RESTORED (saved musical events)"+EventScoreText(loaded); status="Saved variation loaded. Apply it to enable arranged playback and exports."; }
            } catch(Exception e) { status="Could not load variation: "+e.Message; }
        }
        VariationBounds("LOAD VARIATION");
        GUILayout.EndHorizontal();
        GUILayout.Label("VARIATION DIFFERENCE: "+(songPlan == null ? "LOW (no variation planned)" : variationDifference),label);
        VariationBounds("DIFFERENCE");
        if(songPlan != null) {
            GUILayout.Label("PLANNED LENGTH: "+songPlan.bars+" bars",label);
        }
        GUILayout.Label(VariationPreview,label);
        VariationBounds("PREVIEW");
        DrawAppliedSongVariation(buttonStyle);
        if(GUILayout.Button("ONE-BAR GROOVE",GUILayout.Height(32))) ReturnToOneBarGroove();
        VariationBounds("ONE-BAR GROOVE");
        GUILayout.Label("Source groove and stem levels are preserved. STRONG creates new musical events; SUBTLE/MEDIUM filter the source groove. Saved plans and seed replay retain exact events. Inactive stems export as silence.",label);
        GUILayout.EndVertical();
        VariationBounds("SECTION");
        if(Event.current.type==EventType.Repaint) VariationRepaints++;
        GUILayout.Space(12);
    }
}
