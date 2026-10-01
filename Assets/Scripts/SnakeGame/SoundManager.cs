using System.Collections.Generic;
using UnityEngine;

/// Runtime audio hub, created before the scene loads:
///  - GameMusic plays from the moment the game opens and loops when it ends.
///  - One-shot SFX are trimmed to the length the event actually needs, because
///    several imports carry long silent tails (AppleBite is ~15s for a bite).
/// Options toggles (MusicOn / SfxOn PlayerPrefs) control it via RefreshPrefs().
public static class SoundManager
{
    public enum Sfx { AppleBite, CrackStone, GameOver, HeadPowerUp, OtherPowerUp }

    const float MusicVolume = 0.30f;
    const float SfxVolume = 0.85f;

    // Seconds of each import that are actually used (rest is tail/silence).
    static readonly Dictionary<Sfx, float> KeepSeconds = new Dictionary<Sfx, float>
    {
        { Sfx.AppleBite,   0.6f },
        { Sfx.CrackStone,  1.2f },
        { Sfx.GameOver,    3.0f },
        { Sfx.HeadPowerUp, 1.2f },
        { Sfx.OtherPowerUp,1.5f },
    };

    // Seconds to skip at the front: the real content can start later than the
    // first audible sample (AppleBite has ~250ms of faint pre-noise before the
    // actual bite at 0.3s — playing from there made the eat sound feel late).
    static readonly Dictionary<Sfx, float> StartSeconds = new Dictionary<Sfx, float>
    {
        { Sfx.AppleBite,   0.24f },
        { Sfx.CrackStone,  0f },
        { Sfx.GameOver,    0f },
        { Sfx.HeadPowerUp, 0f },
        { Sfx.OtherPowerUp,0f },
    };

    // Resource name per Sfx value, index-aligned with the enum.
    static readonly string[] ResNames = { "AppleBite", "CrackStone", "GameOver", "HeadPowerUps", "OtherPowerUps" };

    static readonly Dictionary<Sfx, AudioClip> ready = new Dictionary<Sfx, AudioClip>();

    static AudioSource musicSrc;
    static AudioSource sfxSrc;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("SoundManager");
        Object.DontDestroyOnLoad(go);

        musicSrc = go.AddComponent<AudioSource>();
        musicSrc.playOnAwake = false;
        musicSrc.loop = true;                 // replay automatically when it ends
        musicSrc.spatialBlend = 0f;

        sfxSrc = go.AddComponent<AudioSource>();
        sfxSrc.playOnAwake = false;
        sfxSrc.spatialBlend = 0f;
        sfxSrc.dopplerLevel = 0f;

        var music = Resources.Load<AudioClip>("Sounds/GameMusic");
        if (music != null)
        {
            musicSrc.clip = music;
            musicSrc.Play();                  // starts with the menu, runs through the game
        }
        RefreshPrefs();
    }

    /// Re-reads the Options toggles; called by the menu switches on change.
    public static void RefreshPrefs()
    {
        // Music keeps playing silently when off, so switching back on resumes seamlessly.
        if (musicSrc != null) musicSrc.volume = MainMenu.MusicOn ? MusicVolume : 0f;
        if (sfxSrc != null) sfxSrc.mute = !MainMenu.SfxOn;
    }

    public static void Play(Sfx which)
    {
        if (sfxSrc == null || !MainMenu.SfxOn) return;
        AudioClip clip = ClipFor(which);
        if (clip != null) sfxSrc.PlayOneShot(clip, SfxVolume);
    }

    /// True while the music is audibly running (test/diagnostic helper).
    public static bool MusicPlaying => musicSrc != null && musicSrc.isPlaying;

    static AudioClip ClipFor(Sfx which)
    {
        AudioClip clip;
        if (ready.TryGetValue(which, out clip)) return clip;

        int idx = (int)which;
        var src = Resources.Load<AudioClip>("Sounds/" + ResNames[idx]);
        clip = src;
        float keep, startAt;
        if (src != null && KeepSeconds.TryGetValue(which, out keep))
        {
            if (!StartSeconds.TryGetValue(which, out startAt)) startAt = 0f;
            clip = Trim(src, keep, startAt);   // cut the silent/late tail once, at boot
        }
        ready[which] = clip;
        return clip;
    }

    /// First `keep` seconds of the source clip as a fresh in-memory clip,
    /// starting at max(startAt, past leading silence) — so playback lands on
    /// the real hit instead of the padding that came before it.
    static AudioClip Trim(AudioClip src, float keep, float startAt)
    {
        int freq = src.frequency;
        int channels = src.channels;
        int total = src.samples;
        if (total <= 0) return src;

        var probe = new float[total * channels];
        if (!src.GetData(probe, 0)) return src; // not readable: fall back to the full clip

        // first audible sample (interleaved index -> frame), back off 8ms to keep the attack
        int first = -1;
        for (int i = 0; i < probe.Length; i++)
            if (Mathf.Abs(probe[i]) > 0.008f) { first = i; break; }
        if (first < 0) return src; // nothing audible: use the clip as-is

        int silenceSkip = Mathf.Max(0, first / channels - Mathf.CeilToInt(0.008f * freq));
        int start = Mathf.Max(silenceSkip, Mathf.CeilToInt(startAt * freq));
        int frames = Mathf.Min(Mathf.CeilToInt(keep * freq), total - start);
        if (frames <= 0) return src;

        var buf = new float[frames * channels];
        System.Array.Copy(probe, start * channels, buf, 0, frames * channels);

        var clip = AudioClip.Create(src.name + "_trim", frames, channels, freq, false);
        clip.SetData(buf, 0);
        return clip;
    }
}
