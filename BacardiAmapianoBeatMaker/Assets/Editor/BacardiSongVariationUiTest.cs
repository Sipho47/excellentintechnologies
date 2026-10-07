using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Actual Game-view mouse events; planning only, no playback, rendering or APIs.
[InitializeOnLoad]
public static class BacardiSongVariationUiTest {
    const string Key="Bacardi.VariationUiValidation";
    static readonly Vector2[] Sizes={new Vector2(1920,1080),new Vector2(960,540),new Vector2(640,480)};
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static BacardiBeatMaker maker;
    static EditorWindow game;
    static int scenario,stage,repaint,index;
    static double deadline;
    static string report="",saved,oldSeed,planJson;
    static bool hadSaved;
    static bool clicked;
    static string beforePreview;
    static BacardiSongVariationUiTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() {
        SessionState.SetBool(Key,true); EditorApplication.isPlaying=true;
    }
    static object Get(string key) { return typeof(BacardiBeatMaker).GetField(key,Flags).GetValue(maker); }
    static void Set(string key,object value) { typeof(BacardiBeatMaker).GetField(key,Flags).SetValue(maker,value); }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static void Configure() {
        game.minSize=Vector2.zero; game.maxSize=new Vector2(4000,3000);
        game.position=new Rect(40,40,Sizes[scenario].x,Sizes[scenario].y);
        game.Focus(); Set("scroll",Vector2.zero); Set("songPlan",null);
        stage=1; index=0; clicked=false; repaint=maker.VariationRepaints;
    }
    static void Click(string key) {
        Rect r=maker.VariationControls[key];
        // Runtime IMGUI uses render-target pixels. GameView SendEvent uses its
        // parent view's points, including zoom, DPI and toolbar/tab borders.
        var image=(Rect)game.GetType().GetProperty("targetInParent",Flags).GetValue(game);
        var point=image.position+new Vector2(r.center.x*image.width/Screen.width,r.center.y*image.height/Screen.height);
        Check(r.width>30 && r.height>=20,"No usable bounds: "+key);
        // Bring each control into the main runtime scroll viewport first.
        game.SendEvent(new Event { type=EventType.MouseDown,button=0,mousePosition=point });
        game.SendEvent(new Event { type=EventType.MouseUp,button=0,mousePosition=point });
    }
    static bool Reveal(string key) {
        Rect r=maker.VariationControls[key];
        float top=maker.VariationViewport.yMin+4;
        float bottom=maker.VariationViewport.yMax-4;
        float needed=key=="PREVIEW" ? 20 : r.height;
        Check(bottom-top>=needed,"Runtime scroll viewport too short: "+maker.VariationViewport);
        if(r.yMin<top || r.yMin+needed>bottom) {
            var scroll=(Vector2)Get("scroll"); scroll.y=Mathf.Max(0,scroll.y+r.yMin-top);
            Set("scroll",scroll); game.Repaint(); return false;
        }
        Check(r.xMin>=maker.VariationViewport.xMin && r.xMax<=maker.VariationViewport.xMax,"Horizontal clipping: "+key);
        return true;
    }
    static void CheckLiveStatus() {
        int personality=(int)Get("variationPersonality"), amount=(int)Get("variationAmount");
        Check(maker.VariationDisplayedText["SELECTED PERSONALITY"]=="SELECTED PERSONALITY: "+BacardiSongVariation.Personalities[personality],"Personality status stale");
        Check(maker.VariationDisplayedText["SELECTED AMOUNT"]=="VARIATION AMOUNT: "+BacardiSongVariation.Amounts[amount],"Amount status stale");
        Check(maker.VariationDisplayedText["CURRENT SEED"]=="CURRENT SEED: "+Get("variationSeed"),"Seed status stale");
        for(int group=0;group<2;group++) for(int i=0;i<(group==0 ? 7 : 3);i++) {
            string key=(group==0 ? "PERSONALITY " : "AMOUNT ")+i;
            bool selected=i==(group==0 ? personality : amount);
            Check(maker.VariationButtonColors[key]==(selected ? BacardiBeatMaker.VariationSelectedColor : BacardiBeatMaker.VariationUnselectedColor),"Highlight does not match request: "+key);
            Check(maker.VariationDisplayedText[key].StartsWith("\u2713 ")==selected,"Selection marker does not match request: "+key);
        }
        Check(maker.VariationControls["CURRENT SEED"].yMax<=maker.VariationControls["GENERATE NEW SONG VARIATION"].yMin,"Status must precede Generate");
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try {
            if(deadline==0) deadline=EditorApplication.timeSinceStartup+180;
            Check(EditorApplication.timeSinceStartup<deadline,"Timeout");
            if(maker==null) {
                maker=new GameObject("Offline variation UI acceptance").AddComponent<BacardiBeatMaker>();
                game=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
                hadSaved=PlayerPrefs.HasKey("BacardiSongVariation"); saved=PlayerPrefs.GetString("BacardiSongVariation",""); Configure(); return;
            }
            if(maker.VariationRepaints<=repaint) { game.Repaint(); return; }
            repaint=maker.VariationRepaints;
            CheckLiveStatus();
            string[] arrangedKeys={"APPLY VARIATION TO SONG","PREVIEW ARRANGED SONG","EXPORT ARRANGED STEMS","EXPORT ARRANGED MIDI","EXPORT ARRANGED STEREO WAV","ONE-BAR GROOVE"};
            string key=stage==1 ? "SONG VARIATION" : stage==2 ? "PERSONALITY "+index : stage==3 ? "AMOUNT "+index : stage==4 ? "NEW SEED" : stage==5 ? "GENERATE NEW SONG VARIATION" : stage==6 ? "REUSE CURRENT SEED" : stage==7 ? "SAVE VARIATION" : stage==8 ? "LOAD VARIATION" : stage==9 ? "PREVIEW" : arrangedKeys[Math.Min(5,stage-10)];
            if(!Reveal(key)) return;
            if(stage>=2 && stage<=8 && !clicked) {
                if(stage==4) oldSeed=(string)Get("variationSeed");
                if(stage==5) beforePreview=maker.VariationPreview;
                Click(key); clicked=true; game.Repaint(); return;
            }
            clicked=false;
            if(stage==1) {
                Check(maker.VariationControls.ContainsKey("SECTION"),"Section missing from live OnGUI");
                if(scenario==0) CaptureBeforePlanning();
                stage++;
            }
            else if(stage==2) {
                Check((int)Get("variationPersonality")==index,"Personality click not bound: expected "+index+" got "+Get("variationPersonality")+" bounds "+maker.VariationControls[key]+" viewport "+maker.VariationViewport);
                if(scenario==0 && index==4) {
                    Check((int)Get("variationAmount")==2 && Get("songPlan")==null,"PRIVATE SCHOOL + STRONG capture must precede planning");
                    CaptureBeforePlanning();
                }
                if(++index==7) { index=0; stage++; }
            } else if(stage==3) {
                Check((int)Get("variationAmount")==index,"Amount click not bound");
                if(++index==3) stage++;
            } else if(stage==4) {
                Check(oldSeed!=(string)Get("variationSeed") && int.TryParse((string)Get("variationSeed"),out _),"New seed failed");
                Check(Get("songPlan")==null,"New seed must only change request"); stage++;
            } else if(stage==5) {
                var plan=(BacardiSongVariation.Plan)Get("songPlan");
                Check(plan!=null && plan.personality==6 && plan.amount==2,"Generate did not call existing planner with selectors");
                Check(beforePreview!=maker.VariationPreview,"Preview did not update");
                planJson=JsonUtility.ToJson(plan); Set("variationSeed","invalid edited seed"); stage++;
            } else if(stage==6) { Check(JsonUtility.ToJson(Get("songPlan"))==planJson,"Current seed replay failed"); stage++; }
            else if(stage==7) { Set("songPlan",null); stage++; }
            else if(stage==8) { Check(JsonUtility.ToJson(Get("songPlan"))==planJson,"Save/load failed"); stage++; }
            else if(stage==9) {
                Check(!maker.ArrangementApplied,"Planning must not apply implicitly");
                foreach(string control in new[]{"PREVIEW ARRANGED SONG","EXPORT ARRANGED STEMS","EXPORT ARRANGED MIDI","EXPORT ARRANGED STEREO WAV"}) Check(!maker.VariationControlEnabled[control],"Exports disabled before Apply");
                stage++;
            } else if(stage==10) { Click(key); stage++; }
            else if(stage<15) { Check(maker.ArrangementApplied && maker.VariationControlEnabled[key],"Applied control enabled: "+key); stage++; }
            else if(stage==15) { Click("ONE-BAR GROOVE"); stage++; }
            else {
                Check(!maker.ArrangementApplied && Get("songPlan")!=null,"One-bar mode preserves planned variation");
                Check(!(bool)Get("playing") && (float)Get("bpm")==104,"Playback/tempo changed");
                report+="PASS Game view "+Screen.width+"x"+Screen.height+" (host "+Sizes[scenario]+"): live status labels, selected button colors/markers match request after all 7 personality / 3 amount clicks and NEW SEED / REUSE CURRENT SEED; status above Generate; existing planner, preview, save/load checks.\n";
                if(++scenario==Sizes.Length) { Finish(true); return; } Configure();
            }
            game.Repaint();
        } catch(Exception e) { report+="FAIL: "+e+"\n"; Finish(false); }
    }
    static void Finish(bool ok) {
        if(hadSaved) PlayerPrefs.SetString("BacardiSongVariation",saved); else PlayerPrefs.DeleteKey("BacardiSongVariation");
        PlayerPrefs.Save(); SessionState.SetBool(Key,false);
        Directory.CreateDirectory("Tools/VariationValidation");
        File.WriteAllText("Tools/VariationValidation/runtime-ui-results.txt",report+"No audio generation/export/playback or paid API calls.\n");
        EditorApplication.Exit(ok ? 0 : 1);
    }
    static void CaptureBeforePlanning() {
        var render=(RenderTexture)game.GetType().GetField("m_RenderTexture",Flags).GetValue(game);
        Check(render!=null,"Game-view render texture missing");
        var previous=RenderTexture.active;
        var picture=new Texture2D(render.width,render.height,TextureFormat.RGB24,false);
        try {
            RenderTexture.active=render; picture.ReadPixels(new Rect(0,0,render.width,render.height),0,0); picture.Apply();
            // GameView flips D3D render targets when displaying them. ReadPixels
            // reads the raw texture, so apply that same orientation for export.
            if(SystemInfo.graphicsUVStartsAtTop) {
                var pixels=picture.GetPixels32();
                for(int y=0;y<render.height/2;y++) for(int x=0;x<render.width;x++) {
                    int a=y*render.width+x,b=(render.height-1-y)*render.width+x;
                    var pixel=pixels[a]; pixels[a]=pixels[b]; pixels[b]=pixel;
                }
                picture.SetPixels32(pixels); picture.Apply();
            }
            Directory.CreateDirectory("Tools/VariationValidation");
            File.WriteAllBytes("Tools/VariationValidation/song-variation-before-planning.png",picture.EncodeToPNG());
        } finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(picture); }
    }
}
