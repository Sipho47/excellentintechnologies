# Apply and export song variation

Runtime path: SONG VARIATION → Generate or Load Variation → variation preview → APPLY VARIATION TO SONG → verify ARRANGEMENT APPLIED: YES and arrangement length → PREVIEW ARRANGED SONG → export arranged stems, MIDI, or stereo WAV.

Apply snapshots the current plan. Planning alone does not activate arrangement playback. One-bar Groove stops playback and retains the planned variation for reapplying. Generating or loading a different plan resets Apply.

Playback and export share SongSourceStep and RenderSound. Plan masks determine entrances, exits, density, pauses and transition hits. Fully inactive bars are silent, including preceding tails. Files end at the planned bar length. The existing groove notes, velocities, BPM, swing and mix gain feed rendering directly.

Arranged stems: 25 stereo float32 WAVs, aligned at Bar 1 and equal in length, including inactive silence. Arranged MIDI: 25 named tracks plus tempo and section markers in Arranged Song.mid; melodic pitches/triads and swung timing match audio. Percussion uses channel 10 and requires instrument/sample assignment.

Arranged stereo: Arranged Stereo.wav, stereo float32 at 44100 Hz. Float headroom preserves the current mix without normalization, rebalancing, limiting or Spotify mastering. Open POST-ARRANGEMENT MASTER TEST, choose IMPORT STEREO WAV and select this file. Mastering remains a separate action.

Tests: Run-UnityVariationValidation.ps1 validates Apply before export, a 56-bar plan, silent inactive bars, planned permissions, MIDI timing, aligned WAV lengths, exact stereo samples, post-arrangement import, source WAV SHA256 hashes, preview and one-bar mode. Run-UnityVariationUiValidation.ps1 checks visible controls and their Apply gating at three window sizes. Both run locally without provider calls.
