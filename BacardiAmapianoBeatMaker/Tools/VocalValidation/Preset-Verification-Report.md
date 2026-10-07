Preset verification

No generation was invoked. No ElevenLabs calls were made.

Production guidance now synchronizes the native IMGUI TextEditor buffer when the preset handler runs, releases keyboard focus, and explicitly uses readable text styling. These production changes appeared in the workspace during inspection and were preserved.

Added Assets/Editor/BacardiPresetInteractionTest.cs. Unity 6000.3.24f1 executed production Draw in an isolated EditorWindow. PASS: exact preset handler assignment from blank and focused edited states, subsequent repaint contents, native TextEditor contents, real keyboard editing, unchanged default lyrics, and exact CurrentRequest mapping. DisplayedPronunciationGuidance is read-only repaint telemetry; it does not supply the request. PronunciationGuidance is the single writable value used by the TextArea and CurrentRequest.

An isolated comparison with the old assignment-only handler also passed. Therefore stale editor state was not reproduced and is not a confirmed root cause of the user's blank field. Previous tests checked model assignment without verifying subsequent repaint, so their earlier success was insufficient evidence of visible rendering.

Simulated mouse clicks were not successfully delivered to the button in the isolated editor. The passing test invokes the exact production button handler within OnGUI and verifies its subsequent rendering and editing. Desktop Computer Use failed because the native pipe was unavailable. The user's existing Play Mode window was not inspected or controlled. Manual re-entry into Play Mode and a single actual preset click are still required before claiming the reported runtime symptom is resolved. Keep lyrics unchanged and do not generate during that check.
