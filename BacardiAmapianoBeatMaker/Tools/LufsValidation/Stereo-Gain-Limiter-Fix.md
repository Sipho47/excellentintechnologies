# Stereo gain and limiter correction — 2026-10-06

The post-arrangement stereo path now calculates loudness gain first and sends it through the existing stereo-linked true-peak limiter. The old peak-derived gain cap and 3 dB stopping rule are removed from this path. Each pass starts from the original PCM and re-analyzes the complete processed file. It aims within 0.15 LU of -14 LUFS and verifies the configured reconstructed peak ceiling with a small internal margin and output safety trim.

The panel reports actual maximum limiter gain reduction and an explicit failure status for excessive limiting (over 9 dB), the +18 dB gain limit, or failure to converge in 12 passes. These are stereo-only policies. Imported WAV bytes, source PCM, stems, arrangement mixing, variation and vocal generation remain untouched.

Offline regression source: -18.80 LUFS-I / -1.15 dBTP. Processed result: -14.14 LUFS-I / -1.10 dBTP, +4.80 dB input gain, 4.75 dB maximum limiter reduction. Independent FFmpeg `ebur128=peak=true` on the 24-bit exported fixture: **-14.1 LUFS-I / -1.1 dBTP**. Source SHA256 preservation passes. Runtime compilation passes with the existing Job.httpStatus warning.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/LufsValidation/Run-StereoValidation.ps1`. Independent results are saved in `Stereo-High-Crest-Independent.txt`. The generated test WAV is in `StereoFixtures/Low-LUFS High-Peak Master.wav`.

This verifies the offline regression export, not the user's actual Arranged Stereo.wav or a running standalone build. Reload the updated application and run SET SPOTIFY MASTER on that same source to check its displayed results before export. No paid services were called.
