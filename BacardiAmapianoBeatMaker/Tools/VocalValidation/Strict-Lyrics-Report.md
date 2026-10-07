# Strict lyrics verification

STRICT LYRICS / WORD PRESERVATION and SLOWER PHRASE DENSITY are optional, off-by-default REAL AI controls. The UI reports their enabled state. Their request flags append separate positive_styles guidance at the proxy; original Lyrics and manually supplied AiPronunciationText are never rewritten. BPM, bars, duration, preview and arrangement timing are unchanged. Export metadata records both settings separately. MOCK ignores the flags.

Passed offline proxy self-tests for all toggle combinations with isiZulu and Unicode originals, and null, empty, whitespace-only and populated pronunciation overrides. A fake HTTP handler inspects the exact outgoing UTF-8 sung text and separate guidance. Payload comparisons prove only positive_styles changes.

Passed runtime source compilation and managed vocal validation: exact lyric export, separate option metadata, identical MOCK PCM with options enabled, timeline, aligned export, regeneration and cancellation.

The isolated Unity runtime check could not start its tests because Unity Licensing Client initialization failed. Live UI/preview verification is therefore incomplete. No paid or live ElevenLabs request was made. Offline tests cannot guarantee that generated singing will obey the guidance.

Restart the existing local proxy before using the new flags for the next generation.
