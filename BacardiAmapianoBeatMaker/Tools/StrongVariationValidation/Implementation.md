# Versioned STRONG musical-event variations

New STRONG variations use plan version 2, event-generator version 1, a persisted source-groove snapshot, 2/4-bar phrase length, and complete per-bar/per-instrument attack notes and velocities. Legacy version-1 masks and the original legacy planner methods are unchanged. REUSE of an old v1 plan retains that plan; generating a new STRONG variation creates v2 events. SUBTLE and MEDIUM retain their existing planner/mask paths.

## Files

- `Assets/Scripts/BacardiSongVariation.cs`: additive versioned event/snapshot/score fields; legacy planning methods unchanged.
- `Assets/Scripts/BacardiVariationEvents.cs`: offline deterministic phrase generation, validation, scoring and bounded acceptance.
- `Assets/Scripts/BacardiBeatMaker.Variation.cs`: v1/v2 validation/resolution, event generation/replay, score display and measured layout height.
- `Assets/Scripts/BacardiBeatMaker.cs`: resolved event pitch/velocity in playback, PCM and MIDI; saved-score display.
- `Assets/Scripts/BacardiBeatMaker.Arrangement.cs`: generated notes/velocities in arranged MIDI.
- `Assets/Editor/BacardiStrongVariationRuntimeTest.cs`: offline event/PCM/MIDI/save/replay/v1/vocal-preview integration tests.
- `Assets/Editor/BacardiStereoMasterUiTest.cs`: bounded wait for the actual Game-view resolution to match the requested test size before asserting layout.
- `Assets/Editor/BacardiArrangedSongRuntimeTest.cs`: unique synthetic output folders, inactive-bar assertions using the existing nearest-sample bar convention, existing preview safety-gain verification, and cached diagnostic strings; production rendering unchanged.
- `Tools/VariationValidation/StrongVariationCorpus.cs` and `Run-StrongVariationCorpus.ps1`: fixed corpus and reproducible measurements.
- `Tools/VariationValidation/Run-VariationValidation.ps1`: include the new event type dependency while running the unchanged legacy tests.

## Musical behavior

The generator repeats 2/4-bar motifs, rotates piano/Rhodes/pluck leads, changes chordal attack spacing, creates new log-drum/sub-bass patterns and entrances, changes percussion combinations/density, uses phrase fills and boundary transitions, and makes breakdowns sparse. Drop 2 has a different lead and low-end/rhythmic pattern from Drop 1. Melody is drawn from the best-fitting diatonic pitch set inferred from the source. Existing pad/string chord samples keep their source voicing/root. Melodic/low-end register changes stay within one octave of the source anchor and the existing +/-24-semitone limits.

Source groove cells, note offsets, velocities, BPM, sample library and 25-instrument architecture are preserved. V2 events resolve consistently for playback, PCM, stereo mix and MIDI. Accepted events, accepted seed, source snapshot, generator version and scores persist for exact saved replay. Unity's nested-object serialization is canonicalized at creation to preserve byte-identical JSON replay as well as exact event/PCM replay.

## Score and final gates

Each musical family scores attack additions/removals and pitch/register changes over the union of resolved events. Velocity changes alone earn no acceptance credit. Melody (instruments 0-5), low end (6-7), and percussion/transitions (8-24) have equal weight in the overall score. Corresponding four-bar body phrases are phase-aligned; transitions align to section start/end. Section-length/boundary metadata earns no credit.

| Comparison | Overall minimum | Each family minimum |
| --- | ---: | ---: |
| Original source groove | 0.85 | 0.75 |
| Previous variation, when present | 0.45 | 0.25 |
| Drop 1 vs Drop 2 | 0.50 | separate family scores reported |

At most 32 deterministic candidate seeds are examined. Failure occurs before stopping/replacing the current plan. The accepted seed is displayed/persisted, and explicit replay uses the persisted plan rather than rerunning history-dependent acceptance.

The initial 0.45 source gate was permissive relative to the observed source-score distribution, so source and drop gates were tightened. The previous-variation family minimum remains 0.25 because accepted corpus cases reach that boundary; the previous overall gate remains 0.45 with observed minimum 0.497238. The stronger final gates were revalidated against the full fixed corpus.

## Fixed corpus

Three fixed source grooves (base, denser transposed melody, denser transposed percussion), seven personalities, and sixteen fixed seeds each: **336/336 accepted**, no failures, 179 cases requiring retries, maximum 12 candidates. There are 1,680 per-case assertions plus two aggregate acceptance/separation checks.

| Metric | SUBTLE | MEDIUM | STRONG |
| --- | ---: | ---: | ---: |
| Mean overall source difference | 0.735948 | 0.712137 | 0.941716 |
| Mean melody difference | 0.694378 | 0.616737 | 0.969918 |
| Mean low-end difference | 0.785978 | 0.806021 | 1.000000 |
| Mean percussion difference | 0.727487 | 0.713655 | 0.855231 |

STRONG source overall range: 0.928744-0.949944. Source family minima: melody 0.930435, low end 1.000000, percussion 0.842199. Previous overall minimum: 0.497238; previous family minimum: 0.250000. Drop contrast minimum: 0.625490. STRONG exceeds the legacy levels substantially in every family on this corpus; these engineering scores do not substitute for a listening review.

Full per-seed measurements: [strong-corpus.csv](../VariationValidation/strong-corpus.csv). Summary: [strong-corpus-results.txt](../VariationValidation/strong-corpus-results.txt).

## Integration evidence

The new runtime suite verifies new attacks, exact source preservation, 2/4-bar motif replay, drop/section contrast, sparse breakdowns, generated event resolution, sample-exact PCM replay, exact arranged MIDI timing/notes/velocities, v1 loading, v2 saved replay, length-only score zero, failed-generation retention, Song bars synchronization, and actual arranged preview PCM containing a placed synthetic vocal. Generated stereo differs from the same-structure source-mask render by RMS **0.101184**. Melody, log-drum and shaker renders each differ independently.

Tests use synthetic fixtures, with the existing saved-vocal suite optionally validating the local PART 8 vocal files read-only. No provider/API calls. No user song exports/mastering. **PART 10 remains untouched.**

Completed offline regressions:

| Suite | Result |
| --- | --- |
| New STRONG runtime integration | 21 checks passed |
| Fixed corpus | 336/336 cases; 1,680 per-case assertions + 2 aggregate checks passed |
| Legacy managed planner | 6,155 checks passed |
| Existing variation end-to-end | 20 checks passed |
| Legacy arrangement end-to-end | 11 checks passed; all 25 stems, MIDI, stereo, preview, one-bar fallback and 12 source WAV hashes |
| Song bars synchronization | 11 checks passed |
| Saved vocal reload | 23 checks passed |
| Arranged vocal preview | 13 checks passed |
| Combined stereo/vocal export | 15 checks passed |
| Vocal provider/source routing | 16 checks passed |
| Managed vocal validation | 10 checks passed |
| Stereo mastering | 16 checks plus independent FFmpeg measurement passed (-14.1 LUFS-I / -1.1 dBTP) |
| Deterministic mastering DSP | Three deterministic runs, three fresh live-processing sessions, quiet guards, stereo balance/toggles, and seven independent FFmpeg measurements passed |
| Mastering runtime UI | 57 assertions across four Game-view sizes passed (success/failure results) |

The UI regression initially asserted resolution before Unity had applied it; the test now waits up to 15 seconds. The legacy arrangement silence assertion initially included one sample belonging to the next active bar by using Ceiling; it now uses the renderer's established nearest-sample boundaries. Its preview assertion now includes the already-existing common safety attenuation for over-full-scale mixes. Repeated diagnostic-string allocation was removed from the sample loop without removing assertions. These test corrections do not change mastering or renderer audio behavior.

Runtime details: [STRONG runtime](runtime-results.txt), [legacy arrangement](../ArrangementValidation/runtime-results.txt), [existing variation](../VariationValidation/unity-results.txt), [Song bars](../VocalSongLengthValidation/runtime-results.txt), [reload](../SavedVocalValidation/runtime-results.txt), [preview](../ArrangedVocalPreviewValidation/runtime-results.txt), [combined export](../CombinedVocalMixValidation/runtime-results.txt), [source routing](../VocalValidation/Source-Routing-Runtime.txt), [mastering UI](../StereoUiValidation/runtime-ui-results.txt).
