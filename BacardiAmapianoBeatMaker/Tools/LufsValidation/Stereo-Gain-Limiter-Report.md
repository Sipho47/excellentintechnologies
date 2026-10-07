# Stereo gain and limiter regression

Only BacardiStereoMaster.cs and its runtime panel changed. Stem rendering, arrangement, vocals and the shared limiter implementation remain unchanged.

SetSpotify now calculates -14 minus measured input LUFS, applies that gain before the existing true-peak limiter, measures the complete processed song and corrects gain over up to 12 fresh renders. Each pass starts from original PCM. The limiter runs 0.1 dB below the configured ceiling; reconstructed-output verification corrects remaining overshoot. Success requires loudness within 0.15 LU and verified true peak within the configured ceiling.

Peak-based input gain capping and the former 3 dB early stop were removed from this stereo path. A 9 dB maximum reduction threshold explicitly reports excessive limiting; an 18 dB loudness-gain bound and iteration exhaustion also report failure explicitly. The runtime panel displays measured limiter peak reduction and the resulting status.

Offline fixture: 12-second stereo, 48 kHz, low-level tone with sparse high transient peaks. Input: -18.80 LUFS / -1.15 dBTP. Processed: -14.14 LUFS / -1.10 dBTP, +4.80 dB pre-limiter gain, 4.75 dB actual peak limiter reduction. Exported 24-bit WAV independently measured by FFmpeg ebur128: -14.1 LUFS / -1.1 dBTP. Regression source SHA256 remained unchanged. All 16 stereo checks passed and runtime scripts compiled.

Run: powershell -ExecutionPolicy Bypass -File Tools/LufsValidation/Run-StereoValidation.ps1. This also independently checks exported fixture loudness and true peak with local FFmpeg.

The user's Arranged Stereo.wav was not processed or exported during this fix. Reimport that same file and run SET SPOTIFY MASTER again with -1.0 dBTP ceiling before deciding to export.
