using System;

// Local lyric writing: each group shares a rhyme and a dance-floor theme.
public static class BacardiHookLyrics
{
    static readonly string[][][] Groups = {
        new[] {
            new[] { "Your rhythm pulls me into the night", "We catch a spark beneath the light", "The bass comes low, our hearts take flight", "Your smile makes every moment bright" },
            new[] { "We move together, feeling right", "Our shadows dance in golden light", "Stay close until the morning light", "Let every step ignite the night" },
            new[] { "The log drum rolls, we come alive", "With every beat our dreams take flight", "Your heartbeat keeps my soul alight", "We let the city shine tonight" },
            new[] { "Just you and me till morning light", "We keep this feeling burning bright", "One more dance beneath the light", "Hold on to me, we'll be alright" }
        },
        new[] {
            new[] { "The bass runs deep, we take it slow", "Your footsteps tell me where to go", "Beneath the stars, our feelings grow", "You feel the rhythm, let it show" },
            new[] { "Our bodies sway, our spirits flow", "The log drum calls from down below", "You smile at me, the room aglow", "We find a place that only we know" },
            new[] { "No rush tonight, just let it flow", "With every turn the sparks will grow", "We ride the groove and let it go", "Our hearts keep time, our faces glow" },
            new[] { "Stay for the sunrise, soft and slow", "Where this will lead, we'll never know", "One more refrain before we go", "Keep dancing with me, let love grow" }
        },
        new[] {
            new[] { "I hear the log drum calling me", "Your laughter sets my spirit free", "We move like waves upon the sea", "Come find the groove right here with me" },
            new[] { "No place tonight I'd rather be", "Your hands keep perfect time with me", "We share a secret melody", "The moon lights up our energy" },
            new[] { "Let all our worries drift to sea", "This rhythm feels like home to me", "We dance into a memory", "Our hearts unlock the harmony" },
            new[] { "Stay in this moment here with me", "Until the dawn, just you and me", "One final chorus, wild and free", "Our song goes on endlessly" }
        }
    };

    public static string Generate(Random random, string previous)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));
        string hook;
        do {
            var group = Groups[random.Next(Groups.Length)];
            var lines = new string[group.Length];
            for (int i = 0; i < lines.Length; i++) lines[i] = group[i][random.Next(group[i].Length)];
            hook = string.Join("\n", lines);
        } while (hook == previous);
        return hook;
    }
}
