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
