using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IPSSI-WAR : reglages audio centralises (musique vs SFX) avec persistance
/// PlayerPrefs et application dynamique sur tous les AudioSources de la scene.
///
/// Heuristique de classification :
///   - tag "Music" sur le GameObject  -> musique
///   - nom du GameObject contient "music" (case insensitive) -> musique
///   - sinon -> SFX
///
/// Les volumes initiaux des AudioSources sont memorises a la 1re passe pour
/// servir de baseline (multiplicateur applique par-dessus).
/// </summary>
public static class GameAudio
{
    private const string PP_MUSIC_VOL = "GameAudio.MusicVolume";
    private const string PP_SFX_VOL   = "GameAudio.SfxVolume";
    private const string PP_MUSIC_MUTE = "GameAudio.MusicMuted";
    private const string PP_SFX_MUTE   = "GameAudio.SfxMuted";

    public static float MusicVolume = 1f;   // 0..1
    public static float SfxVolume   = 1f;   // 0..1
    public static bool  MusicMuted  = false;
    public static bool  SfxMuted    = false;

    // Baseline (volume tel que pose par le designer dans l'inspector).
    // Cle = instanceID de l'AudioSource. Recharge si le source revient apres reload.
    private static readonly Dictionary<int, float> s_BaselineVol = new Dictionary<int, float>();
    private static bool s_Loaded;

    public static void Load()
    {
        if (s_Loaded) return;
        MusicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PP_MUSIC_VOL, 0.7f));
        SfxVolume   = Mathf.Clamp01(PlayerPrefs.GetFloat(PP_SFX_VOL,   1.0f));
        MusicMuted  = PlayerPrefs.GetInt(PP_MUSIC_MUTE, 0) != 0;
        SfxMuted    = PlayerPrefs.GetInt(PP_SFX_MUTE,   0) != 0;
        s_Loaded = true;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat(PP_MUSIC_VOL, MusicVolume);
        PlayerPrefs.SetFloat(PP_SFX_VOL,   SfxVolume);
        PlayerPrefs.SetInt(PP_MUSIC_MUTE, MusicMuted ? 1 : 0);
        PlayerPrefs.SetInt(PP_SFX_MUTE,   SfxMuted   ? 1 : 0);
        PlayerPrefs.Save();
    }

    // IPSSI-WAR : detecte une source musique. Un AudioSource est considere comme
    // musique si :
    //   - tag du GameObject = "Music"
    //   - nom du GameObject ou de l'AudioClip contient music/bgm/ost/ambient/theme/song
    // Sinon : SFX.
    public static bool IsMusicSource(AudioSource src)
    {
        if (src == null) return false;
        if (src.gameObject.CompareTag("Music")) return true;

        string objName = src.gameObject.name ?? "";
        string clipName = src.clip != null ? src.clip.name : "";
        string combined = (objName + "|" + clipName).ToLowerInvariant();

        if (combined.Contains("music") || combined.Contains("bgm") ||
            combined.Contains("ost") || combined.Contains("ambient") ||
            combined.Contains("theme") || combined.Contains("song"))
            return true;

        return false;
    }

    /// <summary>Scanne tous les AudioSources et applique les reglages.</summary>
    public static void Apply()
    {
        Load();
        AudioSource[] all = Object.FindObjectsOfType<AudioSource>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var src = all[i];
            if (src == null) continue;

            int id = src.GetInstanceID();
            if (!s_BaselineVol.TryGetValue(id, out float baseVol))
            {
                baseVol = src.volume;
                s_BaselineVol[id] = baseVol;
            }

            bool isMusic = IsMusicSource(src);
            float mult = isMusic
                ? (MusicMuted ? 0f : MusicVolume)
                : (SfxMuted   ? 0f : SfxVolume);
            src.volume = baseVol * mult;
        }
    }
}
