# AI vocal generator

Open Assets/Scenes/BacardiBeatMaker.unity, enter Play mode, and scroll beneath the existing beat/export controls to AI VOCAL GENERATOR.

MOCK remains the default provider. It generates a synthetic vowel melody to validate workflow and timing, not intelligible sung lyrics. REAL AI uses ElevenLabs Music v2.5 through a local credential-holding backend; configuration, costs and real-audio limitations are described in [the proxy guide](Tools/VocalProxy/README.md).

Use Female / Lead / C Major / four bars at the current beat tempo (104 BPM initially), enter the default hook, and GENERATE VOCAL. PREVIEW / STOP PREVIEW audition the current take. Add it with HOOK 1: 13 > ADD TO SONG and HOOK 2: 17 > ADD TO SONG. Four-bar intervals are [13,17) and [17,21). Generated takes remain separate from 06 Vocal Chop.

The musical tracker uses AudioSettings.dspTime and 4/4 time, independently of the unchanged one-bar sequencer. PLAY starts/resumes it alongside the beat; PAUSE preserves vocal position; STOP resets it to Bar 1. At 104 BPM, Bar 13 begins 27.692307692 seconds after transport start (frame 1,221,231); Bar 17 begins 36.923076923 seconds after start (frame 1,628,308). Native PlayScheduled uses those musical positions rather than a delayed coroutine.

Tempo changes never pitch-shift vocals. Native pitch remains 1.0. Vocals generated at a different tempo stop/wait at the next unscheduled beat boundary; REGENERATE explicitly produces a new take at the current BPM and updates placements referencing the previous current take without moving their bars. Other takes are retained. No automatic paid regeneration occurs. Preview/aligned export reject BPM mismatches.

SAVE VOCAL WAV writes the isolated current take. EXPORT VOCAL STEM writes all committed placements aligned from Bar 1, with leading/trailing silence; when there are no placements, it uses the current take/start-bar field. Files are 44.1 kHz, 24-bit stereo WAV under Unity persistentDataPath/Exports/Bacardi-<timestamp-id>/AI Vocals/, with VOCAL_INFO.txt. Song length defaults to 32 bars and extends to fit complete placements.

No vocal LUFS normalization occurs. Vocal peak and vocal LUFS-I are displayed separately from the existing full-master LUFS-I/true-peak readouts. Preview and song vocal sources pass through the existing listener mastering processor. Optional light preview reverb affects only the preview source.

The original beat arrangement data, all 25 instrument slots/samples, velocities, original mix controls, mastering/limiter algorithms, master target, instrument stem exporters and MIDI exporter are preserved. Existing one-bar Full Mix/instrument/MIDI exports do not become a full-song vocal master exporter. Vocal placements are session-local; save audio/metadata before leaving Play mode.

Validation: Tools/VocalValidation/Run-VocalValidation.ps1 -Runtime runs managed/native mock and timing acceptance, unavailable REAL-provider handling, bar placement, export and regeneration checks. Tools/VocalProxy/Start-VocalProxy.ps1 -SelfTest runs offline API-contract/audio conversion fixtures. The editor's Bacardi > Validate REAL Sung Vocal (uses API) is an explicitly invoked live technical acceptance test and incurs one composition plus one separation request. It rejects mocks and tests a Female/Lead/C Major/104 BPM/four-bar request through both complete hooks. Audition is still needed to confirm actual lyrics and notes.

See [Real-Provider-Report.md](Tools/VocalValidation/Real-Provider-Report.md) for the current integration, file changes and validation status. The earlier Implementation-Report.md documents the original mock milestone.
