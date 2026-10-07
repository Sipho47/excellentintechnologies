# Deterministic mastering / transient-protection report

Current implementation, 2026-10-04. This report supersedes the previous -14.1 LUFS / +3.1 dB / 5.6 dB limiting result as the default behavior.

## Cause and fix

The previous gain was chosen from a finite one-bar export including decay. Repeating the pattern overlaps long samples, changing the actual PCM and increasing integrated loudness and peak level. The earlier -14.1 to -12.3 LUFS difference compared two distinct programs, not two identical fresh runs. We did not find evidence of stale meter values feeding the gain calculation. However, the old gain policy did not account for those overlaps and allowed excessive limiting.

Analysis now builds a complete deterministic render of the current one-bar pattern plus full tails and a 16-repeat playback reference. Long custom tails extend the reference so all overlapping tails are represented. It chooses one fixed gain using both programs' measured loudness and reconstructed peak, with a maximum verified limiter reduction of 3 dB and a 0.02 dB numerical margin. Rendered output is checked for actual maximum reduction, and gain is reduced if the budget is exceeded. The ceiling remains -1.0 dBTP or below, using the existing -1.3 dB internal margin. The algorithm does not compress individual channels, alter notes or velocity, or normalize stems.

Fresh analysis creates new integrated meters, peak reconstruction histories, limiter envelopes/lookahead buffers and reduction statistics. Fresh playback creates a new audio session initialized to the selected fixed gain, so it does not inherit old limiter state or ramp up from an unintended gain. Analysis stops active voices and starts the pattern from the beginning afterward if it was playing. Normal bypass toggles keep their histories warm and use the existing 50 ms crossfade; they are distinct from fresh starts. Gain never derives from a rolling live meter.

## Three separate fresh runs

The complete demo uses all 25 sounds. Each run analyzes and renders both the full finite demo and its repeated-playback reference with newly created DSP/measurement state. Outputs across all three runs are bit-identical, as are the numerical gain and reduction results.

| Run | Program | LUFS-I | Maximum TP | Applied gain | Maximum limiter reduction |
|---|---|---:|---:|---:|---:|
| 1 | Finite demo + all tails | -16.744 | -1.300 dBTP | -0.264 dB | 2.227 dB |
| 2 | Finite demo + all tails | -16.744 | -1.300 dBTP | -0.264 dB | 2.227 dB |
| 3 | Finite demo + all tails | -16.744 | -1.300 dBTP | -0.264 dB | 2.227 dB |
| 1 | 16-bar overlapping playback | -14.861 | -1.300 dBTP | -0.264 dB | 2.980 dB |
| 2 | 16-bar overlapping playback | -14.861 | -1.300 dBTP | -0.264 dB | 2.980 dB |
| 3 | 16-bar overlapping playback | -14.861 | -1.300 dBTP | -0.264 dB | 2.980 dB |

Independent FFmpeg 9.0.2 EBU R128 / true-peak measurements of all six completed 24-bit WAVs agree at their displayed precision: finite -16.7 LUFS-I / -1.3 dBTP and looping -14.9 LUFS-I / -1.3 dBTP. See Deterministic-runs.csv and each corresponding *-measurement.txt.

Three freshly initialized live comparison DSP sessions also reproduce the finite offline samples within 1e-6. This is a streaming DSP test with 1024-frame buffers, not a run of Unity's native AudioSource scheduler. New-session output starts at the analyzed gain. The filter-history optimization (duplicated ring storage instead of per-tap modulo indexing) retains the original limiter's numerical behavior.

Different-duration/content programs retain different legitimate LUFS-I measurements. Finite tails are proportionally quieter than overlapping playback. The same duration/input/settings is deterministic; an integrated meter should not be frozen to conceal startup/decay or the true signal. Live integration may converge as the loop continues, but it does not change the applied master gain. Re-analyze after edits.

## Safer versus forced

| Finite demo version | LUFS-I | TP | Gain | Maximum reduction |
|---|---:|---:|---:|---:|
| Selected transient-safe version | -16.744 | -1.300 dBTP | -0.264 dB | 2.227 dB |
| Forced target fixture | -14.067 | -1.299 dBTP | +3.137 dB | 5.628 dB |

The forced file is a validation/listening fixture only; no forced mastering option is exposed in the application. The normal system chooses the safer result and displays `TARGET: -14 LUFS | RESULT: -16.7 LUFS-I | ... | TP LIMITED: transient protection`, with predicted loop loudness and reduction alongside it. Stems, mix balance and arrangement are unchanged. No target is forced at the expense of the reduction budget.

Listening files: Safe Run 1 Master.wav, Loop Run 1 Master.wav and Forced Master.wav. These differ in loudness; level-match before judging musical quality. Numerical reduction and continuity checks are not proof of subjective transparency. Unity device playback, subjective listening, Game-view click-through and a standalone build were not exercised.

## Other checks and reproduction

All runtime scripts compile against the installed Unity assemblies. Quiet/silence rejection, long-tail reference sizing, fixed-gain streaming equivalence, stereo-balance preservation and repeated bypass/enable continuity pass. Largest switching-ramp sample increment on constant stereo input: 0.00006351. Export uses the same loop-aware gain policy as analysis. Fresh export renders have their own state. Existing MIDI, sequencing, save/load, generation and stem-level code remains unchanged.

Re-run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/LufsValidation/Run-MasterValidation.ps1
```

Changed source/documentation:
- Assets/Scripts/BacardiMastering.cs
- Assets/Scripts/BacardiMasterComparison.cs
- Assets/Scripts/BacardiMasterLufsMeter.cs
- Assets/Scripts/BacardiBeatMaker.cs
- BEAT-MAKER.md
- Tools/LufsValidation/MasterCheck.cs
- Tools/LufsValidation/Run-MasterValidation.ps1
- Tools/LufsValidation/README.md
- Tools/LufsValidation/Deterministic-Mastering-Report.md

Added validation artifacts: Deterministic-runs.csv, Forced-result.txt, Safe Run 1/2/3 Master.wav, Loop Run 1/2/3 Master.wav, Forced Master.wav and corresponding *-measurement.txt files. The runner's temporary response files, compiled DLLs and runtime configuration are recreated as needed and removed after validation. Older fixtures/reports are retained as historical artifacts.
