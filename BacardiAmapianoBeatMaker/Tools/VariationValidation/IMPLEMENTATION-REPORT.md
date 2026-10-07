# Song variation implementation report

## Root cause

The existing generator created only a 16-step, one-bar groove. PLAY repeated that same bar; WAV and MIDI represented the same bar rather than a developed song. Both generated grooves and legacy stem export filled unused instruments with quiet accents, repeatedly returning to all 25 sounds. Groove-cell similarity checks did not measure section boundaries, lead changes, sectional layer density, or logdrum/bass entrances because no multi-section plan existed. Loudness processing did not cause this structural repetition.

## Files changed

- `Assets/Scripts/BacardiBeatMaker.cs`: connects one shared song-event resolver to playback, PCM duration/rendering, WAV/stem export and MIDI; persists the plan with the existing pattern save; preserves current tempo during legacy groove generation. Long-song stems render sequentially rather than retaining 25 full-song buffers. Existing one-bar behavior remains available.
- `Assets/Scripts/BacardiBeatMaker.Variation.cs`: new SONG VARIATION controls, preview, seed generation/replay, plan validation, event resolver and full-song duration.
- `Assets/Scripts/BacardiSongVariation.cs`: pure deterministic planner and similarity checking; no Unity, audio-file IO, gain or provider dependencies.
- `Assets/Editor/BacardiSongVariationRuntimeTest.cs`: offline Unity rendering/export/transport acceptance.
- New `.meta` files for the three new Unity scripts.
- `Tools/VariationValidation/VariationCheck.cs`, `Run-VariationValidation.ps1`, `Run-UnityVariationValidation.ps1`, `README.md`, and this report: repeatable offline validation and documentation.
- `BEAT-MAKER.md`: current controls, save/replay semantics, export behavior and limitations.

Validation creates an isolated `Tools/UnityVariationProject`, response files, compiled checks, result logs and local export fixtures. No source WAV, scene, sample assignment, mastering engine, vocal generator/provider, or post-arrangement master-panel implementation was edited.

## Algorithm

A versioned xorshift32 PRNG makes a plan from the accepted signed integer seed, personality and variation amount. Plans retain six musical section roles while varying their durations and boundaries. SUBTLE uses the familiar 4/4/8/4/8/4-bar shape; MEDIUM uses broader 4/8-bar transitions and 8/12-bar drops; STRONG uses 2/4/6/8/12-bar transitions and 4/8/12/16-bar drops. Total song length follows these choices.

Seven personalities bias lead choices, melodic-layer count, percussion count and favored percussion families, vocal-chop use, and logdrum development. Each section chooses a subset of stems, a lead, energy, density, logdrum entrance and bass entrance. Build layers enter progressively; outros remove parts. Section-end reverse cymbal/riser and section-start crash/impact choices reposition existing effect hits. STRONG adds selective half-bar breaths. Per-bar sixteenth-note permission masks selectively omit existing groove hits. Piano, Rhodes, pad, pluck and strings can lead different sections. Existing source pitch/velocity is preserved at every retained event.

The resolver drives AudioSource scheduling, finite PCM, duration/tails and MIDI. Transition events retain the original effect velocity. Omitted stems remain silent in full-song export, while all 25 files retain the same start and duration for FL Studio. Mix-balanced stems keep the existing shared PCM headroom trim and unity-level reconstruction; no per-stem normalization is introduced. The optional reference-normalization mode remains the existing explicitly selected mode.

NEW SEED and repeated GENERATE clicks request new plans. REUSE CURRENT SEED intentionally bypasses novelty rejection. Automatic rejection advances deterministic candidate seeds and exposes the accepted seed, so replay does not depend on previous-song history. SAVE/LOAD stores both groove and accepted plan; exports include plan JSON and a readable preview. Matching audio also requires matching groove, tempo, swing and source clips.

## Similarity check

Distance is a weighted, normalized score between 0 and 1:

- 30% section stem-set Jaccard distance.
- 25% section starts, durations and total length.
- 10% percussion-layer density.
- 10% melodic-layer density and lead changes.
- 15% logdrum/bass entrance positions or absence.
- 10% sixteenth-note permission-mask differences sampled at section midpoints.

LOW is below 0.20, MEDIUM is 0.20–0.3799, HIGH is at least 0.38. Minimum accepted distance is 0.08 for SUBTLE, 0.20 for MEDIUM and 0.38 for STRONG. STRONG additionally requires changed section boundaries. Up to 1,024 candidates are checked before returning an error and retaining the current song. Saved-plan loading and explicit seed replay may show LOW difference because they reproduce a chosen arrangement.

This is a musical arrangement heuristic, not a claim about perceived listening distance. It measures planning decisions; the audible result also depends on which notes exist in the source groove and how long its samples sustain. Dropouts suppress new events without cutting sample tails. The system does not compose missing source melodies or reposition generated REAL AI vocals, which retain the existing independent workflow.

## Preservation and tests

The current tempo stays at 104 BPM unless changed by the user. Source audio files and the per-stem level/pitch/velocity data remain untouched. Mastering engine, REAL AI vocal generator/provider and POST-ARRANGEMENT MASTER TEST files are unchanged. Existing export buttons and formats remain; their event input now accepts the full-song plan. Full songs supply finite full-song PCM including tails to the existing mastering engine; legacy one-bar grooves retain their existing overlapping reference. The one-bar 16-bar diagnostic is excluded in full-song mode because that window would not represent the whole changing arrangement.

Completed offline results on 2026-10-06:

- **6,155 planner assertions passed:** 40 seeds × seven personalities × three amounts; 840 distinct accepted arrangements. Exact same-seed replay, history-independent accepted-seed replay, thresholds, omission of all-stem layering, delayed entrances and STRONG boundary changes passed.
- **12 source WAVs under Assets:** SHA-256 hashes unchanged through planner validation.
- **15 Unity integration checks passed:** 104 BPM and groove/velocity preservation, exact seed replay, SAVE/LOAD, full-song PCM/tails, silent omitted stems, existing mix-balanced export, 25 aligned stems plus full mix, exported plan, MIDI duration/event parity, retained vocal/master-test components, transport and legacy one-bar rendering. Maximum summed-stem error against the planned unmastered mix was **4.17232513427734e-7**, below the 1e-5 tolerance.
- **12 existing stereo-master checks passed:** import, full-song LUFS/peak analysis, target gain, limiter ceilings, WAV export, overwrite protection, source immutability and bypass.
- **Existing offline vocal suite passed:** lyric metadata, local hooks, timeline DSP mapping, pause/resume/STOP, mock vocal PCM preservation, aligned 32-bar stem export, tempo mismatch/regeneration and cancellation.
- Runtime scripts compile against installed Unity assemblies. The pre-existing provider metadata warning CS0649 remains unrelated to this change.

No ElevenLabs calls or paid API calls were made. Unity integration ran in an isolated project with access to the existing licensing client. Tests validate reproducibility and technical behavior; perceptual listening review remains a separate musical judgment.

Detailed results: `results.txt`, `unity-results.txt`, `unity.log`, plus the existing `Tools/VocalValidation` and `Tools/LufsValidation` results/fixtures. Commands are in the adjacent README.
