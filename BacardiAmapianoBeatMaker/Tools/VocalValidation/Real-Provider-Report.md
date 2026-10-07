# Real-provider integration report — 5 October 2026

ElevenLabs Music v2.5 is integrated behind IAiVocalProvider through a secure local/backend proxy. On 5 October 2026, the user started the credential-configured proxy; health confirmed configured=true and ffmpeg=true. One authorized real-hook acceptance attempt reached ElevenLabs and returned HTTP 402 (Payment Required). No real audio was returned, no automatic retry or mock fallback occurred, and real placements/export/master acceptance remain blocked. Account billing or Music API access must be checked before another attempt. The sanitized response does not identify the exact account restriction; any billing impact must be checked in the account. See Real-Sung-Acceptance.txt and Unity-Real-Sung.log.

## Provider selection

Verified primary documentation before selection:
- ElevenLabs Music: custom sung lyrics, composition-plan durations and separate vocal-stem API. https://elevenlabs.io/docs/api-reference/music/compose and https://elevenlabs.io/docs/api-reference/music/separate-stems
- fal ACE-Step: programmatic lyrics-to-music with downloadable audio. https://fal.ai/models/fal-ai/ace-step/api
- MiniMax Music: programmatic sung-song generation, but its documentation restricts new paid API users from August 20, 2026. https://platform.minimax.io/docs/guides/music-generation

The user selected ElevenLabs. No speech/TTS endpoint is used.

## Implemented behavior

MOCK / REAL AI selector; MOCK remains the default. REAL errors are readable and never silently fall back to MOCK. The existing mock remains a separate explicit selection.

ElevenLabsAiVocalProvider serializes the existing request, submits/polls a proxy job, downloads its WAV, checks format/duration, decodes stereo PCM, and measures vocal peak/LUFS-I with the existing meter. No API key is read in the Unity client.

The C#/.NET 6 proxy accepts exact lyrics, BPM, key/scale, voice/style, role, energy/emotion, bars and dry preference. It submits a music_v2_5 composition plan and then a two_stems_v1 separation request. Only the unambiguous vocal stem is imported. FFmpeg converts it to 44.1 kHz stereo; output is 24-bit PCM with exact requested musical frame length. Peak-only attenuation above -12 dBFS supplies mixing headroom; no vocal loudness normalization or DSP compressor is added.

BPM/key/gender/role/emotion are prompt directions and require checking in the actual output. Dry separation can retain artifacts or reverb. A production take must be auditioned; request metadata does not verify sung words or C Major notes.

At 104 BPM, native Bar 13/17 scheduling and aligned export remain unchanged: offsets 27.692307692 / 36.923076923 seconds, start frames 1,221,231 / 1,628,308.

Tempo conversion now uses explicit regeneration, not pitch-shifting/time stretching. Native pitch stays 1.0. A mismatch ends/waits at the next unscheduled boundary and prevents preview/aligned export. Successful REGENERATE updates placements referencing the previous current take while preserving start bars. Other takes are unchanged and need individual regeneration.

Original mastering/limiter source files remain byte-identical. The master target, approximately -1.3 dBTP protection, one-bar beat sequencing, mix controls, instrument/MIDI exporters and pattern data are unchanged.

## Completed validation

PASS:
- Unity runtime compilation and native Unity editor compilation/import, including the new real-audio acceptance helper.
- Native real provider unavailable error, no implicit fallback.
- Default mock Female / Lead / 104 BPM / C Major fixture, preview/stop, native Bars 13 and 17, isolated save and full-length aligned WAV.
- PLAY / PAUSE / resume / STOP, active mismatch ending at a scheduled boundary, explicit 120 BPM regeneration, unchanged native pitch and preserved Bars 13/17.
- Managed sample-accurate alignment, leading/trailing silence, exact regenerated-tempo output and cancellation.
- Offline API HTTP fixture: supplied sung-lyrics payload, correct composition duration/model, two-stem extraction, FFmpeg conversion and 44.1 kHz/24-bit stereo WAV with peak-only headroom.
- Offline provider 401 rejection with one call and no retry/fallback.
- Actual local HTTP proxy health/configuration and missing-key HTTP 503 behavior, without external calls.
- Original native Unity mastering regression: LIVE=-15.045651 LUFS-I, ANALYZED=-14.860887, difference=-0.184764 LU, true peak=-1.300000 dBTP, max reduction=2.641070 dB.
- Original 25 mix-balanced stem reconstruction and sparse-pattern/all-sounds regression; existing notes/velocities/pitches preserved.
- Protected original DSP/runtime source SHA-256 checks.

Real female sung hook / actual lyrics / actual C Major musical correctness / real-hook master safety: NOT VALIDATED YET. Mock results and HTTP fixtures do not substitute for these checks.

The explicit editor menu Bacardi > Validate REAL Sung Vocal (uses API), or BacardiRealVocalAcceptance.RunCommandLine in the isolated test project, is ready to generate one real take once the proxy has credentials. It checks real provenance, native preview, both complete hooks, WAV saves/alignment and master true-peak readout. It rejects mock fixtures. Audition remains required for words/key/voice quality.

## Files added

- Assets/Scripts/ElevenLabsAiVocalProvider.cs and matching .meta
- Assets/Editor/BacardiRealVocalAcceptance.cs and matching .meta
- Tools/VocalProxy/VocalProxy.cs
- Tools/VocalProxy/VocalProxyTests.cs
- Tools/VocalProxy/Start-VocalProxy.ps1
- Tools/VocalProxy/README.md
- Tools/VocalProxy/build.rsp, VocalProxy.dll, VocalProxy.runtimeconfig.json (generated)
- Tools/VocalValidation/RealProviderBaseline/*.txt (four preserved pre-change sources)
- Tools/VocalValidation/Proxy-Contract-Fixture.wav and Proxy-Contract-Validation.txt (synthetic, not real singing)
- Tools/VocalValidation/Proxy-Smoke.log, Proxy-Smoke.err and Proxy-Smoke-Validation.txt
- Tools/VocalValidation/Unity-Master-RealProvider-Regression.log/.txt
- This report

## Files modified

- Assets/Scripts/AiVocalProvider.cs: aligned export rejects BPM mismatches instead of resampling.
- Assets/Scripts/BacardiAiVocalGenerator.cs: provider selector, regeneration policy, mismatch handling, native pitch fixed to 1.0.
- Assets/Editor/BacardiVocalRuntimeTest.cs: native unavailable-provider and regeneration acceptance.
- Tools/VocalValidation/VocalCheck.cs: BPM mismatch/regeneration tests.
- Tools/VocalValidation/Run-VocalValidation.ps1: imports new explicit real-audio acceptance helper.
- AI-VOCALS.md: current workflow/limits.

Validation runs regenerated Tools/VocalValidation compiler response files/binaries/config, existing mock WAV/metadata fixtures, Managed-Validation.txt, Unity-Vocal.log and Unity-Vocal-Runtime.txt. Existing Balanced-Stem-01..25.wav and reconstruction report were regenerated by the original stem test. The isolated Tools/UnityRoutingProject mirrors these source changes and regenerates its imports, test reports and Library/Logs/Temp caches.

No new change was made to BacardiBeatMaker.cs, BacardiVocalTimeline.cs, original production scenes/settings, instruments, melodic MIDI export or mastering/DSP source during this integration.

## Credentials, costs and limitations

Run Tools/VocalProxy/Start-VocalProxy.ps1 -PromptForKey in PowerShell for masked input, or set ELEVENLABS_API_KEY only in the backend environment, or supply -KeyFile pointing outside the repository. BACARDI_VOCAL_PROXY_URL configures the Unity endpoint; default http://127.0.0.1:8765. VOCAL_FFMPEG_PATH is optional; helper finds the existing FFmpeg automatically.

The supplied server is loopback-only, rejects browser-origin requests, pins the upstream host without redirects, and never prints key values. Public deployment needs HTTPS and user authentication/authorization.

ElevenLabs documents Music API access on paid plans. Its current API pricing page lists generation at US$0.15/minute before tax: https://elevenlabs.io/pricing/api . Proportional generation-only arithmetic is about US$0.023 for the 9.230769-second hook; this is not a total charge quote. Stem separation, plan minimums and billing granularity/entitlements must be checked in the account. API charges are not automatically retried or refunded by cancellation.

One generation uses one music request and one separation request. Two placements of the same take cost no extra generation. Local limits: one generation at a time, 16 retained jobs, 30-minute completed-job expiry, 64 MB download cap, 80-150 BPM and 2/4/8/16 bars. The separation call uses MP3 44.1 kHz/128 kbps and local 24-bit conversion; that conversion cannot restore lossy source information.

Lyrics, key, tempo, voice character and truly dry stem quality need audition. No automatic key detection, lyric transcription, pitch-preserving stretching or full-song vocal master exporter is added. Session-local placements and existing one-bar exports are preserved.
