using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Real Play Mode OnGUI and Game-view mouse/wheel events. Local fixture only.
public sealed class BacardiStereoMasterUiHost : MonoBehaviour {
    public BacardiStereoMasterPanel Panel;
    void OnGUI() {
        GUILayout.BeginArea(new Rect(24,18,Screen.width-48,Screen.height-36));
        GUILayout.Label("BACARDI / AMAPIANO",new GUIStyle(GUI.skin.label) { fontSize=30,fontStyle=FontStyle.Bold });
        GUILayout.Label("BEAT MAKER   /   25 SOUNDS   /   16 STEPS",new GUIStyle(GUI.skin.label) { fontSize=14,wordWrap=true });
        GUILayout.Button("RETURN TO STEM MIX",GUILayout.Height(38));
        Panel.Draw(); GUILayout.EndArea();
    }
}

[InitializeOnLoad]
public static class BacardiStereoMasterUiTest {
    const string Key="Bacardi.StereoUiValidation";
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static readonly Vector2Int[] Sizes={new Vector2Int(1920,1080),new Vector2Int(960,540),new Vector2Int(640,480),new Vector2Int(480,320)};
    static readonly string[] Rows={"EXPORT SPOTIFY MASTER WAV","SOURCE / BEFORE","MASTER / AFTER","GAIN APPLIED","LIMITER REDUCTION","TARGET STATUS","OPERATION STATUS"};
    static BacardiStereoMasterPanel panel;
    static BacardiStereoMaster session;
    static EditorWindow game;
    static int scenario,stage,row,repaint;
    static double deadline;
    static string report="",sourcePath,sourceBytes;
    static object Get(string name) { return typeof(BacardiStereoMasterPanel).GetField(name,Flags).GetValue(panel); }
    static void Set(string name,object value) { typeof(BacardiStereoMasterPanel).GetField(name,Flags).SetValue(panel,value); }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static BacardiStereoMasterUiTest() { EditorApplication.update+=Tick; }
    public static void RunCommandLine() { SessionState.SetBool(Key,true); EditorApplication.isPlaying=true; }
    static void Configure() {
        // Fix the actual render target resolution independently of desktop size.
        var assembly=typeof(EditorWindow).Assembly;
        var sizesType=assembly.GetType("UnityEditor.GameViewSizes");
        var singleton=typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        var sizes=singleton.GetProperty("instance",BindingFlags.Public|BindingFlags.Static|BindingFlags.FlattenHierarchy).GetValue(null);
        var groupType=game.GetType().GetProperty("currentSizeGroupType",Flags|BindingFlags.Static).GetValue(game);
        var group=sizesType.GetMethod("GetGroup").Invoke(sizes,new[]{groupType});
        var sizeType=assembly.GetType("UnityEditor.GameViewSize");
        var mode=Enum.Parse(assembly.GetType("UnityEditor.GameViewSizeType"),"FixedResolution");
        var size=Activator.CreateInstance(sizeType,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{mode,Sizes[scenario].x,Sizes[scenario].y,"Stereo UI test"},null);
        group.GetType().GetMethod("AddCustomSize").Invoke(group,new[]{size});
        int total=(int)group.GetType().GetMethod("GetTotalCount").Invoke(group,null);
        game.GetType().GetProperty("selectedSizeIndex",Flags).SetValue(game,total-1);
        game.minSize=Vector2.zero; game.maxSize=new Vector2(4000,3000);
        game.position=new Rect(40,40,Mathf.Min(1200,Sizes[scenario].x),Mathf.Min(850,Sizes[scenario].y)+40);
        game.Focus(); Set("scroll",Vector2.zero); repaint=panel.ResultRepaints;
        stage=scenario==0?1:4; row=0; game.Repaint();
    }
    static Vector2 EventPoint(Vector2 pixel) {
        var image=(Rect)game.GetType().GetProperty("targetInParent",Flags).GetValue(game);
        return image.position+new Vector2(pixel.x*image.width/Screen.width,pixel.y*image.height/Screen.height);
    }
    static bool Reveal(string key) {
        Rect r=panel.ResultRects[key],v=panel.ScrollViewport;
        Check(r.height>0 && r.height<=v.height-8,"Row cannot fit viewport: "+key);
        Check(r.xMin>=v.xMin-.5f && r.xMax<=v.xMax+.5f,"Horizontal clipping: "+key);
        if(r.yMin<v.yMin+4 || r.yMax>v.yMax-4) {
            var scroll=(Vector2)Get("scroll"); scroll.y=Mathf.Max(0,scroll.y+r.yMin-v.yMin-4);
            Set("scroll",scroll); game.Repaint(); return false;
        }
        return true;
    }
    static void Tick() {
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try {
            if(deadline==0) deadline=EditorApplication.timeSinceStartup+180;
            Check(EditorApplication.timeSinceStartup<deadline,"Runtime UI test timed out");
            if(panel==null) {
                Directory.CreateDirectory("Tools/StereoUiValidation");
                sourcePath=Path.GetFullPath("Tools/StereoUiValidation/fixture-"+Guid.NewGuid()+".wav");
                var pcm=new float[48000*2];
                for(int f=0;f<48000;f++) pcm[f*2]=pcm[f*2+1]=(float)(.112*Math.Sin(2*Math.PI*997*f/48000));
                BacardiStereoMaster.WriteWav(sourcePath,pcm,48000);
                sourceBytes=Convert.ToBase64String(File.ReadAllBytes(sourcePath));
                var owner=new GameObject("Offline stereo runtime UI"); panel=owner.AddComponent<BacardiStereoMasterPanel>();
                owner.AddComponent<BacardiStereoMasterUiHost>().Panel=panel;
                session=(BacardiStereoMaster)Get("session");
                typeof(BacardiStereoMasterPanel).GetMethod("Run",Flags).Invoke(panel,new object[]{(Action)(()=>session.Import(sourcePath)),true});
                game=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); Configure(); return;
            }
            if(panel.ResultRepaints<=repaint) { game.Repaint(); return; }
            repaint=panel.ResultRepaints;
            Check(Screen.width==Sizes[scenario].x && Screen.height==Sizes[scenario].y,"Actual Game-view size differs: "+Screen.width+"x"+Screen.height);
            Check(panel.ScrollViewport.yMax<=Screen.height-18+.5f,"Viewport extends below parent area");
            if(stage==1) {
                if(Get("pending")!=null) return;
                if(!Reveal("SET SPOTIFY MASTER")) return;
                var point=EventPoint(panel.ResultRects["SET SPOTIFY MASTER"].center);
                game.SendEvent(new Event { type=EventType.MouseDown,button=0,mousePosition=point });
                game.SendEvent(new Event { type=EventType.MouseUp,button=0,mousePosition=point }); stage=2;
            } else if(stage==2) {
                if(Get("pending")!=null) return;
                Check(session.After!=null && session.TargetReached,"SET SPOTIFY MASTER click did not produce results");
                Rect final=panel.ResultRects["OPERATION STATUS"],viewport=panel.ScrollViewport;
                Check(final.yMin>=viewport.yMin && final.yMax<=viewport.yMax,"Completion did not reveal the final status automatically");
                report+="PASS: SET SPOTIFY MASTER automatically reveals final status.\n";
                stage=4;
            } else if(stage==4 || stage==7) {
                string key=Rows[row]; if(!Reveal(key)) return;
                if(key=="GAIN APPLIED"||key=="LIMITER REDUCTION"||key=="TARGET STATUS") Check(panel.ResultText[key].StartsWith(key+":"),"Explicit label missing: "+key);
                report+="PASS "+Sizes[scenario]+" "+(stage==7?"failure":"success")+": "+key+" fully visible inside scroll viewport.\n";
                if(++row==Rows.Length) {
                    stage=stage==4?5:8; row=0;
                    game.SendEvent(new Event { type=EventType.ScrollWheel,mousePosition=EventPoint(panel.ScrollViewport.center),delta=new Vector2(0,10000) });
                }
            } else if(stage==5 || stage==8) {
                Rect last=panel.ResultRects["OPERATION STATUS"],v=panel.ScrollViewport;
                Check(last.yMin>=v.yMin && last.yMax<=v.yMax,"Final line clipped at scroll bottom");
                var scroll=(Vector2)Get("scroll");
                Check(Math.Abs(scroll.y-Mathf.Max(0,panel.ScrollContentHeight-v.height))<2,"Wheel did not reach actual scroll bottom");
                Capture(stage==5?"success":"failure");
                if(stage==5) {
                    typeof(BacardiStereoMaster).GetProperty("MasterStatus").SetValue(session,"Target not reached: excessive limiting would be required. This long failure message must wrap inside the scrollable section and remain completely readable at small Game-view sizes.");
                    stage=7;
                } else {
                    typeof(BacardiStereoMaster).GetProperty("MasterStatus").SetValue(session,"Target reached");
                    if(++scenario==Sizes.Length) { Finish(true); return; } Configure();
                }
            }
            game.Repaint();
        } catch(Exception e) { report+="FAIL: "+e+"\n"; Finish(false); }
    }
    static void Capture(string state) {
        var render=(RenderTexture)game.GetType().GetField("m_RenderTexture",Flags).GetValue(game);
        Check(render!=null,"Game-view render texture missing");
        var previous=RenderTexture.active; var picture=new Texture2D(render.width,render.height,TextureFormat.RGB24,false);
        try {
            RenderTexture.active=render; picture.ReadPixels(new Rect(0,0,render.width,render.height),0,0); picture.Apply();
            if(SystemInfo.graphicsUVStartsAtTop) {
                var pixels=picture.GetPixels32();
                for(int y=0;y<render.height/2;y++) for(int x=0;x<render.width;x++) {
                    int a=y*render.width+x,b=(render.height-1-y)*render.width+x; var pixel=pixels[a]; pixels[a]=pixels[b]; pixels[b]=pixel;
                }
                picture.SetPixels32(pixels); picture.Apply();
            }
            File.WriteAllBytes("Tools/StereoUiValidation/"+Sizes[scenario].x+"x"+Sizes[scenario].y+"-"+state+".png",picture.EncodeToPNG());
        } finally { RenderTexture.active=previous; UnityEngine.Object.DestroyImmediate(picture); }
    }
    static void Finish(bool ok) {
        SessionState.SetBool(Key,false);
        if(sourcePath!=null) Check(sourceBytes==Convert.ToBase64String(File.ReadAllBytes(sourcePath)),"Fixture source changed");
        File.WriteAllText("Tools/StereoUiValidation/runtime-ui-results.txt",(ok?"PASS\n":"FAIL\n")+report+"Local fixture preserved. Export was never clicked. No paid API calls.\n");
        EditorApplication.Exit(ok?0:1);
    }
}
