# Bacardi Amapiano Beat Maker

## Song variation system (current)

In Play mode, the **SONG VARIATION** section sits above the sound pads. Choose one of seven personalities (DEEP / MINIMAL, VOCAL AMAPIANO, LOGDRUM HEAVY, SOULFUL, PRIVATE SCHOOL, CLUB / ENERGY, AFRO-HOUSE FUSION), choose SUBTLE, MEDIUM or STRONG, and press **GENERATE NEW SONG VARIATION**. This creates a complete Intro / Build / Drop 1 / Breakdown / Drop 2 / Outro plan over the current groove. STRONG varies section lengths, layer choices, entrances, rhythmic density and pauses. Playback, WAVs and melodic MIDI follow the same plan. The preview lists bar ranges, leads, energy, percussion and melodic density, logdrum/bass entrances and selected layers. SONG BAR shows the current position.

The planner keeps the current BPM (104 by default), swing, source clips, note pitches, velocities and relative stem levels. It selects and schedules existing parts; it does not invent new melodies when the groove has no notes for a selected instrument. Use the groove controls to create/edit the underlying musical material. **ONE-BAR GROOVE** returns to the existing looping sequencer. The older **GENERATE NEW SONG** button generates a new one-bar groove and clears the full-song plan; it now preserves the user's current tempo.

**NEW SEED** generates another plan. Repeated GENERATE clicks choose a new seed. **REUSE CURRENT SEED** explicitly replays the displayed seed with the chosen personality/amount. A stable versioned integer PRNG reproduces the plan; matching audio also requires the same groove and samples. **SAVE PATTERN / LOAD PATTERN** saves/restores the groove, tempo and full accepted plan together. Each song export includes `SONG_VARIATION.json` and a readable `SONG_VARIATION.txt`. If similarity rejection replaces an entered seed, the accepted reproducible seed is shown in the seed field.

New plans are compared against the current plan using section stem overlap, section starts/lengths, percussion density, melodic density/lead, logdrum/bass entrances and rhythmic permission masks. Difference is LOW below 0.20, MEDIUM from 0.20 to below 0.38, and HIGH at 0.38 or above. SUBTLE requires at least 0.08 difference, MEDIUM at least 0.20, and STRONG at least 0.38 plus changed section boundaries. The planner tries up to 1,024 deterministic replacement seeds before retaining the current song with an explanatory status. Explicit seed replay and loading a saved variation intentionally allow LOW difference.

Full-song stem exports still produce 25 equal-length stereo WAVs at the same start, plus Full Mix and melodic MIDI. Unselected stems remain silent; full-song export never adds quiet accents or changes individual stem gains. Existing unity-level reconstruction and common PCM headroom trim remain in use. The legacy one-bar export retains its previous accent-filling behavior. Import the stems together at the reported BPM in FL Studio and avoid playing Full Mix alongside them. Sample tails remain intact across section boundaries; dropouts stop new notes rather than cutting sustained audio.

The mastering algorithms, REAL AI vocal generator, optional reference-stem normalization and POST-ARRANGEMENT MASTER TEST implementation are unchanged. Full songs supply their finite PCM, including tails, to the existing mastering engine; legacy one-bar grooves retain their overlapping-loop reference. The legacy 16-bar loop diagnostic runs only in one-bar mode. Re-analyze after changing the arrangement. Vocals remain independently placed through the existing vocal timeline and export controls; the variation system schedules the Vocal Chop instrument, not generated REAL AI vocals. No external AI or paid API is involved.

Offline test instructions and results: [Tools/VariationValidation/README.md](Tools/VariationValidation/README.md).

In Unity, choose **Bacardi > Create Beat Maker Scene**, then press **Play**. The menu creates `Assets/Scenes/BacardiBeatMaker.unity`. Set the Game view to 1280 Ãƒâ€” 800 or larger for comfortable controls.

The Game view also shows the full interface when Unity Play mode is off. This is a preview with disabled controls; press Unity's Play button to enable sound and pattern editing. Edit mode does not create audio sources or generated audio clips.

All 25 requested instruments are included. Tap a pad to audition and select it, then toggle its 16 sequencer steps. PLAY loops the pattern; STOP resets playback. Tempo spans 80Ã¢â‚¬â€œ150 BPM, with adjustable swing and master volume. DEMO GROOVE loads a starting rhythm using all 25 sounds. SAVE PATTERN and LOAD PATTERN keep one local pattern between sessions. EXPORT WAV renders a stereo mix with full sample tails. EXPORT ALL STEMS uses all 25 instruments and creates 25 stereo stems, plus the full mix and melodic MIDI. Any unused pads receive quiet sequencer accents; existing notes are preserved. EXPORT MELODIC MIDI exports the eight melodic sound slots. Export folders are created inside Unity's persistent data directory under Exports; the full path appears in the interface and Console.

Sounds are synthesized approximations, including a synthetic vocal effect rather than a recorded voice. For production samples, assign AudioClips to the beat maker's Samples array in the Inspector, in the same order as the pads. Leave slots empty to retain generated sounds. Custom clips must permit GetData (use Decompress On Load) for WAV export.

The existing visualizer scenes are preserved. To include the beat maker in a build, add its new scene to the active Build Profile scene list. The interface uses Unity IMGUI and supports pointer/touch pad interaction.

## Unity to FL Studio

1. Program the 16-step groove and set BPM, swing and master volume.
2. Choose **EXPORT ALL STEMS**. Each export gets a unique folder containing numbered instrument WAVs, `Full Mix.wav`, `Melodic.mid` and `IMPORT.txt`.
3. Set FL Studio to the exported BPM. Import the instrument WAVs onto separate Playlist tracks, all starting at the same song position, and route them to separate Mixer inserts.
4. Use `Full Mix.wav` as a reference and mute it while playing stems. WAV files are 44.1 kHz, 24-bit stereo, share the same start and length, and include full sample tails. Loop the first four beats rather than the full file. EXPORT ALL STEMS fills unused sound slots with quiet hits, including an empty pattern. Tapping a pad previews its sound. Zero master volume and silent samples produce an error instead of silent WAVs.
5. Import `Melodic.mid` and assign instruments to its named Piano, Rhodes, Pad, Pluck, Strings, Vocal Chop, Logdrum and Sub Bass tracks. MIDI preserves BPM, 4/4 and swing timing; it does not contain instrument sounds. Avoid playing the same musical part from both its WAV and MIDI unless you intend to layer it.

EXPORT MIX-BALANCED STEMS is the primary arrangement export. All 25 stems retain original relative levels, timing and stereo mapping without individual normalization or limiting; unused pads receive quiet accents so all 25 stems contain sound; existing notes, pitches and velocities are preserved. Gain is unity unless a stem exceeds PCM headroom, in which case one shared trim is recorded in MIX_BALANCED.txt. Summing at unity reconstructs the unmastered mix with that common gain, within 24-bit rounding error. EXPORT -14 LUFS STEMS is an optional reference mode with clearly labeled filenames and STEM_LOUDNESS.txt; independent gains change balance and peak/transient protection may prevent reaching target. Full Mix mastering is unchanged.

## Optional master loudness target

Use **TARGET -14 LUFS** beside MASTER / LUFS to analyze and enable a fixed stereo master gain. **Analyze / Set -14 LUFS** measures the whole current one-bar arrangement including sample tails and recalculates that gain. Re-analyze after changing notes, velocity, tempo, swing, samples or MASTER. Playback never chases its rolling meter; exports always analyze the latest arrangement.

The listener processes final mixed audio through master gain and a stereo-linked 8x reconstructed-peak lookahead limiter, then measures LUFS-S, gated LUFS-I and true peak on the resulting output. Limiting has a 120 ms release and approximately 0.7 ms latency at 44.1 kHz. The -1.0 dBTP ceiling includes a 0.3 dB reconstruction margin. Master gain is capped at +18 dB; silence and arrangements below -50 LUFS are rejected. Verified limiter reduction is capped at 3 dB (with a small numerical margin) and iterative export correction is capped to avoid crushing high crest-factor material; such material can remain below the target and is reported as safety-limited. Turning the target off or enabling BYPASS MASTERING crossfades to the original stereo sum with the displayed safety trim and peak protection. The trim is shared across all sounds; it prevents the original demo sum from exceeding full scale. It does not normalize individual sounds.

EXPORT WAV and EXPORT ALL STEMS write a 24-bit stereo master and verify the completed WAV. Desktop installs with FFmpeg under Tools use an independent EBU R128 / true-peak measurement; other installs use the built-in meter. Results and applied gain appear beside the controls and in MASTERING.txt. The meter continues to show live final output, separately from the export result. A -14 LUFS target is an optional playback-normalization reference, not a mandatory Spotify mastering rule.

Each sound has a velocity slider under every step, from 1 (soft) to 127 (full level). Click the step button to turn the hit on/off; drag its slider to set its strength. Lower velocities give active steps a darker colour. Sliders retain their values when steps are toggled off and on. New steps default to 127, and DEMO GROOVE includes softer hats, shaker accents and log drum ghost notes. Velocity affects playback, WAV mixes, stems and melodic MIDI. SAVE/LOAD PATTERN preserves all velocities; older saves without velocity data load at 127 to preserve their audio levels. CLEAR ALL resets velocities to 127; CLEAR SOUND switches off its hits while retaining their velocities.

The sequencer supports generated per-step pitches. MIDI exports those notes, including the generated Pad/Strings major triads, with each step's velocity and one swung sixteenth-note gate. MIDI instruments can therefore sustain differently from WAV samples. Custom samples may need MIDI pitch adjustment to match their root notes. Current SAVE/LOAD PATTERN still stores one local 16-step pattern.

Feature order: velocity (implemented) â†’ manual pitch/note editing â†’ longer patterns â†’ multiple patterns â†’ arrangement mode â†’ mute/solo â†’ per-sound volume/pan â†’ project save/load.


## Generate a new song idea

Press **GENERATE NEW SONG** beside the pattern controls in Play mode. It replaces the current 16-step idea only when clicked; it does not write over SAVE PATTERN. Playback restarts cleanly with the new idea if it was running. Generation changes tempo and swing; master volume, samples and all 25 pads remain available.

The generator switches between five profiles: Deep & spacious, Driving Bacardi, Broken groove, Melodic bounce and Percussion session. Consecutive generations change profile and key. Each profile has different kick/logdrum rhythms, melodic density, lead choices, percussion density, tempo and swing. Motifs also vary their direction, intervals and placement. Every profile uses all 25 instruments, adding quiet supporting accents to parts outside its main groove. Melody and logdrum use a shared transposed major pentatonic scale; harmony uses I/IV/V major roots. Transition effects use low-velocity accents in the one-bar loop. The status line shows the new profile, BPM and swing; you can adjust tempo and swing afterwards.

Candidates must differ from the current pattern in at least 24 on/off cells, two instrument rhythms and four melodic cells (rhythm or pitch) before replacement. The 24-cell threshold is skipped when starting from an empty sequencer so spacious ideas can still be generated. This prevents velocity-only changes from counting as a new idea. Kick, logdrum and melody rhythms also move, add and remove individual hits. The generator remembers the last 256 generated grooves during the current session and rejects matching hit layouts even if key, tempo or velocity differ. Each retry chooses a fresh profile and key. Generation tries at most 128 candidates, retaining the current idea if no suitable candidate is found. These are musical constraints, not an objective guarantee of perceived similarity. PLAY still loops the current 16-step idea.

Per-step pitch offsets now drive AudioSource pitch, resampled WAV/stem audio and MIDI notes. Export length accounts for pitch-shifted sample tails. SAVE/LOAD also stores pitch offsets; older saves default to their original pitches. DEMO GROOVE and CLEAR ALL reset pitch offsets. Custom samples are transposed relative to the original pad roots; correctly tuned source samples give the most consistent harmony. Pitch shifting also changes sample duration. Generation creates a one-bar song idea within the existing sequencer, not a full multi-section song.



The default groove starts at 104 BPM with 16% swing, pitched logdrum answers, ghost hits, offbeat open hats and rolling shakers. Generated profiles use relaxed tempos from 96 to 114 BPM and syncopated logdrum phrases. Generating a new idea no longer forces an extra tempo jump. The rhythm kit now has a saturated FM logdrum, short punchy kick, three-burst clap, tuned sub and distinct hat/shaker envelopes. Playback and WAV exports use the same kit; custom samples retain precedence. Restart Unity Play mode to hear the updated sounds.



## Mastering comparison

**BYPASS MASTERING** retains the analyzed target gain for an instant return to the mastered path. Both paths run continuously with identical lookahead delay, so switching does not skip samples or change phase alignment. Enabling, disabling and bypassing crossfade over 50 ms; gain edits also ramp. Measurement resets do not reset audio-processing history. The existing loudness meters continue measuring the actual output, including transitions. The analyzed result shows maximum limiter reduction and bypass safety trim. Bypass also applies to Full Mix WAV export, with its safety trim recorded in MASTERING.txt; individual stem targeting is independent of Full Mix bypass.

Bypass is level-safe, but its loudness need not match the target. For subjective testing use the two WAVs in Tools/LufsValidation: Bypass Master.wav and Level Matched Master.wav. The second is an attenuated listening copy of the mastered export, matched to the bypass integrated loudness; Target Master.wav retains the actual target result. Keep listening volume fixed and compare kick/logdrum attacks, bass movement and percussion tails. Numerical peak and transition checks do not prove perceptual transparency.

## Deterministic transient protection (current behavior)

Fresh playback resets integrated/short-term measurement, peak reconstruction, limiter envelope/lookahead/history and comparison state. Fresh analysis pauses active voices, builds deterministic PCM from the current notes and velocities, computes new measurement/limiter state, and restarts playback from the beginning if it was running. Bypass toggles retain warm histories for click-free comparison; they are not fresh playback starts.

Analyze and target WAV export use the same analysis policy: measure the finite one-bar arrangement with full tails, and a deterministic 16-repeat reference with overlapping tails. Long custom samples extend the reference to reach full tail overlap. Choose one shared fixed gain that respects both programs and never exceeds 3 dB measured maximum limiter reduction. The gain is held during playback, without feedback from live LUFS. Edits require re-analysis. The UI displays the finite result, predicted loop loudness and maximum reduction, with TP LIMITED / transient protection when the requested target is constrained.

The same PCM/settings/duration produces the same results across fresh runs. A finite one-bar export including its decay and continuously overlapping playback contain different PCM; their integrated loudness can differ legitimately. For the default demo, the safe finite export measures -16.7 LUFS-I while the 16-bar loop reference measures -14.9 LUFS-I, both at the same -0.264 dB gain and at -1.3 dBTP. Maximum reduction is 2.23 dB finite / 2.98 dB loop. A forced -14.1 LUFS finite fixture needs 5.63 dB reduction and is excluded from the normal mastering mode. See Tools/LufsValidation/Deterministic-Mastering-Report.md for all three independent runs and listening files. Earlier numerical results in historical reports are superseded by this policy.



