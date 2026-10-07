# ElevenLabs sung-vocal provider

The Unity selector supports MOCK and REAL AI. MOCK remains the default. REAL AI uses ElevenLabs Music v2.5 and its two-stem vocal separation API through a loopback backend. It does not use any TTS endpoint.

## Start the local proxy without putting a key in Unity

Open PowerShell in the Bacardi project and run:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\VocalProxy\Start-VocalProxy.ps1 -PromptForKey

The key prompt is masked. The key is placed only in that helper process's environment and inherited by the backend; it is not written to a file, Unity asset, command-line argument or repository. Keep that helper running while using REAL AI.

Alternatively, configure ELEVENLABS_API_KEY in the backend environment before launching the script, or use:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\VocalProxy\Start-VocalProxy.ps1 -KeyFile "C:\your-private-folder\elevenlabs.key"

A key file must reside outside this repository. Its contents are never printed. Do not paste keys into chat or commit them.

The helper automatically locates the existing Tools/ffmpeg executable and uses the installed Unity .NET 6 runtime/compiler. No Python or package installation is required. To override FFmpeg, set VOCAL_FFMPEG_PATH in the backend environment.

Unity connects to http://127.0.0.1:8765 by default. BACARDI_VOCAL_PROXY_URL is the optional endpoint configuration. That variable contains a proxy URL, never an ElevenLabs key. This supplied server binds only loopback, rejects browser Origin/chunked requests, has no CORS allowance, and pins upstream calls to api.elevenlabs.io without redirects. Do not expose this local helper publicly; a deployed backend requires HTTPS and user authentication/authorization.

## Free connectivity diagnosis

Start the proxy for a health-only check (no key prompt or paid request):

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\VocalProxy\Start-VocalProxy.ps1

Keep this process running. In Unity select REAL AI and press TEST REAL AI CONNECTION.
This sends only GET http://127.0.0.1:8765/health and displays PROXY ONLINE or a detailed
connection error. The Bacardi > TEST REAL AI CONNECTION (health only) editor menu
exercises the same action and saves Tools/VocalValidation/Unity-Proxy-Health.txt.
HTTP is correct for this IPv4 loopback server; it does not serve HTTPS. An
HTTP=N/A connection refusal with no Windows listener on 8765 means the local
proxy is stopped. Credentials missing in a successful health response means
connectivity works, but the backend must be restarted with credentials before generation.

## Real-hook workflow

1. Start the configured proxy, open Assets/Scenes/BacardiBeatMaker.unity, and enter Play mode.
2. Set beat tempo 104, vocal key C Major, Female, Lead, four bars, Dry. Turn off the optional A minor flavor for a strictly C Major request.
3. Use the default four-line hook and select REAL AI.
4. GENERATE VOCAL sends one composition request with the exact lyrics, duration, BPM/key/style/role/energy/emotion directions, followed by one two-stem separation request.
5. PREVIEW and listen: the provider must actually sing the supplied words. Check key, phrasing, pronunciation, voice gender and separation artifacts before treating the take as validated.
6. HOOK 1: 13 > ADD TO SONG, then HOOK 2: 17 > ADD TO SONG.
7. PLAY starts the independent DSP vocal timeline with the existing one-bar beat loop. Sources pass through the existing listener mastering path.
8. SAVE VOCAL WAV writes the isolated current take. EXPORT VOCAL STEM writes all added layers aligned from Bar 1 with silence before/after; stereo 44.1 kHz, 24-bit PCM and VOCAL_INFO.txt.

A four-bar hook at 104 BPM is 9.230769 seconds. The API composition plan asks for 9,231 ms. The proxy rejects output duration differences exceeding 250 ms; smaller codec/rounding differences are padded/cropped to the exact requested frame count. It never changes pitch to fit the duration. It imports only the extracted vocal stem, never the generated backing/full song.

BPM, key, voice gender, emotion and role are generative directions, not hard guarantees from the service. Vocal separation can leave reverb/instrument bleed. Format/timing checks cannot prove that the singer used the right notes or every requested word; a real generated take still needs audition.

## Tempo changes without pitch shifting

AudioSource.pitch stays at 1.0. When tempo changes, mismatched placed audio ends at the beat scheduler's next unscheduled DSP boundary, while the existing beat and independent musical tracker continue. PREVIEW and aligned export reject a vocal generated at a different BPM.

Press REGENERATE at the new project BPM. After successful generation, placements referencing the previous current take are updated to the new take and keep their original start bars. Other takes are preserved and must be regenerated individually or removed/re-added. Regeneration is explicit, may incur charges, and is never triggered automatically by tempo edits. The original beat data, mastering code and instrument/MIDI exports are unchanged.

## Backend behavior and limits

POST /jobs submits generation; GET /jobs/<id> polls status; GET /jobs/<id>/audio downloads the WAV; DELETE /jobs/<id> cancels the local job. GET /health reports configuration booleans only. Missing credentials/FFmpeg produce readable HTTP 503 errors. Upstream 401/403/429 errors show account, entitlement, quota or rate-limit guidance. No implicit mock fallback or paid retry is performed.

The local helper runs one generation at a time, retains up to 16 jobs, expires completed jobs after 30 minutes, caps downloads at 64 MB, and restricts requests to 80-150 BPM and 2/4/8/16 bars. Cancellation cannot reverse a request already processed/billed by ElevenLabs.

The proxy decodes the separated stem with FFmpeg, converts to stereo 44.1 kHz, and only attenuates peaks above -12 dBFS for mixing headroom. No vocal LUFS target, compressor or limiter is added. The existing master target and true-peak DSP are untouched.

## Pricing and access checked 5 October 2026

- ElevenLabs documents Music API access on paid plans: https://elevenlabs.io/docs/eleven-creative/products/music
- The current API pricing page lists music generation at US$0.15/minute before tax: https://elevenlabs.io/pricing/api
- At that listed proportional rate, a 9.230769-second composition is approximately US$0.023 for generation alone. This is not a total quote: plan minimums, billing granularity and stem-separation charges/entitlements can change the amount. The stem-separation API reference does not establish a separate price, so verify your account's usage dashboard.
- This implementation makes one composition plus one separation request per successful generation. Reusing the same take at Bars 13 and 17 costs no additional generation.
- The separation call requests mp3_44100_128 and locally converts it to 24-bit WAV; this avoids requiring the API's high-tier 44.1 kHz PCM output option. Conversion does not restore information lost in MP3.

Official endpoint/schema references:
https://elevenlabs.io/docs/api-reference/music/compose
https://elevenlabs.io/docs/eleven-api/guides/how-to/music/composition-plans
https://elevenlabs.io/docs/api-reference/music/separate-stems

## Unicode lyrics and singing languages (checked 5 October 2026)

The DEFAULT HOOK lyrics area accepts multiline clipboard text verbatim, including accents,
apostrophes, combining marks, non-Latin scripts and mixed languages. Ctrl+V and the native
Paste command replace the current selection; standard selection, copy and cut remain available.
GENERATE VOCAL and REGENERATE read the current box. Only GENERATE NEW LYRICS explicitly
creates a new offline four-line hook. The UI does not truncate pasted lyrics; REAL AI rejects
requests beyond its existing 4,000 UTF-16 code-unit limit with a readable error.

Unity serializes the current lyrics as JSON and posts UTF-8. The proxy parses UTF-8 JSON and
posts UTF-8 JSON to Music v2.5. The composition chunk contains `[Hook]\n` followed by the
exact original lyrics, with explicit directions against translation, rewriting and romanization.
No language detection, English-only validation or Unicode normalization is applied.

The connected model is ElevenLabs **Music v2.5**, not its multilingual speech/TTS models.
Official Music documentation confirms multilingual singing including English, Spanish,
German and Japanese, but does not provide an exhaustive Music language list confirming
isiZulu, Afrikaans, Xhosa, Portuguese, French, Korean, Chinese, Arabic or mixed-language
pronunciation. The REAL AI UI therefore displays “Selected vocal model may not support
this language” for all requests instead of guessing language from script or changing lyrics.
This is a coverage warning, not a claim that those languages are unsupported.

Source: https://elevenlabs.io/docs/eleven-creative/products/music
Model reference: https://elevenlabs.io/docs/overview/models

Offline tests cover the supplied isiZulu hook, accented Latin, Japanese/Korean/Chinese/Arabic,
mixed-language text, emoji, combining accents, CRLF, blank lines and edge spaces. Unity runtime
tests exercise the actual TextEditor clipboard operations and Unity JSON serialization; proxy
fixtures inspect the outgoing HTTP JSON. Synthetic tests do not verify actual pronunciation
or that the generative service sings every supplied word. Live multilingual singing has not
been auditioned as part of these offline checks.

## Validation

Run Start-VocalProxy.ps1 -SelfTest for offline HTTP/audio pipeline fixtures; no paid request is made. Run Tools/VocalValidation/Run-VocalValidation.ps1 -Runtime for Unity compile, real-provider-unavailable behavior, explicit mock workflow, both hook timings, mastering route, aligned export and regeneration at 120 BPM with pitch remaining 1.0.

An offline contract fixture is explicitly marked synthetic. It does not count as a real sung-hook validation. Live validation requires configured credentials and access to Music plus stem separation.

## Optional pronunciation / accent guidance

REAL AI now includes Pronunciation / Accent Guidance and a SOUTH AFRICAN / ISIZULU preset. The preset fills native isiZulu pronunciation instructions without editing lyrics or starting generation.

Unity sends `PronunciationGuidance` separately from `Lyrics` in UTF-8 JSON to POST /jobs. The proxy appends nonblank guidance to `composition_plan.chunks[0].positive_styles` in POST https://api.elevenlabs.io/v1/music, prefixed with `Vocal pronunciation/accent guidance only (never sung text; do not rewrite, translate, romanize or alter lyrics): `. Blank guidance adds no entry. Guidance is limited to 1,000 characters and recorded separately in export metadata. MOCK ignores it.

`AiPronunciationText` is optional and manually editable beneath guidance. COPY ORIGINAL LYRICS fills it with an exact copy; no phonetic spelling is invented. Null, empty, or whitespace-only values keep the existing chunk text (`[Hook]\n` plus original `Lyrics`). When populated, its exact Unicode contents replace only `composition_plan.chunks[0].text`, with no prefix or normalization. Original `Lyrics` remain in the request, result/arrangement metadata, and lyric exports; pronunciation text is recorded separately. MOCK ignores this sung-word override. GENERATE VOCAL and REGENERATE show the selected source. The proxy rejects pronunciation text over 4,000 characters rather than truncating it.

Warning shown in the app: `Pronunciation text changes what the AI receives as sung words; original lyrics are preserved separately.` Restart the local proxy after updating its source so it reads the new optional field.

Restart the running proxy with the existing startup command before the next controlled generation to compile/load the changed backend. SelfTest builds a separate DLL without interrupting the running proxy or contacting ElevenLabs. Actual accent quality requires auditioning an explicitly generated take.

STRICT LYRICS / WORD PRESERVATION and SLOWER PHRASE DENSITY are optional REAL AI toggles, off by default. They append separate positive_styles entries only. Strict guidance asks for every written word exactly once in order with no omissions, replacements or inventions. Density guidance requests a simpler melody and more phrase space at the unchanged project BPM. Neither option alters original lyrics, pronunciation text, duration or arrangement. MOCK ignores both; export metadata records the settings separately. Restart the proxy to load these options. Offline tests cover every toggle combination with original and pronunciation text.
