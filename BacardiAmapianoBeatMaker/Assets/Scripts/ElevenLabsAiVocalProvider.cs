using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// Service credentials live ONLY in the backend. Selection/generation never auto-falls back.
public sealed class ElevenLabsAiVocalProvider : IAiVocalProvider {
    public string Name { get { return "ElevenLabs Music via vocal proxy"; } }
    readonly Uri endpoint;
    public string ProxyUrl { get { return endpoint.AbsoluteUri; } }
    [Serializable] sealed class Health { public string provider = ""; public bool configured = false, ffmpeg = false; }
    // Connectivity only: this method never submits a job or contacts ElevenLabs.
    public async Task<string> TestConnectionAsync() {
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5))) {
            try {
                using (var response = await client.GetAsync(new Uri(endpoint, "health"), timeout.Token).ConfigureAwait(false)) {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) return "PROXY CONNECTION ERROR: HTTP " + (int)response.StatusCode + "; " + AiVocalFailure.Clean(body);
                    var health = JsonUtility.FromJson<Health>(body);
                    if (health == null || health.provider != "ElevenLabs Music") return "PROXY CONNECTION ERROR: HTTP 200; unexpected VocalProxy health response";
                    return "PROXY ONLINE\n" + ProxyUrl + " | HTTP 200 | Backend credentials: " + (health.configured ? "configured" : "missing") + " | FFmpeg: " + (health.ffmpeg ? "available" : "missing") + "\nConnectivity check only; ElevenLabs was not contacted.";
                }
            } catch (OperationCanceledException e) { return "PROXY CONNECTION ERROR: " + ProxyUrl + "; HTTP=N/A; health check timed out after 5 seconds; " + AiVocalFailure.Clean(e.ToString());
            } catch (Exception e) { return "PROXY CONNECTION ERROR: " + ProxyUrl + "; HTTP=N/A; " + AiVocalFailure.Clean(e.ToString()); }
        }
    }
    static readonly HttpClient client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(12) };
    [Serializable] sealed class Job {
        public string id="", state="", error="", provider="";
        public string stage="generation"; public int httpStatus; public float progress=0;
        public bool isMock=false;
    }
    public ElevenLabsAiVocalProvider() : this(Environment.GetEnvironmentVariable("BACARDI_VOCAL_PROXY_URL") ?? "http://127.0.0.1:8765/") {}
    public ElevenLabsAiVocalProvider(string url) {
        endpoint = new Uri(url.EndsWith("/") ? url : url + "/");
        if ((!endpoint.IsLoopback && endpoint.Scheme != "https") || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query))
            throw new ArgumentException("Proxy must use loopback HTTP or authenticated HTTPS deployment; no credentials in its URL.");
    }
    public Task<AiVocalResult> GenerateAsync(AiVocalRequest request, IProgress<AiVocalProgress> progress, CancellationToken token) {
        string payload = JsonUtility.ToJson(request);
        return GenerateCore(request, payload, progress, token);
    }
    async Task<AiVocalResult> GenerateCore(AiVocalRequest request, string payload, IProgress<AiVocalProgress> progress, CancellationToken token) {
        string jobId = null; bool completed = false; string stage = "generation";
        try {
            progress.Report(new AiVocalProgress("GENERATING...", .02f));
            using (var body = new StringContent(payload, Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(new Uri(endpoint, "jobs"), body, token).ConfigureAwait(false)) {
                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw new AiVocalFailure(stage, (int)response.StatusCode, json);
                var job = JsonUtility.FromJson<Job>(json);
                if (job == null || !Guid.TryParseExact(job.id, "N", out _)) throw new Exception("Proxy returned an invalid job identifier.");
                jobId = job.id;
            }
            while (true) {
                token.ThrowIfCancellationRequested();
                using (var response = await client.GetAsync(new Uri(endpoint, "jobs/" + jobId), token).ConfigureAwait(false)) {
                    if (!response.IsSuccessStatusCode) throw new AiVocalFailure(stage, (int)response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var job = JsonUtility.FromJson<Job>(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    if (job == null) throw new Exception("Invalid vocal job response.");
                    stage = string.IsNullOrEmpty(job.stage) ? "generation" : job.stage; if (job.state == "ERROR") throw new AiVocalFailure(stage, job.httpStatus, job.error);
                    if (job.state == "CANCELLED") throw new OperationCanceledException(token);
                    progress.Report(new AiVocalProgress(job.state, job.progress));
                    if (job.state == "READY") {
                        if (job.isMock) throw new Exception("REAL AI returned LOCAL_MOCK audio; generation rejected. No mock fallback is allowed.");
                        stage = "download"; progress.Report(new AiVocalProgress("DOWNLOADING...", .9f));
                        byte[] wav;
                        using (var audio = await client.GetAsync(new Uri(endpoint, "jobs/" + jobId + "/audio"), token).ConfigureAwait(false)) {
                            if (!audio.IsSuccessStatusCode) throw new AiVocalFailure(stage, (int)audio.StatusCode, await audio.Content.ReadAsStringAsync().ConfigureAwait(false));
                            if (audio.Content.Headers.ContentLength > 64 * 1024 * 1024) throw new Exception("Vocal download exceeds the 64 MB limit.");
                            wav = await audio.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        }
                        progress.Report(new AiVocalProgress("PROCESSING...", .96f));
                        stage = "decoding"; float[] pcm = DecodeWav(wav);
                        int expected = (int)Math.Round(request.Seconds * 44100) * 2;
                        if (pcm.Length != expected) throw new Exception("Proxy vocal duration does not match requested bars.");
                        double peak = 0; foreach (float value in pcm) peak = Math.Max(peak, Math.Abs(value));
                        completed = true;
                        return new AiVocalResult { Request = request, Provider = job.provider, IsMock = false, Source = "REAL_AI",
                            Pcm = pcm, PeakDb = 20 * Math.Log10(Math.Max(1e-20, peak)),
                            Integrated = BacardiMastering.Measure(pcm, 44100) };
                    }
                }
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        } catch (AiVocalFailure) { throw;
        } catch (OperationCanceledException e) {
            if (token.IsCancellationRequested) throw;
            throw new AiVocalFailure(stage, 0, "Request timed out (HTTP client timeout: 12 minutes).", e);
        } catch (HttpRequestException e) {
            throw new AiVocalFailure(stage, 0, "Vocal proxy unreachable: " + e.Message, e);
        } catch (Exception e) { throw new AiVocalFailure(stage, 0, e.Message, e);
        } finally {
            if (!completed && jobId != null) {
                try {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    using (var cancelled = await client.DeleteAsync(new Uri(endpoint, "jobs/" + jobId), timeout.Token).ConfigureAwait(false)) {}
                } catch { /* Cancellation cannot undo service work already billed. */ }
            }
        }
    }
    static string SafeError(string json) {
        try { return JsonUtility.FromJson<Job>(json).error ?? "proxy rejected the request"; }
        catch { return "proxy rejected the request"; }
    }
    public static float[] DecodeWav(byte[] wav) {
        if (wav == null || wav.Length < 44 || wav.Length > 64 * 1024 * 1024) throw new ArgumentException("Invalid WAV size.");
        using (var r = new BinaryReader(new MemoryStream(wav))) {
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "RIFF") throw new ArgumentException("RIFF WAV required.");
            r.ReadInt32();
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "WAVE") throw new ArgumentException("WAVE required.");
            bool format = false;
            while (r.BaseStream.Position + 8 <= wav.Length) {
                string id = Encoding.ASCII.GetString(r.ReadBytes(4)); int size = r.ReadInt32();
                long start = r.BaseStream.Position, end = start + size;
                if (size < 0 || end > wav.Length) throw new ArgumentException("Truncated WAV chunk.");
                if (id == "fmt ") {
                    if (size < 16 || r.ReadInt16() != 1 || r.ReadInt16() != 2 || r.ReadInt32() != 44100)
                        throw new ArgumentException("44.1 kHz stereo PCM required.");
                    r.ReadInt32();
                    if (r.ReadInt16() != 6 || r.ReadInt16() != 24) throw new ArgumentException("24-bit PCM required.");
                    format = true;
                } else if (id == "data") {
                    if (!format || size == 0 || size % 6 != 0) throw new ArgumentException("Invalid stereo WAV data.");
                    var pcm = new float[size / 3];
                    for (int i = 0; i < pcm.Length; i++) {
                        int sample = r.ReadByte() | (r.ReadByte() << 8) | (r.ReadByte() << 16);
                        if ((sample & 0x800000) != 0) sample |= unchecked((int)0xff000000);
                        pcm[i] = sample / 8388608f;
                    }
                    return pcm;
                }
                r.BaseStream.Position = end + (size & 1);
            }
        }
        throw new ArgumentException("WAV has no PCM data.");
    }
}
