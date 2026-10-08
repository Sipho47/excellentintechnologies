# Bacardi Amapiano Beat Maker / Drum Pad

Unity desktop beat maker with 25 procedural instruments, a 16-step sequencer, full-song arrangement and variation controls, vocal workflow, WAV/stem/MIDI exports, and a post-arrangement stereo mastering panel.

## Open the app

1. Install Unity **6000.3.24f1** through Unity Hub.
2. In Unity Hub, add this `BacardiAmapianoBeatMaker` folder as a project.
3. Open `Assets/Scenes/BacardiBeatMaker.unity` and enter Play Mode.
4. Use a Game view of 1280 x 800 or larger for comfortable controls.

The 25 default sounds are generated locally. Large recordings, unrelated video/visualizer scenes, generated exports, Unity caches, binaries, and credentials are excluded.

See [BEAT-MAKER.md](BEAT-MAKER.md) for controls and [AI-VOCALS.md](AI-VOCALS.md) for the vocal workflow. Mock vocals work locally. Real vocals require a separately configured local proxy; see [the proxy guide](Tools/VocalProxy/README.md). Real-provider generation is optional and can incur API charges.

## Runtime layout verification

The post-arrangement master panel measures wrapped result rows inside its scroll content and reveals the final status when processing completes. It includes SOURCE / BEFORE, MASTER / AFTER, GAIN APPLIED, LIMITER REDUCTION, TARGET STATUS, and the export button.

`Assets/Editor/BacardiStereoMasterUiTest.cs` runs Play Mode checks at 1920x1080, 960x540, 640x480, and 480x320 using a generated local fixture, including long failure text. Run in a separate checkout/project because the command exits Unity when finished:

```powershell
& 'C:\Program Files\Unity 6000.3.24f1\Editor\Unity.exe' -projectPath (Get-Location).Path -executeMethod BacardiStereoMasterUiTest.RunCommandLine -logFile runtime-ui.log
```

The [saved test report](Tools/StereoUiValidation/runtime-ui-results.txt) records all four sizes passing. The tests do not export audio or call paid APIs. Additional offline validation sources and scripts are under `Tools`.

## Vocal workflow updates

Vocal results retain their provider identity. Switching providers or starting, failing, or cancelling generation invalidates stale results; preview requires valid audio from the selected provider. Saved isolated vocal WAVs can be reloaded without provider calls, using validated source metadata rather than filenames to establish provenance.

Arranged preview mixes current vocal placements into a fresh instrumental render, preserves bar timing, and bypasses listener mastering. `EXPORT ARRANGED STEREO + VOCALS WAV` exports the combined mix with placement/source metadata and a single safety attenuation if needed. The original `EXPORT ARRANGED STEREO WAV` remains instrumental-only.

Song bars follows the applied arrangement, or the pending variation when none is applied, and is read-only while synchronized. Vocal duration remains independent. Invalid placements after shortening are reported and blocked without automatic edits. Returning to one-bar mode restores editable Song bars.

Latest offline Play Mode results:

| Suite | Passing checks | Report |
| --- | ---: | --- |
| Song bars synchronization | 11 | [Results](Tools/VocalSongLengthValidation/runtime-results.txt) |
| Saved vocal reload | 23 | [Results](Tools/SavedVocalValidation/runtime-results.txt) |
| Arranged vocal preview | 13 | [Results](Tools/ArrangedVocalPreviewValidation/runtime-results.txt) |
| Combined vocal export | 15 | [Results](Tools/CombinedVocalMixValidation/runtime-results.txt) |

Test entry points in `Assets/Editor` are `BacardiVocalSongLengthRuntimeTest`, `BacardiSavedVocalRuntimeTest`, `BacardiArrangedVocalPreviewRuntimeTest`, and `BacardiArrangedVocalMixRuntimeTest`, each exposing `RunCommandLine`. Run one suite at a time in an isolated project using the command pattern above. These suites use offline fixtures; the saved-vocal suite optionally validates the existing local PART 8 files read-only. No provider calls were made, PART 8 was not exported/mastered, and PART 9 was not started.
