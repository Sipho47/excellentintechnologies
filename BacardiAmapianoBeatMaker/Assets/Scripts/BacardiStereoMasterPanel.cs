using System;
using System.Threading.Tasks;
using UnityEngine;

public sealed class BacardiStereoMasterPanel : MonoBehaviour {
    readonly BacardiStereoMaster session=new BacardiStereoMaster();
    AudioSource source; AudioClip clip; Task pending;
    string input="", output="", ceiling="-1.0", status="Import the finished FL Studio stereo mix.";
    bool rebuild,target=true,showResults; Vector2 scroll;
    public void StopPlayback() { if(source!=null) source.Stop(); }
    void OnDestroy() { if(clip!=null) Destroy(clip); }
    void Run(Action action,bool updateClip=false) { StopPlayback(); rebuild=updateClip; status="Working on the full song..."; pending=Task.Run(action); }
    void Update() {
        if(pending==null||!pending.IsCompleted) return;
        if(pending.IsFaulted) status=pending.Exception.GetBaseException().Message;
        else {
            status="Complete.";
            if(rebuild) {
                if(source==null) { source=gameObject.AddComponent<AudioSource>(); source.playOnAwake=false; source.volume=1; source.spatialBlend=0; }
                if(clip!=null) Destroy(clip);
                var pcm=session.PlaybackCopy(); clip=AudioClip.Create("Post-arrangement stereo",pcm.Length/2,2,session.Rate,false); clip.SetData(pcm,0); source.clip=clip;
                output=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(session.SourcePath),System.IO.Path.GetFileNameWithoutExtension(session.SourcePath)+" Spotify Master.wav");
            }
        }
        pending=null;
        showResults=true;
    }
    GUIStyle resultLabel;
#if UNITY_EDITOR
    public readonly System.Collections.Generic.Dictionary<string,Rect> ResultRects=new System.Collections.Generic.Dictionary<string,Rect>();
    public readonly System.Collections.Generic.Dictionary<string,string> ResultText=new System.Collections.Generic.Dictionary<string,string>();
    public Rect ScrollViewport { get; private set; }
    public float ScrollContentHeight { get; private set; }
    public int ResultRepaints { get; private set; }
#endif
    // Explicitly bound the viewport and measure every wrapped content row.
    public void Draw() {
        if(resultLabel==null) resultLabel=new GUIStyle(GUI.skin.label) { wordWrap=true, stretchWidth=false };
        var viewport=GUILayoutUtility.GetRect(0,100000,0,100000,GUILayout.ExpandWidth(true),GUILayout.ExpandHeight(true));
        float scrollbarWidth=Mathf.Max(16,GUI.skin.verticalScrollbar.fixedWidth);
        float width=Mathf.Max(1,viewport.width-scrollbarWidth-6);
        float height=DrawContent(width,false)+16;
        // Results can add wrapped rows after the async operation completes.
        // Use their newly measured height before moving to the final status.
        if(showResults && Event.current.type==EventType.Repaint) {
            scroll.y=Mathf.Max(0,height-viewport.height);
            showResults=false;
        }
        scroll=GUI.BeginScrollView(viewport,scroll,new Rect(0,0,width,height),false,true);
        DrawContent(width,true);
        GUI.EndScrollView();
#if UNITY_EDITOR
        if(Event.current.type==EventType.Repaint) {
            ScrollViewport=new Rect(GUIUtility.GUIToScreenPoint(viewport.position),viewport.size);
            ScrollContentHeight=height; ResultRepaints++;
        }
#endif
    }
    void Record(string key,Rect rect,string text) {
#if UNITY_EDITOR
        if(Event.current.type==EventType.Repaint) {
            ResultRects[key]=new Rect(GUIUtility.GUIToScreenPoint(rect.position),rect.size);
            ResultText[key]=text;
        }
#endif
    }
    void RowLabel(ref float y,float width,bool paint,string text,string key=null) {
        float height=Mathf.Max(26,resultLabel.CalcHeight(new GUIContent(text),width));
        var rect=new Rect(0,y,width,height); y+=height+6;
        if(paint) { GUI.Label(rect,text,resultLabel); if(key!=null) Record(key,rect,text); }
    }
    bool RowButton(ref float y,float width,bool paint,string text) {
        var rect=new Rect(0,y,width,36); y+=42;
        if(!paint) return false;
        Record(text,rect,text); return GUI.Button(rect,text);
    }
    string RowField(ref float y,float width,bool paint,string value) {
        var rect=new Rect(0,y,width,30); y+=36;
        return paint?GUI.TextField(rect,value):value;
    }
    float DrawContent(float width,bool paint) {
        float y=0; bool wasEnabled=GUI.enabled;
        RowLabel(ref y,width,paint,"POST-ARRANGEMENT MASTER TEST");
        RowLabel(ref y,width,paint,"SOURCE: POST-ARRANGEMENT STEREO MASTER");
        RowLabel(ref y,width,paint,session.Active?"SPOTIFY MASTER ACTIVE":"BYPASSED");
        bool idle=pending==null; if(paint) GUI.enabled=idle;
        RowLabel(ref y,width,paint,"Import WAV path"); input=RowField(ref y,width,paint,input);
        if(RowButton(ref y,width,paint,"IMPORT STEREO WAV")) {
            string path=Pick(false,input); if(!string.IsNullOrEmpty(path)) { input=path; Run(()=>session.Import(path),true); }
        }
        if(paint) GUI.enabled=idle&&session.Loaded;
        var playback=new Rect(0,y,width,36); y+=42;
        if(paint) {
            float buttonWidth=(width-18)/4;
            if(GUI.Button(new Rect(0,playback.y,buttonWidth,36),"PLAY / RESUME")) { source.UnPause(); if(!source.isPlaying) source.Play(); }
            if(GUI.Button(new Rect(buttonWidth+6,playback.y,buttonWidth,36),"PLAY FROM START")) { source.Stop(); source.Play(); }
            if(GUI.Button(new Rect((buttonWidth+6)*2,playback.y,buttonWidth,36),"PAUSE")) source.Pause();
            if(GUI.Button(new Rect((buttonWidth+6)*3,playback.y,buttonWidth,36),"STOP")) StopPlayback();
        }
        if(source!=null&&clip!=null) RowLabel(ref y,width,paint,string.Format("Playback {0:F1} / {1:F1} seconds",source.time,clip.length));
        var toggle=new Rect(0,y,width,30); y+=36;
        if(paint) target=GUI.Toggle(toggle,target,"TARGET -14 LUFS");
        RowLabel(ref y,width,paint,"True Peak ceiling dBTP (-12 to 0)"); ceiling=RowField(ref y,width,paint,ceiling);
        if(RowButton(ref y,width,paint,"ANALYZE FULL SONG")) Run(()=>session.Analyze());
        if(paint) GUI.enabled=idle&&session.Loaded&&target;
        if(RowButton(ref y,width,paint,"SET SPOTIFY MASTER")) {
            double value;
            if(double.TryParse(ceiling,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)) Run(()=>session.SetSpotify(value),true);
            else status="Enter a numeric ceiling such as -1.0.";
        }
        if(paint) GUI.enabled=idle&&session.After!=null;
        if(RowButton(ref y,width,paint,"BYPASS / ACTIVATE STEREO MASTER")) { StopPlayback(); session.Active=!session.Active; clip.SetData(session.PlaybackCopy(),0); }
        RowLabel(ref y,width,paint,"New export path (existing files are protected)"); output=RowField(ref y,width,paint,output);
        if(RowButton(ref y,width,paint,"EXPORT SPOTIFY MASTER WAV")) {
            string path=Pick(true,output); if(!string.IsNullOrEmpty(path)) { output=path; Run(()=>session.Export(path)); }
        }
        if(paint) GUI.enabled=wasEnabled;
        if(session.Before!=null) RowLabel(ref y,width,paint,"SOURCE / BEFORE: "+session.Before,"SOURCE / BEFORE");
        if(session.After!=null) {
            RowLabel(ref y,width,paint,"MASTER / AFTER: "+session.After,"MASTER / AFTER");
            RowLabel(ref y,width,paint,"GAIN APPLIED: "+session.GainDb.ToString("F2")+" dB","GAIN APPLIED");
            RowLabel(ref y,width,paint,"LIMITER REDUCTION: "+session.MaxReductionDb.ToString("F2")+" dB","LIMITER REDUCTION");
            RowLabel(ref y,width,paint,"TARGET STATUS: "+session.MasterStatus,"TARGET STATUS");
        }
        RowLabel(ref y,width,paint,status,"OPERATION STATUS");
        if(paint) GUI.enabled=wasEnabled;
        return y;
    }
    static string Pick(bool save,string path) {
#if UNITY_EDITOR
        return save?UnityEditor.EditorUtility.SaveFilePanel("Export stereo master",System.IO.Path.GetDirectoryName(path)??"",System.IO.Path.GetFileName(path),"wav"):UnityEditor.EditorUtility.OpenFilePanel("Import stereo WAV","","wav");
#elif UNITY_STANDALONE_WIN
        var d=new Dialog(); d.size=System.Runtime.InteropServices.Marshal.SizeOf(d); d.filter="WAV\0*.wav\0\0";
        d.file=new System.Text.StringBuilder(path,32768); d.maxFile=32768; d.extension="wav"; d.flags=0x80000|8|0x800|(save?0:0x1000);
        return (save?Save(d):Open(d))?d.file.ToString():"";
#else
        return path;
#endif
    }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential,CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    sealed class Dialog {
        public int size; public IntPtr owner,instance; public string filter,customFilter; public int maxCustomFilter,filterIndex;
        public System.Text.StringBuilder file; public int maxFile; public string fileTitle; public int maxFileTitle; public string initialDir,title;
        public int flags; public short fileOffset,extensionOffset; public string extension; public IntPtr customData,hook; public string template;
        public IntPtr reserved; public int reservedInt,flagsEx;
    }
    [System.Runtime.InteropServices.DllImport("comdlg32.dll",EntryPoint="GetOpenFileNameW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern bool Open([System.Runtime.InteropServices.In,System.Runtime.InteropServices.Out] Dialog d);
    [System.Runtime.InteropServices.DllImport("comdlg32.dll",EntryPoint="GetSaveFileNameW",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern bool Save([System.Runtime.InteropServices.In,System.Runtime.InteropServices.Out] Dialog d);
#endif
}

