# Master LUFS validation

The runtime scripts compiled against Unity 6000.3.24f1 assemblies with no errors.
Existing JSON-populated export fields produced CS0649 warnings.

Check.cs feeds stereo float PCM to the real meter engine in 1024-frame chunks,
checks that the input is unchanged, and checks a fresh session with four seconds
of silence produces NaN internally (the UI formats missing values as `--`).

FFmpeg 9.0.2 `ebur128` comparisons used 12-second stereo sine signals with
amplitudes 0.1 for 4 s, 0.005 for 4 s, then 0.05 for 4 s. This exercises the
relative gate as well as frequency weighting. Results in LUFS:

| Rate | Tone | Engine S | FFmpeg S | Engine I | FFmpeg I |
|---|---|---|---|---|---|
| 44100 | 60 | -29.607 | -29.6 | -25.794 | -25.8 |
| 44100 | 997 | -26.018 | -26.0 | -22.204 | -22.2 |
| 44100 | 8000 | -22.668 | -22.7 | -18.854 | -18.9 |
| 48000 | 60 | -29.611 | -29.6 | -25.798 | -25.8 |
| 48000 | 997 | -26.021 | -26.0 | -22.207 | -22.2 |
| 48000 | 8000 | -22.672 | -22.7 | -18.858 | -18.9 |

The meter attaches to the scene's AudioListener at runtime. It reads mixed PCM
without modifying it or calling Unity APIs from the audio thread. The main thread
consumes 100 ms energy slices, computes the 3 s short-term window, and retains
400 ms blocks with 75% overlap for exact session re-gating at -70 LUFS and 10 LU
below the absolute-gated mean. Short-term is ungated. No display smoothing is
added beyond the specified window; one-decimal text refreshes at approximately
10 Hz. Integrated begins after 400 ms; short-term after a full 3 s window.
Silence clears short-term after the window drains, while the previous integrated
session result remains available. Starting playback resets both measurements;
this also covers automatic playback of a newly generated song. Audio device
configuration changes reset the meter for the new rate/channel layout.

The implementation uses runtime APIs and has no Editor-only dependency. Live
Game-view capture and a built executable were not exercised in this validation.
These comparisons are numerical checks, not complete EBU certification.

References: https://www.itu.int/rec/R-REC-BS.1770 and
https://tech.ebu.ch/docs/tech/tech3341.pdf

## Optional -14 LUFS mastering validation (2026-10-04)

The completed 44.1 kHz / 24-bit stereo `Target Master.wav` measured **-14.1 LUFS-I** and **-1.3 dBTP** with independent FFmpeg 9.0.2 `ebur128=peak=true`. Input integrated loudness was -16.404 LUFS and the applied fixed master gain was **+3.1 dB**. The limiter uses a -1.3 dB internal threshold to leave a 0.3 dB margin beneath the requested -1.0 dBTP ceiling.

| Fixture | FFmpeg LUFS-I | FFmpeg dBTP | Applied gain |
|---|---:|---:|---:|
| All-25-sound demo arrangement, full tails | -14.1 | -1.3 | +3.1 dB |
| 19 kHz stereo tone | -14.0 | -16.9 | -16.4 dB |
| Stereo noise, opposite channel polarity | -14.0 | -10.5 | -14.4 dB |
| Sparse high crest-factor impulses | -31.2 | -1.3 | -0.4 dB |

The impulse result intentionally remains below target instead of crushing the waveform. A demo attenuated by 20 dB hits the +18 dB boost cap and remains at -18.4 LUFS-I. A demo boosted by 9.54 dB reaches -14.1 LUFS-I with -6.4 dB master gain. Silence and extremely quiet input are rejected. Streaming in 1024-frame chunks produces identical PCM to an offline limiter pass, including the lookahead tail. All runtime scripts compile without errors against the installed Unity assemblies.

`MasterCheck.cs` contains the default demo, synthesis, arrangement renderer and 24-bit WAV writer extracted from the application, plus a managed math shim. It runs the actual `BacardiMastering` / `BacardiLufsEngine` implementations. This validates completed WAV audio and DSP; it does not simulate Unity's audio device or click the in-game export buttons. Unity Game-view appearance, subjective listening and a standalone build were not exercised. Tempo, swing, generation, saving/loading and MIDI logic were not modified. The stem exporter now uses its existing shared gain and no longer calls independent stem normalization.

Re-run from the project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/LufsValidation/Run-MasterValidation.ps1
```

The script compiles the runtime scripts and validation harness, renders fixtures and checks each completed WAV with FFmpeg. Runtime mastering uses BS.1770-style integrated measurement and 8x, 32-tap reconstructed peaks; it is not a certification claim. Reference: https://www.itu.int/rec/R-REC-BS.1770

Files changed or added for this implementation:

- `Assets/Scripts/BacardiBeatMaker.cs`
- `Assets/Scripts/BacardiMasterLufsMeter.cs`
- `Assets/Scripts/BacardiLoudness.cs`
- `Assets/Scripts/BacardiMastering.cs` (new)
- `Assets/Scripts/BacardiMastering.cs.meta` (new)
- `BEAT-MAKER.md`
- `Tools/LufsValidation/README.md`
- `Tools/LufsValidation/MasterCheck.cs` (new validation harness)
- `Tools/LufsValidation/Run-MasterValidation.ps1` (new validation runner)
- `Tools/LufsValidation/Target Master.wav` (new completed master fixture)
- `Tools/LufsValidation/Target-measurement.txt` (new independent measurement)
- `Tools/LufsValidation/HF-measurement.txt`
- `Tools/LufsValidation/Noise-measurement.txt`
- `Tools/LufsValidation/Impulse-measurement.txt`

The runner also generates compiler response files, compiled DLLs, a runtime configuration and HF/Noise/Impulse WAV fixtures. These temporary artifacts were removed after validation; the runner recreates them as needed. Existing `Check.cs`, `Check.runtimeconfig.json` and `BacardiLufsEngine.cs` were unchanged.

## Comparison follow-up
See [Listening-Safety-Report.md](Listening-Safety-Report.md) for bypass, gain reduction, toggle continuity, level-matched listening files and the repeated-playback limitation.


## Current deterministic policy
See [Deterministic-Mastering-Report.md](Deterministic-Mastering-Report.md). The new 3 dB limiter budget and loop-aware fixed gain supersede the earlier 5.6-6.4 dB results. Three fresh runs and six independently measured WAVs verify identical results for identical programs.

