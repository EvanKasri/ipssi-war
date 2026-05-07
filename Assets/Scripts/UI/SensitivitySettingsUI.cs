using UnityEngine;

/// <summary>
/// IPSSI-WAR : panneau reglages affiche avec Tab.
/// - Sensibilite souris (persiste en PlayerPrefs).
/// - Volume musique / volume SFX + mute (persiste via GameAudio).
/// </summary>
public class SensitivitySettingsUI : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    public KeyCode m_ToggleKey = KeyCode.Tab;
    public float m_Min = 0.5f;
    public float m_Max = 8f;

    private float m_Sens;
    private CursorLockMode m_PrevLock;
    private bool m_PrevVisible;
    private float m_NextAudioRefresh;

    private void Awake()
    {
        m_Sens = PlayerPrefs.GetFloat("MouseSensitivity", 2.5f);
        CameraControl.MouseSensitivity = m_Sens;

        // Charge et applique les reglages audio des l'entree en game.
        GameAudio.Load();
        GameAudio.Apply();
    }

    private void Update()
    {
        if (Input.GetKeyDown(m_ToggleKey))
            Toggle();

        // Reapplique les volumes periodiquement pour rattraper les AudioSources
        // crees apres le debut de partie (tanks spawnes par GameManager, shells...).
        if (Time.unscaledTime >= m_NextAudioRefresh)
        {
            m_NextAudioRefresh = Time.unscaledTime + 0.5f;
            GameAudio.Apply();
        }
    }

    private void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen)
        {
            m_PrevLock = Cursor.lockState;
            m_PrevVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            PlayerPrefs.SetFloat("MouseSensitivity", m_Sens);
            PlayerPrefs.Save();
            CameraControl.MouseSensitivity = m_Sens;
            GameAudio.Save();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void OnGUI()
    {
        // Petit hint en bas de l'ecran
        GUI.color = new Color(1f, 1f, 1f, 0.65f);
        GUI.Label(new Rect(10, Screen.height - 24, 400, 22),
            $"[{m_ToggleKey}] Reglages  -  Sensi : {m_Sens:F2}");
        GUI.color = Color.white;

        if (!IsOpen) return;

        const float w = 460f, h = 360f;
        float x = (Screen.width - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        GUI.Box(new Rect(x, y, w, h), "Reglages");

        // --- Sensibilite ---
        float row = y + 40f;
        GUI.Label(new Rect(x + 20, row, w - 40, 22),
            $"Sensibilite souris : {m_Sens:F2}");
        row += 26f;
        m_Sens = GUI.HorizontalSlider(
            new Rect(x + 20, row, w - 40, 22), m_Sens, m_Min, m_Max);
        CameraControl.MouseSensitivity = m_Sens;
        row += 22f;
        GUI.Label(new Rect(x + 20, row, w - 40, 22),
            $"({m_Min:F1} = lente, {m_Max:F1} = rapide)");
        row += 28f;

        // --- Audio ---
        bool audioChanged = false;

        GUI.Label(new Rect(x + 20, row, w - 40, 22),
            $"Musique : {Mathf.RoundToInt(GameAudio.MusicVolume * 100f)} %");
        bool newMusicMute = GUI.Toggle(
            new Rect(x + w - 110, row, 90, 22), GameAudio.MusicMuted, "Muet");
        if (newMusicMute != GameAudio.MusicMuted) { GameAudio.MusicMuted = newMusicMute; audioChanged = true; }
        row += 24f;
        float newMusic = GUI.HorizontalSlider(
            new Rect(x + 20, row, w - 40, 22), GameAudio.MusicVolume, 0f, 1f);
        if (!Mathf.Approximately(newMusic, GameAudio.MusicVolume))
        { GameAudio.MusicVolume = newMusic; audioChanged = true; }
        row += 30f;

        GUI.Label(new Rect(x + 20, row, w - 40, 22),
            $"Effets (SFX) : {Mathf.RoundToInt(GameAudio.SfxVolume * 100f)} %");
        bool newSfxMute = GUI.Toggle(
            new Rect(x + w - 110, row, 90, 22), GameAudio.SfxMuted, "Muet");
        if (newSfxMute != GameAudio.SfxMuted) { GameAudio.SfxMuted = newSfxMute; audioChanged = true; }
        row += 24f;
        float newSfx = GUI.HorizontalSlider(
            new Rect(x + 20, row, w - 40, 22), GameAudio.SfxVolume, 0f, 1f);
        if (!Mathf.Approximately(newSfx, GameAudio.SfxVolume))
        { GameAudio.SfxVolume = newSfx; audioChanged = true; }

        if (audioChanged) GameAudio.Apply();

        if (GUI.Button(new Rect(x + (w - 140) * 0.5f, y + h - 40, 140, 28),
            $"Fermer ({m_ToggleKey})"))
            Toggle();
    }
}
