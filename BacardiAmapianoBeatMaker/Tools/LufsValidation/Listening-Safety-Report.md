# Mastering comparison and listening-safety pass

2026-10-04. Numerical validation passed; subjective listening and Unity Game-view operation were not performed. No claim is made that the limiter is inaudible. The complete built-in demo is one 16-step bar using all 25 sounds, with full sample tails. A separate 16-bar repetition tests overlapping tails and sustained bass/percussion activity.

## Complete demo export

Independent FFmpeg 9.0.2 `ebur128=peak=true` measurements of completed 24-bit stereo WAVs:

| State | Integrated loudness | True peak | Gain |
|---|---:|---:|---:|
| Raw original stereo sum, before output clipping | -16.4 LUFS-I | +1.2 dBTP (built-in reconstructed-peak estimate) | 0 dB |
| Level-safe bypass listening/export | -18.9 LUFS-I | -1.3 dBTP | -2.5 dB shared safety trim |
| Target mastered export | -14.1 LUFS-I | -1.3 dBTP | +3.1 dB master gain |
| Level-matched mastered listening copy | -18.9 LUFS-I | -6.1 dBTP | Mastered copy attenuated by 4.8 dB |

Limiting occurred. Maximum limiter reduction on the completed demo: **5.628 dB**. The limiter thresholds, release, gain analysis and final settled master output are unchanged. A regression test compares the settled live comparison wet path to the original offline processed samples within 1e-6.

Raw original peaks exceed full scale; bypass therefore uses a clearly displayed shared stereo safety trim. It does not alter individual instrument levels or normalize stems. A peak protector on the dry branch remains available to catch newly louder input after edits. With the analyzed demo trim, the dry path does not need limiting once settled. Bypass is level-safe; it is not automatically loudness-matched to the -14 master. Use the paired level-matched WAVs below to avoid louder-is-better listening bias.

## Toggle safety

Both branches stay warm and have identical 31-frame lookahead delay. A 50 ms linear crossfade changes the selection without resetting the audio history. Gain edits also ramp. Meter resets leave DSP history intact. Tests toggle enabled/disabled and bypass repeatedly with constant stereo input, isolating switching steps from musical waveform transients. The largest adjacent-sample change was 0.00006351, within the expected smooth ramp increment; no instantaneous full-gain jump occurred. Stereo ratio remained unchanged. These are numerical continuity checks, not an auditory click test on the user's device.

## Repeated playback finding

| 16-bar repeated demo | LUFS-I | True peak | Master gain | Maximum GR |
|---|---:|---:|---:|---:|
| Using the existing one-bar analysis gain (current live playback behavior) | -12.3 | -1.3 dBTP | +3.1 dB | 6.381 dB |
| Analyzing the complete 16-bar render | -14.1 | -1.3 dBTP | +0.7 dB | 3.898 dB |

The raw repeated arrangement measures -14.326 LUFS-I, with an estimated +1.944 dBTP. Natural overlapping sample tails make its integrated loudness different from the finite one-bar export. Current application exports and analyzes one bar plus tails; it does not have a native multi-bar arrangement. The longer test is diagnostic and does not add an arrangement feature. Existing gain behavior was preserved as requested. Peak reduction reaching 5.6-6.4 dB warrants listening to kick/logdrum attacks and bass recovery before calling the limiter transparent. No channel mix changes or limiter tuning were made based on an unverified subjective assumption.

## Listen

- `Bypass Master.wav`: original demo stereo sum with safety trim.
- `Level Matched Master.wav`: processed demo attenuated to match bypass LUFS-I; compare these two at a fixed playback volume.
- `Target Master.wav`: actual -14.1 LUFS target export.
- `Fixed Gain Loop Master.wav`: repeated demo with the application's one-bar fixed gain.
- `Looped Master.wav`: repeated demo analyzed as a complete longer render.

In Unity, press Play, Analyze / Set -14 LUFS, and toggle BYPASS MASTERING during playback. Listen for kick attack loss, logdrum/bass breathing, percussion tail modulation and tonal dulling. The listener meter reports actual blended output; first measurements after switching include the transition. Subjective approval remains with the user.

## Validation and files

All runtime scripts compile against the installed Unity assemblies. Silence/quiet guards, original fixture loudness/peak tests, streaming/offline parity, comparison wet-path identity, stereo balance and smooth toggles pass. `Run-MasterValidation.ps1` recreates all fixtures and independent FFmpeg measurement logs. No Unity device playback, UI click-through, MIDI integration run or standalone build was performed.

Changed/added source and documentation this pass:

- `Assets/Scripts/BacardiBeatMaker.cs`
- `Assets/Scripts/BacardiMasterLufsMeter.cs`
- `Assets/Scripts/BacardiMastering.cs`
- `Assets/Scripts/BacardiMasterComparison.cs`
- `Assets/Scripts/BacardiMasterComparison.cs.meta`
- `BEAT-MAKER.md`
- `Tools/LufsValidation/MasterCheck.cs`
- `Tools/LufsValidation/Run-MasterValidation.ps1`
- `Tools/LufsValidation/README.md`
- `Tools/LufsValidation/Listening-Safety-Report.md`

Added or refreshed validation artifacts: Target, Bypass, Level Matched, Looped, Fixed Gain Loop, HF, Noise and Impulse master WAVs, and corresponding `*-measurement.txt` files. Temporary response files, compiled DLLs and the copied runtime configuration are recreated by the runner and removed after validation. The synthesis, sequencer, tempo, swing, pattern save/load, generation and MIDI logic were not changed. EXPORT ALL STEMS continues using shared stem gain; bypass affects the stereo Full Mix only.
