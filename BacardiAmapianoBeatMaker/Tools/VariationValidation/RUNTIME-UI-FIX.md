# SONG VARIATION runtime UI

The production builder is `BacardiBeatMaker.OnGUI()` in `Assets/Scripts/BacardiBeatMaker.cs`, delegating to `DrawSongVariation` in the partial variation file. This is runtime IMGUI, rather than a Canvas/editor-only panel.

## Findings and cause limits

Before this fix, the production source already called `DrawSongVariation()` as the first item inside the main scroll view. Therefore an absent runtime draw call was not demonstrated. The old section had unconstrained layout, no SONG PERSONALITY label, no SAVE VARIATION / LOAD VARIATION buttons, and no preview until a plan existed. Its old NEW SEED button immediately created a plan. Previous integration tests invoked planner functions through reflection and did not inspect or click the runtime UI.

Those are confirmed code defects and coverage gaps. The exact cause of the earlier screenshot's missing section remains unverified: no screenshot was attached to this request, and the native computer-use connection failed with `native pipe ... cannot find the file specified`. The open editor could not be observed, stopped, or restarted through that connection. A stale live assembly, an existing scroll position, or another view cannot be confirmed from the provided text. Do not present any of these as an established root cause.

## Changes

- Bound the main scroll width and reserve at least 1800 pixels plus the planner's explicit height; content still grows with the remaining runtime panels.
- Give the planner explicit width and calculated height, including wrapped preview text. Personality controls wrap into two columns at narrow widths.
- Add all requested labels/buttons and retain ONE-BAR GROOVE.
- Bind seven personalities, three amounts, and the seed field directly to existing request state. Generate and replay call the unchanged existing planner entry point.
- NEW SEED updates the seed request only. REUSE CURRENT SEED restores the active plan seed before replaying it.
- SAVE/LOAD VARIATION uses a separate variation-only preference key and the existing plan validator. Loading does not load groove/tempo data.
- Always show a current arrangement preview and a LOW/MEDIUM/HIGH difference readout.
- Leave the planner algorithm, audio generation code, mastering, vocals, stems, exports, and tempo behavior untouched.

## Verification

`BacardiSongVariationUiTest` enters Unity Play Mode in an isolated project and exercises the actual production `OnGUI` through Game-view mouse-down/up events, not direct selector assignments. It verifies the visible section, each of seven personalities and three amounts, NEW SEED, current-seed replay, Generate's planner result and changed preview, and save/load. Each control is scrolled into the measured runtime viewport before clicking. Requests remain paused and 104 BPM.

Final results are in `runtime-ui-results.txt`: three rendered sizes, 2400x1324, 1200x649, and 800x574, all pass. Host sizes differ because of Windows DPI scaling. Coordinates are converted using Unity GameView's render-target rectangle; see [Unity GameView source](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/GameView/GameView.cs).

`song-variation-before-planning.png` captures the isolated test Game view after entering Play Mode, before selector/seed/generate clicks. It is not a screenshot of the user's already-open editor. The capture mirrors Unity's Direct3D orientation correction.

Run with `powershell -NoProfile -ExecutionPolicy Bypass -File Tools/VariationValidation/Run-UnityVariationUiValidation.ps1`. The override affects only that process. The fixture restores its prior saved-variation preference. No playback, generated vocal/audio export, network provider call, ElevenLabs call, or paid generation is invoked.
