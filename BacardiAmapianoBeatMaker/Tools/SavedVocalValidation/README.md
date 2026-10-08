# Saved vocal reload

In Play Mode, use **LOAD SAVED VOCAL WAV** in the AI vocal panel to select an isolated saved take. Loading does not invoke vocal generation or contact the proxy. A validated result enables preview, add-to-song and the existing save/stem export controls. Song length and existing placements are not restored or changed automatically.

Supported audio: standard 44.1 kHz, stereo, 24-bit PCM WAV. The project tempo must match the saved take. Audio duration must match metadata BPM and bar count within one frame. No pitch shift, resampling, normalization or mastering is performed.

Future isolated vocal saves include `<filename>.wav.vocal.json`: versioned request/source metadata, frame dimensions, filename and SHA-256 of the saved WAV. A mismatching or malformed sidecar is rejected without falling back to legacy metadata.

For older saves, keep the adjacent `VOCAL_INFO.txt`. The loader requires one unambiguous isolated take, a known provider record, consistent source description, expected saved filename and matching format/duration. Filename alone never establishes REAL_AI. Unknown or conflicting provenance, aligned stems, and ambiguous multi-take folders are rejected. Saved LOCAL_MOCK audio retains its mock identity.

Run the Play Mode suite in the isolated test project, because it exits Unity when finished:

```powershell
& 'C:\Program Files\Unity 6000.3.24f1\Editor\Unity.exe' -batchmode -projectPath "$PWD\Tools\UnityStereoUiProject" -executeMethod BacardiSavedVocalRuntimeTest.RunCommandLine -logFile "$PWD\Tools\SavedVocal-Runtime.log"
```

Copy current runtime sources and the test script into that project's `Assets/Scripts` and `Assets/Editor` before running. [Results](runtime-results.txt) cover legacy/sidecar reload, settings, actual preview PCM, placement, synthetic aligned-stem export, source labels, invalid input rejection and zero provider/proxy calls. Existing PART 8 audio is validated read-only when available at the known saved location; it is never exported or mastered by the suite.
