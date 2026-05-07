using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IPSSI-WAR : HUD en haut a gauche affichant la barre de vie de tous les
/// joueurs (le tank local en premier et surligne, les autres ensuite).
/// Utilise TankHealth.CurrentHealth (propriete publique, pas de reflection)
/// pour rester compatible IL2CPP en build.
/// </summary>
public class HealthHud : MonoBehaviour
{
    public static HealthHud Instance { get; private set; }

    private Texture2D m_TexWhite;
    private GUIStyle m_LabelStyle;
    private GUIStyle m_TitleStyle;
    private float m_RefreshAt;
    private TankHealth[] m_Cache = new TankHealth[0];

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        m_TexWhite = new Texture2D(1, 1);
        m_TexWhite.SetPixel(0, 0, Color.white);
        m_TexWhite.Apply();

        Debug.Log("[HealthHud] Awake — singleton actif.");
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // Rafraichit la liste des tanks toutes les 0.5s.
        // includeInactive=true pour aussi voir les tanks morts.
        if (Time.unscaledTime >= m_RefreshAt)
        {
            m_RefreshAt = Time.unscaledTime + 0.5f;
            m_Cache = FindObjectsOfType<TankHealth>(true);
        }
    }

    private void OnGUI()
    {
        if (m_LabelStyle == null)
        {
            m_LabelStyle = new GUIStyle(GUI.skin.label);
            m_LabelStyle.fontSize = 12;
            m_LabelStyle.fontStyle = FontStyle.Bold;
            m_LabelStyle.normal.textColor = Color.white;
        }
        if (m_TitleStyle == null)
        {
            m_TitleStyle = new GUIStyle(GUI.skin.label);
            m_TitleStyle.fontSize = 11;
            m_TitleStyle.fontStyle = FontStyle.Bold;
            m_TitleStyle.normal.textColor = new Color(1f, 0.83f, 0f, 1f);
        }

        // Tri : tank local en premier, puis les autres.
        var ordered = new List<TankHealth>(m_Cache.Length);
        TankHealth local = null;
        for (int i = 0; i < m_Cache.Length; i++)
        {
            if (m_Cache[i] == null) continue;
            if (m_Cache[i].IsOwner && local == null) local = m_Cache[i];
            else ordered.Add(m_Cache[i]);
        }
        if (local != null) ordered.Insert(0, local);

        const float panelX = 12f;
        const float panelY = 12f;
        const float rowH = 28f;
        const float rowW = 230f;
        const float gap = 6f;
        const float titleH = 18f;

        int rows = ordered.Count;
        float panelH = titleH + 4f + Mathf.Max(1, rows) * (rowH + gap) + 6f;

        // Panneau de fond (toujours visible : confirme que le HUD est charge).
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(panelX - 8, panelY - 6, rowW + 16, panelH), m_TexWhite);
        GUI.color = old;

        // Titre
        GUI.Label(new Rect(panelX, panelY, rowW, titleH), "VIES DES JOUEURS", m_TitleStyle);

        if (rows == 0)
        {
            GUI.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            GUI.Label(new Rect(panelX, panelY + titleH + 4, rowW, rowH),
                "(en attente des tanks...)", m_LabelStyle);
            GUI.color = old;
            return;
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            TankHealth t = ordered[i];
            float y = panelY + titleH + 4f + i * (rowH + gap);

            float cur = t.CurrentHealth;
            float max = Mathf.Max(1f, t.m_StartingHealth);
            float ratio = Mathf.Clamp01(cur / max);

            Color tankCol = GetTankColor(t);

            // Fond de la barre
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(panelX, y, rowW, rowH), m_TexWhite);

            // Remplissage de la barre (lerp rouge -> vert selon HP)
            Color fill = Color.Lerp(new Color(0.85f, 0.1f, 0.1f),
                                     new Color(0.15f, 0.85f, 0.2f), ratio);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(panelX + 2, y + 2, (rowW - 4) * ratio, rowH - 4), m_TexWhite);

            // Pastille couleur du tank
            GUI.color = tankCol;
            GUI.DrawTexture(new Rect(panelX + 4, y + 6, 16, 16), m_TexWhite);

            // Bord pour le tank local
            if (t == local)
            {
                GUI.color = new Color(1f, 0.83f, 0f, 0.95f);
                GUI.DrawTexture(new Rect(panelX, y, rowW, 2), m_TexWhite);
                GUI.DrawTexture(new Rect(panelX, y + rowH - 2, rowW, 2), m_TexWhite);
                GUI.DrawTexture(new Rect(panelX, y, 2, rowH), m_TexWhite);
                GUI.DrawTexture(new Rect(panelX + rowW - 2, y, 2, rowH), m_TexWhite);
            }

            GUI.color = Color.white;
            string name = (t == local ? "VOUS" : $"P{t.OwnerClientId}");
            GUI.Label(new Rect(panelX + 26, y + 4, rowW - 30, rowH - 8),
                $"{name}  -  {Mathf.CeilToInt(cur)} / {Mathf.CeilToInt(max)}", m_LabelStyle);
        }

        GUI.color = old;
    }

    private static Color GetTankColor(TankHealth t)
    {
        var renderers = t.GetComponentsInChildren<MeshRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].material != null)
                return renderers[i].material.color;
        }
        return Color.white;
    }
}
