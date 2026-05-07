using UnityEngine;

/// <summary>
/// IPSSI-WAR : hit marker FPS-style (4 traits formant un X) au centre de l'ecran
/// quand le joueur local touche un adversaire. Joue aussi un petit "tic" sonore.
/// Singleton cree dynamiquement par LocalPlayerSetup.
/// </summary>
public class HitMarkerUI : MonoBehaviour
{
    public static HitMarkerUI Instance { get; private set; }

    [Header("Affichage")]
    public float m_DisplayTime = 0.35f;
    public Color m_Color = new Color(1f, 0.95f, 0.2f, 1f);
    public float m_Size = 14f;
    public float m_Gap  = 5f;

    [Header("Son")]
    public AudioClip m_HitSound;
    [Range(0f, 1f)] public float m_Volume = 0.7f;

    private float m_TimeLeft;
    private AudioSource m_Audio;
    private static Texture2D s_Pixel;

    private void Awake()
    {
        Instance = this;
        m_Audio = gameObject.AddComponent<AudioSource>();
        m_Audio.playOnAwake = false;
        m_Audio.spatialBlend = 0f;

        // Genere un "tic" synthetique si aucun clip n'est fourni
        if (m_HitSound == null)
            m_HitSound = GenerateTickClip();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Trigger()
    {
        m_TimeLeft = m_DisplayTime;
        if (m_HitSound != null) m_Audio.PlayOneShot(m_HitSound, m_Volume);
    }

    private void Update()
    {
        if (m_TimeLeft > 0f) m_TimeLeft -= Time.deltaTime;
    }

    private void OnGUI()
    {
        if (m_TimeLeft <= 0f) return;
        float a = Mathf.Clamp01(m_TimeLeft / m_DisplayTime);
        Color prev = GUI.color;
        GUI.color = new Color(m_Color.r, m_Color.g, m_Color.b, a);

        float cx = Screen.width * 0.5f;
        float cy = Screen.height * 0.5f;
        float s = m_Size;
        float g = m_Gap;

        // 4 segments diagonaux formant un X autour du centre
        DrawLine(cx - s - g, cy - s - g, cx - g, cy - g);
        DrawLine(cx + s + g, cy - s - g, cx + g, cy - g);
        DrawLine(cx - s - g, cy + s + g, cx - g, cy + g);
        DrawLine(cx + s + g, cy + s + g, cx + g, cy + g);

        GUI.color = prev;
    }

    private static Texture2D Pixel
    {
        get
        {
            if (s_Pixel == null)
            {
                s_Pixel = new Texture2D(1, 1);
                s_Pixel.SetPixel(0, 0, Color.white);
                s_Pixel.Apply();
            }
            return s_Pixel;
        }
    }

    private void DrawLine(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float len = Mathf.Sqrt(dx * dx + dy * dy);
        float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(ang, new Vector2(x1, y1));
        GUI.DrawTexture(new Rect(x1, y1 - 1.5f, len, 3f), Pixel);
        GUI.matrix = saved;
    }

    // Tick aigu court genere a la volee
    private AudioClip GenerateTickClip()
    {
        int sr = 44100;
        float dur = 0.07f;
        int samples = (int)(sr * dur);
        float[] data = new float[samples];
        float freq = 1800f;
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / sr;
            float env = Mathf.Exp(-t * 35f);
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.6f;
        }
        AudioClip clip = AudioClip.Create("HitMarkerTick", samples, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }
}
