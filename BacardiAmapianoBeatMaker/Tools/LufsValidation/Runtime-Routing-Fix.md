# Runtime mastering routing investigation — 2026-10-04

The discrepancy was upstream of mastering: the analyzed renderer copies each mono sample at full amplitude into both stereo channels, but Unity's centered mono AudioSource applies constant-power panning. Its per-channel amplitude is approximately 0.707107 of that analyzed input. This lowers the limiter input by about 3.0103 dB. A correctly enabled limiter can then show zero reduction because its input never reaches the ceiling.

Unity documents this mono panning behavior: https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/audiosource/panstereo

## Actual Unity baseline

The isolated test used copies of the existing scene, Bacardi scripts and project audio settings, in a real Unity 6000.3.24f1 editor Play Mode session. The scene has no imported sample assignments and generates the default demo sounds. The test called the actual AnalyzeMaster, Stop and StartPlayback methods, then captured the full 16 bars from OnAudioFilterRead. No offline PCM was injected into playback. Unity used stereo output at 48,000 Hz, with listener volume 1, one AudioListener and one meter. Analysis uses 44,100 Hz.

The baseline measured -17.8 LUFS-I versus -14.9 analyzed (-2.9 LU), -1.7 dBTP, and 0.0 dB maximum reduction. Analyzed and live gain both measured -0.264 dB / 0.970055 linear. Mastering was active, bypass mix was 0.000, and analysis and live processing referenced the same meter instance (-2122). The post-gain limiter input peak was 0.825161. See Unity-Routing-Before.txt.

This rules out the gain handoff, unintended bypass and duplicate meter hypotheses for this scene. BacardiMastering is a static analysis utility, not a Unity component with an instance ID. Its fresh offline limiter and the live comparison's limiter deliberately have separate DSP histories. Analysis publishes its selected gain to the same listener meter referenced by the UI. Telemetry includes the meter's Unity instance IDs, UI owner ID, and managed identity hash of the live comparison processor; a managed identity hash is not a Unity instance ID.

## Change

BacardiBeatMaker now creates an in-memory stereo playback clip for each mono clip, copying every mono sample into L and R exactly as RenderSound does. Existing stereo clips pass through directly. Runtime triggering uses these playback clips, while analysis and exports still use the original clips and PCM. Playback clips are released on destruction.

This corrects channel mapping before the existing mastering chain. It does not add compensating master gain, change the target or 3 dB budget, alter pattern/velocity/mix controls, or modify exports. The gain calculation, limiter envelope, ceiling, reconstruction kernel and release are unchanged. There is no extra wet-branch safety attenuation: the existing true-peak limiter provides wet safety; the dry branch retains its original safety gain and limiter. Both branches remain warm and retain the original crossfade.

Read-only limiter telemetry adds maximum post-gain reconstructed input peak and maximum output sample peak. The separate existing listener detector still reports final true peak. The UI has an optional Runtime mastering telemetry toggle, and completed capture reports include gain, branch state, peaks, GR and object identities. There are no logs or Unity object calls added to the audio callback.

## Verification

The first corrected actual Unity run measured -15.0 LUFS-I versus -14.9 analyzed, a rounded -0.2 LU difference, -1.3 dBTP and 2.641 dB maximum reduction. Its limiter input peak was 1.166953, approximately sqrt(2) times the baseline peak. The analyzed gain remained -0.264072 dB, and bypass remained zero. See Unity-Routing-After-Rounded.txt.

The remaining small differences can include native source resampling/pitch interpolation at 48 kHz and the different capture/reference end windows. The live test ends after exactly 16 bars; analysis's repeated reference includes the last sample tails. These differences were not hidden by changing gains, measurement windows or limiter tuning.

Managed runtime compilation and deterministic DSP/FFmpeg validation passed. Original results remain: analyzed loop -14.860887 LUFS-I, -1.300000 dBTP, gain -0.264072 dB, maximum reduction 2.980000 dB. Streaming comparison still matches offline PCM; quiet guards and bypass crossfade checks pass.

The final full-precision actual Unity capture passed: live -15.033346 LUFS-I, analyzed -14.860887 LUFS-I, difference -0.172458 LU, true peak -1.300000 dBTP and maximum GR 2.641070 dB. Analysis and live meter IDs both were -1688; mastering was active and bypass mix was 0.000. See Unity-Routing-After.txt. This meets the 0.2 LU acceptance tolerance. Runtime maximum GR is 0.338930 dB below the analyzed 2.980000 dB maximum.

The reusable editor test is Bacardi > Validate Runtime Master Routing (16 bars), from Edit Mode with the Bacardi scene open. It checks 0.2 LU loudness agreement, 0.2 dB true-peak agreement and a safe peak below -1 dBTP, maximum GR within 0.5 dB of the analyzed loop maximum, and an active mastered branch with zero bypass mix. The 0.5 dB GR comparison is an explicit interpretation of “near”; it is not a change to the 3 dB safety budget.

Scope: actual native AudioSource/listener playback of the default scene was tested. Imported custom samples, other output devices and generated songs require their own fresh analysis/run. This capture measures the listener PCM, not OS/device processing after Unity.
