# Post-arrangement stereo master workflow

Open POST-ARRANGEMENT MASTER TEST above the existing stem controls. Import the finished FL Studio stereo WAV, ANALYZE FULL SONG, enable TARGET -14 LUFS, set the ceiling (default -1.0 dBTP), and SET SPOTIFY MASTER. Compare with bypass, then EXPORT SPOTIFY MASTER WAV to a new filename. Export always writes the prepared master, including when auditioning bypass. Both before and after measurements remain visible.

## Separation and source protection

The stereo session owns private original and processed PCM arrays, its own AudioSource and AudioClip, and independent measurements. Entering this view pauses beat/vocal playback and stops scheduled stem voices. It suspends beat scheduling and the stem-analysis shortcut. The listener's stem comparison processor is bypassed only while this view is open; its target, gain, bypass, and stem state are retained. Returning stops stereo playback and restores normal stem routing. No vocal generation, arrangement metadata, stem export, or provider code was changed. No APIs are called by this workflow.

Import uses read-only file access. Processing copies original PCM and never changes that array. Playback receives a copy. Export rejects the original path, then uses FileMode.CreateNew: all existing destinations, including source hard-link aliases, are refused. The output is stereo 24-bit PCM at the source sample rate and with the original frame count. Import supports RIFF stereo PCM16/24/32 and float32, including standard extensible subformats, at 8–192 kHz. Unsupported/mono/malformed or non-finite input is rejected. RF64 and packed valid-bit containers are unsupported. Long songs are loaded into memory; analysis/mastering run on a worker to keep the UI responsive.

## Measurement and processing

The existing BacardiLufsEngine performs sample-rate adapted K-weighting, stereo energy summation, 400 ms blocks at 100 ms intervals, -70 LUFS absolute gating and a relative gate 10 LU below the absolute-gated mean. Short-term is the ungated last 3 seconds; the panel also reports maximum observed short-term. No valid loudness window produces NaN (silence/short files). These are offline file measurements, not a live playback short-term display.

True peak reuses the 8x, 32-tap windowed-sinc reconstruction detector and flushes its tail. Sample peak is the maximum absolute sample. Measurements are implementation estimates, not certified meters. The stereo-linked lookahead limiter receives a configurable ceiling with 0.3 dB margin, remeasures its completed output, and trims if needed. Gain is iterated toward -14 LUFS, capped at +18 dB and constrained by a 3 dB input-peak limiting budget. Peak protection may leave high-crest-factor material below target; the UI reports that. Source analysis always precedes processing. Existing stem limiter calls retain their original -1.3 dB internal threshold via the new optional parameter.

## Changed files

- Assets/Scripts/BacardiStereoMaster.cs and .meta: independent import, analysis, processing, protected export.
- Assets/Scripts/BacardiStereoMasterPanel.cs and .meta: dedicated UI, asynchronous work, playback, Editor/Windows native file dialogs; text paths on other platforms.
- Assets/Scripts/BacardiBeatMaker.cs: visible workflow entry, view routing, stem scheduling suspension.
- Assets/Scripts/BacardiMasterLufsMeter.cs: stereo-mode listener processing bypass.
- Assets/Scripts/BacardiMastering.cs: optional limiter threshold; original default unchanged.
- Tools/LufsValidation/StereoCheck.cs and Run-StereoValidation.ps1: executable DSP/file regression checks.
- This report and Stereo-Independent-Measurement.txt: validation record.

## Validation

Runtime sources compile against installed Unity 6000.3.24f1 assemblies for Windows standalone and Editor defines. Only the pre-existing ElevenLabs Job.httpStatus CS0649 warning was reported. The harness checks stereo import, bypass, full-song integrated/short-term/peak analysis, gain targeting, default and configurable transient limiter ceilings, export dimensions and bit depth, source/existing-file overwrite rejection, byte-for-byte unchanged input, and silence. Twelve checks passed. Independent FFmpeg ebur128=peak=true measured the exported sine fixture at -14.0 LUFS-I and -14.0 dBTP.

Run: powershell -NoProfile -ExecutionPolicy Bypass -File Tools/LufsValidation/Run-StereoValidation.ps1

Independent FFmpeg analysis of the transient fixture mastered with a -3.0 dBTP ceiling measured -3.3 dBTP and -16.1 LUFS-I, confirming peak protection takes priority over reaching target. See Stereo-Limited-Measurement.txt.

The runner generates compiler response files, DLLs and uniquely named WAV fixtures under Tools/LufsValidation. Live Unity Game-view interactions, device playback, native dialog interactions and a standalone build have not been exercised. Numerical compilation/DSP checks do not validate those surfaces.
