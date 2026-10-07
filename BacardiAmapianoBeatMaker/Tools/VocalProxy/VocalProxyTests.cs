using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
public static class VocalProxyTests {
    static void Assert(bool ok,string reason) { if(!ok) throw new Exception(reason); }
    sealed class FakeApi : HttpMessageHandler {
        public int Calls; public bool Fail;
        public byte[] Fixture;
        public string ExpectedLyrics;
        public string ExpectedGuidance;
        public string ExpectedPronunciationText;
        public bool ExpectedStrict, ExpectedSlower;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage q,CancellationToken token) {
            Calls++;
            Assert(q.RequestUri.Host=="api.elevenlabs.io","Pinned upstream host");
            Assert(q.Headers.Contains("xi-api-key"),"Server-side credential header");
            if(Fail) return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("Invalid key TEST; original backend detail") };
            if(Calls==1) {
                using var payload=JsonDocument.Parse(await q.Content.ReadAsStringAsync());
                Assert(payload.RootElement.GetProperty("model_id").GetString()=="music_v2_5","Music model, not TTS");
                Assert(!payload.RootElement.TryGetProperty("prompt",out _),"Composition plan and prompt not mixed");
                var chunk=payload.RootElement.GetProperty("composition_plan").GetProperty("chunks")[0];
                Assert(chunk.GetProperty("duration_ms").GetInt32()==9231,"Exact requested musical duration");
                Assert(chunk.GetProperty("text").GetString()==(string.IsNullOrWhiteSpace(ExpectedPronunciationText) ? "[Hook]\n"+ExpectedLyrics : ExpectedPronunciationText),"Exact selected Unicode sung words at upstream UTF-8 HTTP boundary");
                var styles=chunk.GetProperty("positive_styles");
                int baseCount=string.IsNullOrWhiteSpace(ExpectedGuidance) ? 13 : 14;
                Assert(styles.GetArrayLength()==baseCount+(ExpectedStrict ? 1 : 0)+(ExpectedSlower ? 1 : 0),"Options add only separate style entries");
                if(ExpectedStrict) Assert(styles[baseCount].GetString()==VocalProxy.StrictLyricsGuidance,"Strict word guidance separate from sung text");
                if(ExpectedSlower) Assert(styles[baseCount+(ExpectedStrict ? 1 : 0)].GetString()==VocalProxy.SlowerPhraseDensityGuidance,"Slower phrase guidance separate from sung text");
                if(!string.IsNullOrWhiteSpace(ExpectedGuidance)) Assert(styles[13].GetString()=="Vocal pronunciation/accent guidance only (never sung text; do not rewrite, translate, romanize or alter lyrics): "+ExpectedGuidance,"Guidance separate from sung text");
                Assert(q.Content.Headers.ContentType.CharSet=="utf-8","UTF-8 request content type");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(Fixture) };
            }
            Assert(q.RequestUri.AbsolutePath.EndsWith("/stem-separation"),"Separate vocals, never import backing");
            Assert((await q.Content.ReadAsStringAsync()).Contains("two_stems_v1"),"Two-stem mode");
            using var bytes=new MemoryStream();
            using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true)) {
                using(var voice=zip.CreateEntry("vocals.wav").Open()) voice.Write(Fixture);
                using(var backing=zip.CreateEntry("instrumental.wav").Open()) backing.Write(new byte[8]);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=new ByteArrayContent(bytes.ToArray()) };
        }
    }
    static byte[] Wave(int frames) {
        using var stream=new MemoryStream(); using(var w=new BinaryWriter(stream,Encoding.ASCII,true)) {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36+frames*4);
            w.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(44100); w.Write(176400); w.Write((short)4); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(frames*4);
            for(int i=0;i<frames;i++) { short v=(short)(Math.Sin(i*2*Math.PI*440/44100)*22000); w.Write(v); w.Write(v); }
        }
        return stream.ToArray();
    }
    public static async Task Run() {
        string ff=Environment.GetEnvironmentVariable("VOCAL_FFMPEG_PATH")??"";
        Assert(File.Exists(ff),"Set VOCAL_FFMPEG_PATH for conversion test.");
        var q=new VocalProxyRequest { Lyrics="Come closer, don\u2019t let go\nMove with me, nice and slow\nAll night, feel the glow\nWhere we\u2019re going, only we know" };
        var fake=new FakeApi { Fixture=Wave((int)Math.Round(q.Seconds*44100)), ExpectedLyrics=q.Lyrics };
        using var client=new HttpClient(fake);
        var job=new VocalProxyJob { isMock=true,provider="CONTRACT FIXTURE ONLY; NOT REAL SINGING" };
        byte[] output=await VocalProxy.Generate(q,job,client,"TEST-KEY-NOT-A-CREDENTIAL",ff,CancellationToken.None);
        Assert(fake.Calls==2,"One composition and one separation; no duplicate billed calls");
        using(var r=new BinaryReader(new MemoryStream(output))) {
            r.BaseStream.Position=22; Assert(r.ReadInt16()==2 && r.ReadInt32()==44100,"Stereo 44100");
            r.BaseStream.Position=34; Assert(r.ReadInt16()==24,"24-bit PCM");
            Assert(output.Length==44+(int)Math.Round(q.Seconds*44100)*6,"Exact bars");
            r.BaseStream.Position=44;
            double peak=0;
            while(r.BaseStream.Position<output.Length) {
                int sample=r.ReadByte() | r.ReadByte()<<8 | r.ReadByte()<<16;
                if((sample&0x800000)!=0) sample|=unchecked((int)0xff000000);
                peak=Math.Max(peak,Math.Abs(sample/8388608.0));
            }
            Assert(peak<=.250001 && peak>.24,"Peak-only headroom trim");
        }
        var bad=new FakeApi { Fail=true };
        using var denied=new HttpClient(bad);
        try { await VocalProxy.Generate(q,new VocalProxyJob(),denied,"TEST",ff,CancellationToken.None); throw new Exception("401 should fail"); }
        catch(AiVocalFailure e) { Assert(e.HttpStatus == 401 && e.Stage == "generation" && e.Message.Contains("original backend detail") && !e.Message.Contains("TEST") && e.UiMessage.Contains("Invalid ElevenLabs API key"),"Readable account failure with redacted backend body"); Assert(bad.Calls==1,"No implicit retry or fallback"); }
        string[] unicodeLyrics = {
            "Sondela kimi, ungangidedeli\nHamba nami kancane\nSizodansa ubusuku bonke\nLapho siya khona, sazi thina",
            "  Coração, dança comigo!\r\nL’été — près de toi; canción.\n\nNão me deixes…  ",
            "君と踊ろう\n함께 춤춰요\n一起跳舞\nارقص معي — Sondela kimi 🎵 e\u0301"
        };
        foreach(string lyrics in unicodeLyrics) {
            // Emulate Unity's UTF-8 JSON upload into the backend, then inspect its upstream HTTP body.
            string guidance="Native South African isiZulu pronunciation. Natural isiZulu vowels and consonants. Avoid English, American or European accenting. Keep lyrics exactly unchanged.";
            byte[] upload=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new VocalProxyRequest { Lyrics=lyrics, PronunciationGuidance=guidance },new JsonSerializerOptions { IncludeFields=true }));
            var received=JsonSerializer.Deserialize<VocalProxyRequest>(upload,new JsonSerializerOptions { IncludeFields=true });
            Assert(received.Lyrics==lyrics,"Unity-to-proxy Unicode round trip");
            Assert(received.PronunciationGuidance==guidance,"Separate guidance round trip");
            var unicodeApi=new FakeApi { Fixture=fake.Fixture,ExpectedLyrics=lyrics,ExpectedGuidance=guidance };
            using var unicodeClient=new HttpClient(unicodeApi);
            await VocalProxy.Generate(received,new VocalProxyJob(),unicodeClient,"TEST",ff,CancellationToken.None);
            Assert(unicodeApi.Calls==2,"Unicode generation and stem extraction");
        }
        q.PronunciationGuidance=new string('a',1001);
        try { VocalProxy.Validate(q); throw new Exception("Oversized guidance accepted"); } catch(ArgumentException) {}
        q.PronunciationGuidance=null; VocalProxy.Validate(q);
        string original=q.Lyrics;
        foreach(string pronunciation in new[] { null, "", " \r\n\t", "  Son-de-la kimi\r\n\u00e9 e\u0301 \u65e5\u672c\ud83c\udfb5\n\n  " }) {
            q.AiPronunciationText=pronunciation;
            VocalProxy.Validate(q);
            var received=JsonSerializer.Deserialize<VocalProxyRequest>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(q,new JsonSerializerOptions { IncludeFields=true })),new JsonSerializerOptions { IncludeFields=true });
            Assert(received.Lyrics==original && received.AiPronunciationText==pronunciation,"Independent original/pronunciation UTF-8 fields");
            var pronunciationApi=new FakeApi { Fixture=fake.Fixture,ExpectedLyrics=original,ExpectedPronunciationText=pronunciation };
            using var pronunciationClient=new HttpClient(pronunciationApi);
            await VocalProxy.Generate(received,new VocalProxyJob(),pronunciationClient,"TEST",ff,CancellationToken.None);
            Assert(received.Lyrics==original && q.Lyrics==original,"Generation never overwrites canonical lyrics");
            // Compare every upstream property except sung chunk text.
            var baseline=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(VocalProxy.Payload(new VocalProxyRequest { Lyrics=original })));
            var overridden=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(VocalProxy.Payload(received)));
            overridden["composition_plan"]["chunks"][0]["text"]=baseline["composition_plan"]["chunks"][0]["text"].GetValue<string>();
            Assert(overridden.ToJsonString()==baseline.ToJsonString(),"Pronunciation text changes ONLY chunk text");
        }
        foreach(string strictOriginal in unicodeLyrics)
        foreach(bool strict in new[] { false,true }) foreach(bool slower in new[] { false,true }) foreach(string pronunciation in new[] { null, "", " \r\n\t", "  Sondela e\u0301 \ud83c\udfb5\r\nHamba nami  " }) {
            var settings=new VocalProxyRequest { Lyrics=strictOriginal, AiPronunciationText=pronunciation, PronunciationGuidance="Native isiZulu", StrictLyrics=strict, SlowerPhraseDensity=slower };
            var received=JsonSerializer.Deserialize<VocalProxyRequest>(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings,new JsonSerializerOptions { IncludeFields=true })),new JsonSerializerOptions { IncludeFields=true });
            Assert(received.StrictLyrics==strict && received.SlowerPhraseDensity==slower,"Toggle serialization");
            var optionApi=new FakeApi { Fixture=fake.Fixture,ExpectedLyrics=strictOriginal,ExpectedPronunciationText=pronunciation,ExpectedGuidance=settings.PronunciationGuidance,ExpectedStrict=strict,ExpectedSlower=slower };
            using var optionClient=new HttpClient(optionApi);
            await VocalProxy.Generate(received,new VocalProxyJob(),optionClient,"TEST",ff,CancellationToken.None);
            Assert(received.Lyrics==strictOriginal && received.AiPronunciationText==pronunciation && received.Bpm==104 && received.Bars==4,"Options preserve words, Unicode, order and BPM");
            var baseline=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(VocalProxy.Payload(new VocalProxyRequest { Lyrics=strictOriginal,AiPronunciationText=pronunciation,PronunciationGuidance=settings.PronunciationGuidance })));
            var actual=System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(VocalProxy.Payload(received)));
            actual["composition_plan"]["chunks"][0]["positive_styles"]=System.Text.Json.Nodes.JsonNode.Parse(baseline["composition_plan"]["chunks"][0]["positive_styles"].ToJsonString());
            Assert(actual.ToJsonString()==baseline.ToJsonString(),"Strict/density options change ONLY positive_styles");
        }
        Console.WriteLine("PASS strict lyrics and slower phrase density: all toggle combinations preserve exact sung text and BPM at offline HTTP boundary; style guidance only.");
        q.AiPronunciationText=new string('x',4001);
        try { VocalProxy.Validate(q); throw new Exception("Oversized pronunciation text accepted"); } catch(ArgumentException) {}
        q.AiPronunciationText="";
        q.Bpm=0;
        try { VocalProxy.Validate(q); throw new Exception("Invalid tempo accepted"); } catch(ArgumentException) {}
        Directory.CreateDirectory("Tools/VocalValidation");
        File.WriteAllBytes("Tools/VocalValidation/Proxy-Contract-Fixture.wav",output);
        string report="PASS offline ElevenLabs contract: correct Music v2.5 composition plan and supplied lyrics; two-stem extraction; FFmpeg conversion; exact 44.1 kHz 24-bit stereo bars; peak-only headroom; credential rejection/no retry.\nTHIS IS A SYNTHETIC HTTP/AUDIO FIXTURE, NOT A REAL ELEVENLABS GENERATION.\n";
        report+="PASS exact UTF-8 lyrics: supplied isiZulu hook, accented Latin with CRLF/blank lines/edge spaces, Japanese/Korean/Chinese/Arabic/mixed text, emoji and combining accents.\n";
        report+="PASS strict lyrics and slower phrase density: all toggle combinations with Unicode originals and null/empty/whitespace/populated pronunciation text; exact sung text at fake HTTP boundary; only positive_styles changes; BPM and duration unchanged.\n";
        report+="PASS optional pronunciation guidance: separate JSON field, style-only upstream entry, unchanged lyric text, empty legacy payload, null and size validation.\n";
        report+="PASS optional AI pronunciation text: null/empty/whitespace preserve legacy original payload; populated Unicode reaches fake ElevenLabs HTTP body exactly and changes only chunks[0].text; original lyrics unchanged; independent UTF-8 fields; size validation. No external requests.\n";
        File.WriteAllText("Tools/VocalValidation/Proxy-Contract-Validation.txt",report); Console.WriteLine(report);
    }
}
