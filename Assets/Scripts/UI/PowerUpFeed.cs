using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Affichage bref d'un power-up ramassé (tous clients). IPSSI-WAR
/// </summary>
public class PowerUpFeed : MonoBehaviour
{
    public static PowerUpFeed Instance { get; private set; }

    [Tooltip("Laissez vide si le texte est sur le même GameObject (GetComponent).")]
    public Text m_Text;
    public float m_DisplaySeconds = 2.2f;

    private void Awake()
    {
        Instance = this;
        if (m_Text == null)
            m_Text = GetComponent<Text>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Announce(PowerUp.Type type, ulong ownerClientId)
    {
        if (m_Text == null) return;
        m_Text.text = $"ipssi-war — joueur {ownerClientId} : {Label(type)}";
        CancelInvoke(nameof(ClearText));
        Invoke(nameof(ClearText), m_DisplaySeconds);
    }

    private void ClearText()
    {
        if (m_Text != null)
            m_Text.text = "";
    }

    private static string Label(PowerUp.Type t)
    {
        switch (t)
        {
            case PowerUp.Type.Heal: return "soin";
            case PowerUp.Type.SpeedBoost: return "vitesse";
            case PowerUp.Type.RapidFire: return "cadence";
            case PowerUp.Type.Damage: return "degats";
            default: return t.ToString();
        }
    }
}
