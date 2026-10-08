using System;
using System.IO;
using System.Reflection;
using System.Net;
using System.Net.Sockets;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BacardiStrongVariationRuntimeTest {
    const string Key="Bacardi.StrongEvents";static string report="";static int checks;
    static BacardiBeatMaker maker;static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    static BacardiStrongVariationRuntimeTest(){EditorApplication.update+=Tick;}
    public static void RunCommandLine(){SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;}
    static object Get(string n){return typeof(BacardiBeatMaker).GetField(n,Flags).GetValue(maker);}
    static void Set(string n,object v){typeof(BacardiBeatMaker).GetField(n,Flags).SetValue(maker,v);}
    static object Call(string n,params object[] args){return typeof(BacardiBeatMaker).GetMethod(n,Flags).Invoke(maker,args);}
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Pass(string text){checks++;report+="PASS "+text+"\n";}
    static void Same(float[] a,float[] b){Check(a.Length==b.Length,"PCM length changed");for(int n=0;n<a.Length;n++)Check(a[n]==b[n],"PCM replay differs");}
    static double Difference(float[] a,float[] b){double d=0;Check(a.Length==b.Length,"Comparison lengths");for(int n=0;n<a.Length;n++){double x=a[n]-b[n];d+=x*x;}return Math.Sqrt(d/a.Length);}
    static int Big(BinaryReader r,int count){int n=0;while(count-->0)n=n*256+r.ReadByte();return n;}
    static int Variable(BinaryReader r){int n=0,b;do{b=r.ReadByte();n=(n<<7)|(b&127);}while((b&128)!=0);return n;}
    static void Midi(string path,BacardiSongVariation.Plan p){
        using(var r=new BinaryReader(File.OpenRead(path))){
            Check(new string(r.ReadChars(4))=="MThd"&&Big(r,4)==6,"MIDI header");Big(r,2);Check(Big(r,2)==26&&Big(r,2)==960,"MIDI tracks");
            int[] drums={36,39,42,46,82,60,61,64,61,54,37,62,41,49,55,81,35};
            for(int track=0;track<26;track++){
                Check(new string(r.ReadChars(4))=="MTrk","MIDI track header");int length=Big(r,4),tick=0,index=0;long end=r.BaseStream.Position+length;
                var expected=new System.Collections.Generic.List<int[]>();
                if(track>0)for(int s=0;s<p.bars*16;s++){
                    int id=track-1,n=s/16*400+id*16+s%16,v=p.eventVelocities[n];if(v==0)continue;
                    int root=id<8?BacardiVariationEvents.Roots[id]+p.eventNotes[n]:drums[id-8];
                    foreach(int note in id==2||id==4?new[]{root,root+4,root+7}:new[]{root})expected.Add(new[]{(int)Math.Round((s+(s%2==1?.16f:0))*240),note,v});
                }
                while(r.BaseStream.Position<end){tick+=Variable(r);int type=r.ReadByte();if(type==255){r.ReadByte();r.ReadBytes(Variable(r));}else{
                    int note=r.ReadByte(),velocity=r.ReadByte();if((type&240)==144&&velocity>0){Check(index<expected.Count,"Extra MIDI note");var x=expected[index++];Check(tick==x[0]&&note==x[1]&&velocity==x[2],"MIDI timing/note/velocity mismatch");}
                }}
                Check(index==expected.Count&&tick==p.bars*3840,"MIDI duration/events");
            }
        }
    }
    static void Tick(){
        if(!SessionState.GetBool(Key,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling)return;
        SessionState.SetBool(Key,false);bool ok=false;var trap=new TcpListener(IPAddress.Loopback,0);
        bool had=PlayerPrefs.HasKey("BacardiPattern");string saved=PlayerPrefs.GetString("BacardiPattern","");string endpoint=Environment.GetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL");
        try{
            trap.Start();Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL","http://127.0.0.1:"+((IPEndPoint)trap.LocalEndpoint).Port+"/");
            maker=new GameObject("Synthetic STRONG offline fixture").AddComponent<BacardiBeatMaker>();
            var cells=(bool[,])Get("steps");var notes=(int[,])Get("noteOffsets");var velocities=(int[,])Get("velocities");
            var source=BacardiVariationEvents.Capture(cells,notes,velocities);var originalCells=(bool[,])cells.Clone();var originalNotes=(int[,])notes.Clone();var originalVelocities=(int[,])velocities.Clone();
            Set("variationSeed","72539");Set("variationAmount",2);Set("variationPersonality",1);maker.GenerateNewSongVariation();
            var p=(BacardiSongVariation.Plan)Get("songPlan");Check(p!=null&&p.version==2&&p.generatorVersion==1,"No new event plan");
            Check(BacardiVariationEvents.ValidEvents(p)&&BacardiVariationEvents.Passes(p.sourceScore),"Invalid accepted plan");Pass("STRONG creates validated version-2 events with generator version and source snapshot");
            int added=0;for(int bar=0;bar<p.bars;bar++)for(int id=0;id<25;id++)for(int s=0;s<16;s++)if(p.eventVelocities[bar*400+id*16+s]>0&&!cells[id,s])added++;
            Check(added>100,"No genuinely new events");Pass("new attacks exist beyond source steps: "+added);
            maker.ApplyVariationToSong();var vocals=(BacardiAiVocalGenerator)Get("aiVocals");Check(maker.ArrangementLength==p.bars&&vocals.SynchronizedSongBars==p.bars,"Song bars lost sync");Pass("new plan length and read-only Song bars synchronize on APPLY");
            for(int bar=0;bar<p.bars;bar++)for(int id=0;id<25;id++)for(int s=0;s<16;s++){
                int a=bar*16+s,n=bar*400+id*16+s,step=(int)Call("SongSourceStep",id,a);
                Check((step>=0)==(p.eventVelocities[n]>0),"Event resolver existence mismatch");
                if(step>=0)Check((int)Call("SongNote",id,a,step)==p.eventNotes[n]&&(int)Call("SongVelocity",id,a,step)==p.eventVelocities[n],"Event resolver data mismatch");
            }Pass("shared live/PCM/MIDI resolver exposes every generated attack, pitch and velocity");
            for(int id=0;id<25;id++)for(int s=0;s<16;s++)Check(cells[id,s]==originalCells[id,s]&&notes[id,s]==originalNotes[id,s]&&velocities[id,s]==originalVelocities[id,s],"Source groove mutated");Pass("all source groove arrays remain unchanged");
            Check(p.sections[2].lead!=p.sections[4].lead&&p.dropScore.overall>=.35,"Drop identity missing");
            Check(p.dropScore.lowEnd>.25&&p.dropScore.percussion>.25,"Low/percussion drop patterns not distinct");Pass("drops have different leads and measurable low-end/percussion/event identities: "+p.dropScore);
            foreach(int si in new[]{0,1,3,5})Check(BacardiVariationEvents.Compare(p,p,source,si,2).overall>.25,"Section repeats drop");Pass("intro/build/breakdown/outro contrast with drop; section event patterns differ");
            int breakdown=0,drop=0;for(int b=0;b<2;b++)for(int n=0;n<400;n++){if(p.eventVelocities[(p.sections[3].start+b)*400+n]>0)breakdown++;if(p.eventVelocities[(p.sections[2].start+b)*400+n]>0)drop++;}
            Check(breakdown<drop*.55,"Breakdown not sparse");Pass("breakdown event density below 55% of drop density");
            int repeats=0;
            foreach(var section in p.sections)if(section.bars>p.phraseBars) {
                for(int n=0;n<6*16;n++){int a=section.start*400+n,b=(section.start+p.phraseBars)*400+n;Check(p.eventNotes[a]==p.eventNotes[b]&&p.eventVelocities[a]==p.eventVelocities[b],"Phrase did not repeat");}repeats++;
            }
            Check(repeats>0,"No phrase fixture");Pass("melodic phrases repeat exactly after the persisted 2/4-bar phrase length; entrance/transition changes remain intentional");
            var mix=(float[])Call("ArrangementMix");Check(mix.Length==(int)Math.Ceiling(p.bars*240.0/104*44100)*2,"Duration mismatch");
            var masked=JsonUtility.FromJson<BacardiSongVariation.Plan>(JsonUtility.ToJson(p));masked.version=1;Set("songPlan",masked);maker.ApplyVariationToSong();var old=(float[])Call("ArrangementMix");
            double rms=Difference(mix,old);Check(rms>.01,"Generated PCM too similar to masked source");Pass("rendered stereo differs from same-structure source-mask render; difference RMS="+rms.ToString("F6",System.Globalization.CultureInfo.InvariantCulture));
            Set("songPlan",p);maker.ApplyVariationToSong();Same(mix,(float[])Call("ArrangementMix"));Pass("rendered stereo is sample-exact on repeated replay");
            foreach(int id in new[]{0,6,12}) {
                var generated=(float[])Call("RenderSound",id,mix.Length/2);Set("songPlan",masked);maker.ApplyVariationToSong();var filtered=(float[])Call("RenderSound",id,mix.Length/2);
                Check(Difference(generated,filtered)>.0001,"Family audio unchanged: "+id);Set("songPlan",p);maker.ApplyVariationToSong();
            }Pass("rendered melody, log-drum and shaker stems each differ from source-mask audio");
            string folder="Tools/StrongVariationValidation";Directory.CreateDirectory(folder);string midi=Path.Combine(folder,"synthetic-"+Guid.NewGuid().ToString("N")+".mid");Call("WriteArrangedMidi",midi);Midi(midi,p);Pass("arranged MIDI notes, velocities, swing timing and duration match generated events");
            string json=JsonUtility.ToJson(p);maker.GenerateNewSongVariation(true);Check(json==JsonUtility.ToJson(Get("songPlan")),"Seed replay differs");Pass("accepted seed replay is exact including event data and generator version");
            Call("Save");Set("songPlan",null);Call("Load");Check(json==JsonUtility.ToJson(Get("songPlan")),"v2 saved reload differs");maker.ApplyVariationToSong();Same(mix,(float[])Call("ArrangementMix"));Pass("saved v2 pattern/variation reload retains exact events, stereo PCM and Song bars");
            var child=new GameObject("Synthetic v2 placed vocal");child.transform.SetParent(maker.transform);
            var vocal=new AiVocalResult {Source="REAL_AI",IsMock=false,Provider="Offline fixture",Request=new AiVocalRequest {Bpm=104,Bars=1,Role="Lead"},Pcm=new float[8820]};
            for(int n=0;n<vocal.Pcm.Length;n+=2){vocal.Pcm[n]=.02f;vocal.Pcm[n+1]=-.01f;}
            vocals.Placements.Add(new BacardiAiVocalGenerator.Placement {StartBar=13,Vocal=vocal,Source=child.AddComponent<AudioSource>()});
            var expectedMix=(float[])mix.Clone();int at=(int)BacardiVocalTimeline.FrameAtBar(13,104)*2;for(int n=0;n<vocal.Pcm.Length;n++)expectedMix[at+n]+=vocal.Pcm[n];
            double peak=0;foreach(float sample in expectedMix)peak=Math.Max(peak,Math.Abs(sample));if(peak>1)for(int n=0;n<expectedMix.Length;n++)expectedMix[n]=(float)(expectedMix[n]/peak);
            var combined=Call("BuildArrangedVocalMix");Same(expectedMix,(float[])combined.GetType().GetField("Pcm").GetValue(combined));
            maker.PreviewArrangedSong();var clip=(AudioClip)Get("arrangedClip");Check(clip!=null,"v2 preview unavailable");var preview=new float[clip.samples*2];clip.GetData(preview,0);Same(expectedMix,preview);Call("Stop");
            vocals.Placements.Clear();UnityEngine.Object.DestroyImmediate(child);Pass("v2 combined-mix helper and actual preview AudioClip contain exact instrumental plus placed stereo vocal PCM");
            // A serialized mask-only v1 fixture exercises the actual old runtime branch.
            var legacy=BacardiSongVariation.Create(613,1,2);string legacyJson=JsonUtility.ToJson(legacy);legacy=JsonUtility.FromJson<BacardiSongVariation.Plan>(legacyJson);
            Set("songPlan",legacy);maker.ApplyVariationToSong();Check(maker.ArrangementApplied,"Legacy v1 rejected");
            for(int a=0;a<legacy.bars*16;a++)for(int id=0;id<25;id++){
                int actual=(int)Call("SongSourceStep",id,a),expected=-1;if(legacy.Allows(id,a)){if(id<21){if(cells[id,a%16])expected=a%16;}else for(int s=0;s<16;s++)if(cells[id,s]){expected=s;break;}}
                Check(actual==expected,"v1 masks changed");if(actual>=0)Check((int)Call("SongNote",id,a,actual)==notes[id,actual]&&(int)Call("SongVelocity",id,a,actual)==velocities[id,actual],"v1 note or velocity changed");
            }Pass("legacy saved v1 plans load and retain exact mask/source-note/source-velocity semantics");
            string legacyScore=(string)Call("EventScoreText",legacy);
            Check(legacyScore.Contains("SOURCE: "+BacardiVariationEvents.Compare(null,legacy,source)),"Legacy score not measured from events");Pass("legacy v1 score display measures actual events instead of default deserialized score objects");
            // A length-only edit with identical repeating events must score zero.
            var one=JsonUtility.FromJson<BacardiSongVariation.Plan>(JsonUtility.ToJson(p));
            for(int si=0;si<6;si++)for(int b=0;b<one.sections[si].bars;b++)for(int n=0;n<400;n++){
                int target=(one.sections[si].start+b)*400+n,first=one.sections[si].start*400+n;
                one.eventNotes[target]=one.eventNotes[first];one.eventVelocities[target]=one.eventVelocities[first];
            }
            var two=JsonUtility.FromJson<BacardiSongVariation.Plan>(JsonUtility.ToJson(one));int offset=0;two.bars+=12;two.eventNotes=new int[two.bars*400];two.eventVelocities=new int[two.bars*400];
            for(int si=0;si<6;si++){int first=one.sections[si].start*400;two.sections[si].start=offset;two.sections[si].bars+=2;for(int b=0;b<two.sections[si].bars;b++){Array.Copy(one.eventNotes,first,two.eventNotes,(offset+b)*400,400);Array.Copy(one.eventVelocities,first,two.eventVelocities,(offset+b)*400,400);}offset+=two.sections[si].bars;}
            Check(BacardiVariationEvents.Compare(one,two,source).overall==0,"Length rewarded");Pass("section-length/boundary changes alone score exactly zero");
            var retained=Get("songPlan");notes[0,0]=25;Set("variationSeed","-99123");maker.GenerateNewSongVariation();notes[0,0]=originalNotes[0,0];Check(ReferenceEquals(retained,Get("songPlan")),"Failed generation replaced current plan");Pass("failed generation retains existing plan");
            Check((float)Get("bpm")==104&&!trap.Pending(),"BPM changed or provider called");Pass("104 BPM preserved; zero provider/API or proxy calls; synthetic fixtures only");ok=true;
        }catch(Exception e){report+="FAIL "+e+"\n";}
        finally{if(maker!=null)UnityEngine.Object.DestroyImmediate(maker.gameObject);if(had)PlayerPrefs.SetString("BacardiPattern",saved);else PlayerPrefs.DeleteKey("BacardiPattern");Environment.SetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL",endpoint);trap.Stop();Directory.CreateDirectory("Tools/StrongVariationValidation");File.WriteAllText("Tools/StrongVariationValidation/runtime-results.txt",(ok?"PASS":"FAIL")+" | checks="+checks+"\n"+report+"PART 10 untouched. No user exports or mastering.\n");EditorApplication.Exit(ok?0:1);}
    }
}
