using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

public sealed class VocalProxyRequest {
    public string PronunciationGuidance="";
    public string AiPronunciationText="";
    public bool StrictLyrics, SlowerPhraseDensity;
    public string Lyrics="", Voice="Female", Role="Lead", Key="C", Scale="Major", Style="Smooth Amapiano / Afro-house", Character="warm, intimate, airy, clear, soulful, hypnotic";
    public double Bpm=104; public int Bars=4;
    public float Energy=.45f, Emotion=.6f; public bool Dry=true, RelativeMinor=true;
    public double Seconds => Bars*4*60/Bpm;
}
public sealed class VocalProxyJob {
    public string id=Guid.NewGuid().ToString("N"), state="GENERATING...", error="", provider="ElevenLabs Music v2.5 / two-stem vocal separation", audioPath="";
    public string stage="generation"; public int httpStatus; public float progress=.02f; public bool isMock;
    [System.Text.Json.Serialization.JsonIgnore] public byte[] Audio;
    [System.Text.Json.Serialization.JsonIgnore] public CancellationTokenSource Cancel=new CancellationTokenSource();
    [System.Text.Json.Serialization.JsonIgnore] public DateTime Created=DateTime.UtcNow;
}
public static class VocalProxy {
    public const string StrictLyricsGuidance="Sing every written word exactly once, in order. Do not omit, merge, replace, paraphrase, repeat, or invent words. Leave enough rhythmic space for every word.";
    public const string SlowerPhraseDensityGuidance="Use a simpler melody and more space between lyric phrases. Keep the project BPM unchanged; reduce phrase density without changing, adding or removing words or syllables.";
    static readonly JsonSerializerOptions Json=new JsonSerializerOptions { IncludeFields=true };
    static readonly ConcurrentDictionary<string,VocalProxyJob> Jobs=new ConcurrentDictionary<string,VocalProxyJob>();
    static readonly SemaphoreSlim Slot=new SemaphoreSlim(1,1);
    static string key, ffmpeg;
    static readonly HttpClient Api=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromMinutes(8) };
    public static object Payload(VocalProxyRequest q) {
        return new {
            model_id="music_v2_5",
            composition_plan=new { chunks=new[] { new {
                text=string.IsNullOrWhiteSpace(q.AiPronunciationText) ? "[Hook]\n"+q.Lyrics : q.AiPronunciationText, duration_ms=(int)Math.Round(q.Seconds*1000),
                positive_styles=new[] { q.Style, q.Voice.ToLowerInvariant()+" melodic singing", q.Character,
                    q.Bpm.ToString(System.Globalization.CultureInfo.InvariantCulture)+" BPM", q.Key+" "+q.Scale,
                    "4/4", q.Role+" vocal", "energy "+q.Energy, "emotion "+q.Emotion,
                    q.RelativeMinor && q.Key=="C" ? "A minor flavor" : q.Key+" "+q.Scale,
                    q.Dry ? "dry isolated a cappella vocals, no backing" : "isolated vocals with subtle ambience",
                    "short catchy phrases, space between lines, relaxed soulful delivery, South African Amapiano feel",
                    "sing the supplied lyrics verbatim in their original languages; preserve mixed-language lyrics; do not translate, rewrite or romanize" }
                    .Concat(string.IsNullOrWhiteSpace(q.PronunciationGuidance) ? Array.Empty<string>() : new[] {
                        "Vocal pronunciation/accent guidance only (never sung text; do not rewrite, translate, romanize or alter lyrics): " + q.PronunciationGuidance
                    })
                    .Concat(q.StrictLyrics ? new[] { StrictLyricsGuidance } : Array.Empty<string>())
                    .Concat(q.SlowerPhraseDensity ? new[] { SlowerPhraseDensityGuidance } : Array.Empty<string>()).ToArray(),
                negative_styles=new[] { "spoken speech", "narration", "rap", "drums", "bass", "piano", "instrumental backing", q.Dry ? "reverb" : "heavy reverb" },
                context_adherence="high"
            } } },
            sign_with_c2pa=false
        };
    }
    public static void Validate(VocalProxyRequest q) {
        if(q==null || string.IsNullOrWhiteSpace(q.Lyrics) || q.Lyrics.Length>4000) throw new ArgumentException("Lyrics must contain 1-4000 characters.");
        if(!double.IsFinite(q.Bpm) || q.Bpm<80 || q.Bpm>150 || !new[]{2,4,8,16}.Contains(q.Bars)) throw new ArgumentException("Choose 80-150 BPM and 2/4/8/16 bars.");
        if(!float.IsFinite(q.Energy) || !float.IsFinite(q.Emotion) || q.Energy<0 || q.Energy>1 || q.Emotion<0 || q.Emotion>1) throw new ArgumentException("Energy/emotion must be 0-1.");
        if(!new[]{"Female","Male"}.Contains(q.Voice) || !new[]{"Lead","Harmony","Ad-lib"}.Contains(q.Role) || !new[]{"Major","Minor"}.Contains(q.Scale) || !new[]{"C","A"}.Contains(q.Key)) throw new ArgumentException("Invalid voice, role or musical key.");
        if(q.Style==null || q.Style.Length>500 || q.Character==null || q.Character.Length>500) throw new ArgumentException("Invalid style/character.");
        if(q.PronunciationGuidance != null && q.PronunciationGuidance.Length>1000) throw new ArgumentException("Pronunciation / Accent Guidance must be at most 1000 characters.");
        if(q.AiPronunciationText != null && q.AiPronunciationText.Length>4000) throw new ArgumentException("AI Pronunciation Text must be at most 4000 characters.");
    }
    static async Task<byte[]> Call(HttpClient client,string path,HttpContent content,string apiKey,CancellationToken token) {
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.elevenlabs.io/v1/"+path);
        request.Headers.Add("xi-api-key",apiKey); request.Content=content;
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode) {
            string reason=(int)response.StatusCode switch { 401 => "Check backend ElevenLabs credentials.", 403 => "Account lacks permission for Music or stem separation.", 429 => "Quota, rate or credit limit reached.", _ => "Provider rejected or failed this request." };
            throw new AiVocalFailure(path.Contains("stem-separation") ? "vocal separation" : "generation", (int)response.StatusCode, (await response.Content.ReadAsStringAsync()).Replace(apiKey, "[REDACTED]"));
        }
        if(response.Content.Headers.ContentLength>64*1024*1024) throw new InvalidOperationException("Provider audio exceeds 64 MB.");
        using var input=await response.Content.ReadAsStreamAsync(token); using var output=new MemoryStream();
        var buffer=new byte[81920]; int count;
        while((count=await input.ReadAsync(buffer.AsMemory(),token))>0) {
            if(output.Length+count>64*1024*1024) throw new InvalidOperationException("Provider audio exceeds 64 MB.");
            output.Write(buffer,0,count);
        }
        return output.ToArray();
    }
    public static async Task<byte[]> Generate(VocalProxyRequest q,VocalProxyJob job,HttpClient client,string apiKey,string ffmpegPath,CancellationToken token) {
        Validate(q); job.state="GENERATING..."; job.progress=.05f;
        using var body=new StringContent(JsonSerializer.Serialize(Payload(q),Json),Encoding.UTF8,"application/json");
        byte[] song=await Call(client,"music",body,apiKey,token);
        job.stage="vocal separation"; job.state="GENERATING..."; job.progress=.45f;
        using var multipart=new MultipartFormDataContent();
        var music=new ByteArrayContent(song); music.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
        multipart.Add(music,"file","generated-song.mp3");
        multipart.Add(new StringContent("two_stems_v1"),"stem_variation_id");
        multipart.Add(new StringContent("false"),"sign_with_c2pa");
        byte[] stems=await Call(client,"music/stem-separation?output_format=mp3_44100_128",multipart,apiKey,token);
        job.state="PROCESSING..."; job.progress=.75f;
        byte[] vocal;
        using(var zip=new ZipArchive(new MemoryStream(stems),ZipArchiveMode.Read)) {
            var candidates=zip.Entries.Where(e=>{
                string name=Path.GetFileNameWithoutExtension(e.FullName).ToLowerInvariant();
                return (name=="vocals" || name.EndsWith("_vocals") || name.EndsWith("-vocals")) && !name.Contains("back") && !name.Contains("instrument");
            }).ToArray();
            if(candidates.Length!=1) throw new InvalidOperationException("Provider ZIP did not contain one unambiguous vocal stem; full song will NOT be imported.");
            if(candidates[0].Length>64*1024*1024) throw new InvalidOperationException("Vocal stem exceeds 64 MB.");
            using var stream=candidates[0].Open(); using var memory=new MemoryStream();
            await stream.CopyToAsync(memory,token); vocal=memory.ToArray();
        }
        job.stage="decoding"; string folder=Path.Combine(Path.GetTempPath(),"BacardiVocal-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try {
            string input=Path.Combine(folder,"vocals.audio"), raw=Path.Combine(folder,"decoded.pcm");
            await File.WriteAllBytesAsync(input,vocal,token);
            var start=new ProcessStartInfo(ffmpegPath) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
            foreach(string argument in new[]{"-hide_banner","-nostdin","-v","error","-i",input,"-vn","-ar","44100","-ac","2","-f","f32le",raw}) start.ArgumentList.Add(argument);
            using var process=Process.Start(start) ?? throw new InvalidOperationException("Could not start FFmpeg.");
            Task<string> error=process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync(token); }
            catch { if(!process.HasExited) process.Kill(true); throw; }
            if(process.ExitCode!=0) { throw new InvalidOperationException("FFmpeg exit " + process.ExitCode + ": " + await error); }
            job.stage="file loading"; byte[] bytes=await File.ReadAllBytesAsync(raw,token);
            int target=(int)Math.Round(q.Seconds*44100), frames=bytes.Length/8;
            if(bytes.Length%8!=0 || frames==0 || Math.Abs(frames-target)>44100*.25) throw new InvalidOperationException("Generated duration differs by more than 250 ms from requested bars. Regenerate; no pitch shifting or word-cutting adjustment was applied.");
            double peak=0; for(int n=0;n<bytes.Length;n+=4) {
                float x=BitConverter.ToSingle(bytes,n); if(!float.IsFinite(x)) throw new InvalidOperationException("Provider returned invalid PCM.");
                peak=Math.Max(peak,Math.Abs(x));
            }
            if(peak<1e-6) throw new InvalidOperationException("Extracted vocal stem is silent.");
            double trim=peak>.25 ? .25/peak : 1; // Peak headroom only. No loudness target.
            using var output=new MemoryStream(); using(var w=new BinaryWriter(output,Encoding.ASCII,true)) {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36+target*6);
                w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)2); w.Write(44100); w.Write(264600); w.Write((short)6); w.Write((short)24);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(target*6);
                for(int n=0;n<target*2;n++) {
                    float value=n<bytes.Length/4 ? BitConverter.ToSingle(bytes,n*4) : 0;
                    int sample=(int)Math.Round(value*trim*8388607);
                    w.Write((byte)sample); w.Write((byte)(sample>>8)); w.Write((byte)(sample>>16));
                }
            }
            return output.ToArray();
        } finally {
            string allowed=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(Path.GetFullPath(folder).StartsWith(allowed,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(folder).StartsWith("BacardiVocal-"))
                Directory.Delete(folder,true);
        }
    }
    static async Task RunJob(VocalProxyRequest q,VocalProxyJob job) {
        try {
            job.Audio=await Generate(q,job,Api,key,ffmpeg,job.Cancel.Token);
            job.audioPath="/jobs/"+job.id+"/audio"; job.progress=1; job.state="READY";
        } catch(OperationCanceledException e) { job.state=job.Cancel.IsCancellationRequested ? "CANCELLED" : "ERROR"; job.error=job.Cancel.IsCancellationRequested ? "Cancelled locally; upstream generation may already be billed." : AiVocalFailure.Clean("Backend timeout (8 minutes): " + e); }
        catch(Exception e) { if(e is AiVocalFailure f) { job.httpStatus=f.HttpStatus; job.stage=f.Stage; } job.error=AiVocalFailure.Clean(e.ToString()); if(!string.IsNullOrEmpty(key)) job.error=job.error.Replace(key,"[REDACTED]"); job.state="ERROR"; }
        finally { Slot.Release(); }
    }
    static async Task Respond(NetworkStream stream,int status,object body,byte[] bytes=null,string type="application/json") {
        byte[] content=bytes??Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body,Json));
        string header="HTTP/1.1 "+status+" Result\r\nContent-Type: "+type+"\r\nContent-Length: "+content.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n\r\n";
        byte[] headerBytes=Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes,0,headerBytes.Length); await stream.WriteAsync(content,0,content.Length);
    }
    static async Task Handle(TcpClient socket) {
        using(socket) {
            socket.ReceiveTimeout=15000; using var stream=socket.GetStream(); using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try {
                var header=new StringBuilder(); var one=new byte[1];
                while(!header.ToString().EndsWith("\r\n\r\n")) {
                    if(header.Length>=16384) throw new ArgumentException("Request headers too large.");
                    if(await stream.ReadAsync(one.AsMemory(),timeout.Token)==0) return; header.Append((char)one[0]);
                }
                string[] lines=header.ToString().Split("\r\n"); string[] first=lines[0].Split(' ');
                if(first.Length!=3) throw new ArgumentException("Invalid HTTP request.");
                var headers=lines.Skip(1).Where(x=>x.Contains(':')).Select(x=>x.Split(':',2)).ToDictionary(x=>x[0].Trim(),x=>x[1].Trim(),StringComparer.OrdinalIgnoreCase);
                if(headers.ContainsKey("Origin") || headers.ContainsKey("Transfer-Encoding")) { await Respond(stream,403,new{error="Browser-origin and chunked requests are disabled."}); return; }
                string method=first[0], path=first[1];
                if(method=="GET" && path=="/health") { await Respond(stream,200,new{provider="ElevenLabs Music",configured=!string.IsNullOrEmpty(key),ffmpeg=File.Exists(ffmpeg)}); return; }
                foreach(var entry in Jobs) if(entry.Value.Created<DateTime.UtcNow.AddMinutes(-30) && entry.Value.state!="GENERATING..." && entry.Value.state!="PROCESSING...") {
                    if(Jobs.TryRemove(entry.Key,out var expired)) expired.Cancel.Dispose();
                }
                if(method=="POST" && path=="/jobs") {


                    if(!headers.TryGetValue("Content-Type",out var contentType) || !contentType.StartsWith("application/json") || !headers.TryGetValue("Content-Length",out var lengthText) || !int.TryParse(lengthText,out int length) || length<1 || length>65536) throw new ArgumentException("JSON with a valid Content-Length is required.");
                    if(headers.TryGetValue("Expect",out var expect) && expect.Equals("100-continue",StringComparison.OrdinalIgnoreCase)) {
                        byte[] interim=Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                        await stream.WriteAsync(interim,0,interim.Length);
                    }
                    byte[] data=new byte[length]; int offset=0;
                    while(offset<length) { int count=await stream.ReadAsync(data.AsMemory(offset,length-offset),timeout.Token); if(count==0) throw new ArgumentException("Truncated request."); offset+=count; }
                    var q=JsonSerializer.Deserialize<VocalProxyRequest>(data,Json); Validate(q);
                    if(string.IsNullOrEmpty(key)) { await Respond(stream,503,new{error="Set ELEVENLABS_API_KEY in the backend environment or use --key-file outside the repository."}); return; }
                    if(!File.Exists(ffmpeg)) { await Respond(stream,503,new{error="Set VOCAL_FFMPEG_PATH to a working FFmpeg executable."}); return; }
                    if(Jobs.Count>=16 || !Slot.Wait(0)) { await Respond(stream,429,new{error="Proxy is busy or retains 16 jobs. No request was sent upstream."}); return; }
                    var job=new VocalProxyJob(); Jobs[job.id]=job; _=RunJob(q,job);
                    await Respond(stream,202,job); return;
                }
                string[] parts=path.Split('/',StringSplitOptions.RemoveEmptyEntries);
                if(parts.Length>=2 && parts[0]=="jobs" && Guid.TryParseExact(parts[1],"N",out _) && Jobs.TryGetValue(parts[1],out var found)) {
                    if(method=="GET" && parts.Length==2) { await Respond(stream,200,found); return; }
                    if(method=="DELETE" && parts.Length==2) { found.Cancel.Cancel(); await Respond(stream,200,new{state="CANCELLING"}); return; }
                    if(method=="GET" && parts.Length==3 && parts[2]=="audio" && found.state=="READY") { await Respond(stream,200,null,found.Audio,"audio/wav"); return; }
                }
                await Respond(stream,404,new{error="Unknown route or audio not ready."});
            } catch(Exception e) {
                try { await Respond(stream,400,new{error=e is ArgumentException ? e.Message : "Invalid request."}); } catch {}
            }
        }
    }
    public static async Task Main(string[] args) {
        if(args.Contains("--self-test")) { await VocalProxyTests.Run(); return; }
        key=Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        int fileOption=Array.IndexOf(args,"--key-file");
        if(fileOption>=0) {
            if(fileOption+1>=args.Length) throw new ArgumentException("--key-file requires a path outside the repository.");
            string file=Path.GetFullPath(args[fileOption+1]);
            string project=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../.."))+Path.DirectorySeparatorChar;
            if(file.StartsWith(project,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("API secrets must be outside the repository.");
            key=File.ReadAllText(file).Trim();
        }
        ffmpeg=Environment.GetEnvironmentVariable("VOCAL_FFMPEG_PATH")??"";
        var listener=new TcpListener(IPAddress.Loopback,8765); listener.Start();
        Console.WriteLine("Vocal proxy listening on http://127.0.0.1:8765. API credentials configured: "+(!string.IsNullOrEmpty(key))+". No paid request until explicit generation.");
        while(true) { var socket=await listener.AcceptTcpClientAsync(); _=Handle(socket); }
    }
}
