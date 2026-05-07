using UnityEngine;

/// <summary>
/// IPSSI-WAR : feedback visuel quand le joueur local prend des degats.
/// 4 bandes rouges sur les bords de l'ecran qui flashent puis fondent.
/// L'intensite est proportionnelle aux degats subis.
/// </summary>
public class DamageVignette : MonoBehaviour
{
    public static DamageVignette Instance { get; private set; }

    private Texture2D m_Tex;
    private float m_Intensity;          // 0..1, intensite courante du flash
    private float m_FadePerSec = 1.4f;  // vitesse de disparition

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        m_Tex = new Texture2D(1, 1);
        m_Tex.SetPixel(0, 0, Color.white);
        m_Tex.Apply();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Declenche un flash rouge, intensite proportionnelle aux degats.</summary>
    public void Flash(float damage, float maxHealth)
    {
        // Normalise : un coup de 50hp sur 500 = 0.1 -> intensite 0.5 environ
        float ratio = Mathf.Clamp01(damage / Mathf.Max(1f, maxHealth));
        float bump = Mathf.Clamp(0.45f + ratio * 3f, 0.45f, 1f);
        m_Intensity = Mathf.Max(m_Intensity, bump);
    }

    private void Update()
    {
        if (m_Intensity > 0f)
            m_Intensity = Mathf.Max(0f, m_Intensity - m_FadePerSec * Time.deltaTime);
    }

    private void OnGUI()
    {
        if (m_Intensity <= 0.01f) return;

        // Largeur des bandes proportionnelle a l'intensite
        float t = m_Intensity;
        float wEdge = Mathf.Lerp(40f, 180f, t);
        float alpha = Mathf.Lerp(0.18f, 0.72f, t);

        Color old = GUI.color;
        GUI.color = new Color(0.85f, 0.05f, 0.05f, alpha);

        // Top
        GUI.DrawTexture(new Rect(0, 0, Screen.width, wEdge), m_Tex);
        // Bottom
        GUI.DrawTexture(new Rect(0, Screen.height - wEdge, Screen.width, wEdge), m_Tex);
        // Left
        GUI.DrawTexture(new Rect(0, 0, wEdge, Screen.height), m_Tex);
        // Right
        GUI.DrawTexture(new Rect(Screen.width - wEdge, 0, wEdge, Screen.height), m_Tex);

        GUI.color = old;
    }
}
