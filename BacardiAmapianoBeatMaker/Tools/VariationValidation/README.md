# Song variation validation

Run from the project directory in PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VariationValidation/Run-VariationValidation.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VariationValidation/Run-UnityVariationValidation.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/LufsValidation/Run-StereoValidation.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VocalValidation/Run-VocalValidation.ps1
```

These tests use local synthesis and fixtures. None calls ElevenLabs or a paid API. The isolated Unity project needs access to the installed Unity licensing client. It does not change the open project or scene. Its export fixtures live under the existing Unity persistent-data export directory. SAVE/LOAD testing restores the previous saved pattern.

The planner suite covers 40 seeds × seven personalities × three amounts: exact replay, replay of an automatically accepted replacement seed without generation history, similarity thresholds, contiguous sections, omission of instruments, delayed logdrum/bass entrances, STRONG structural changes, and SHA-256 equality of every source WAV under Assets.

The Unity suite checks actual runtime compilation, tempo/groove preservation, seed replay, SAVE/LOAD, finite-song PCM with tails, silent omitted stems, all 25 aligned stem files, summation against the unmastered mix, MIDI event counts and duration, exported plan metadata, transport, legacy one-bar rendering, and retained vocal/post-arrangement components. Stereo and vocal suites exercise the existing master and vocal algorithms separately.

Results are written to `results.txt`, `unity-results.txt`, and `unity.log` here. The source WAV hash check concerns file immutability; numerical tests cannot establish perceived musical quality, which still needs listening.
