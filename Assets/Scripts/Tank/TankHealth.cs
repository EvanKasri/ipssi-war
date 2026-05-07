using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class TankHealth : NetworkBehaviour
{
    public float m_StartingHealth = 100f;
    public Slider m_Slider;
    public Image m_FillImage;
    public Color m_FullHealthColor = Color.green;
    public Color m_ZeroHealthColor = Color.red;
    public GameObject m_ExplosionPrefab;

    // La sant� est stock�e c�t� serveur et synchronis�e sur tous les clients.
    // IPSSI-WAR : valeur par defaut alignee sur m_StartingHealth pour eviter qu'un
    // client en train de rejoindre voie brievement un tank avec HP differents.
    private NetworkVariable<float> m_CurrentHealth = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private AudioSource m_ExplosionAudio;
    private ParticleSystem m_ExplosionParticles;
    private bool m_Dead;


    private void Awake()
    {
        // Chaque client cr�e ses propres effets de particules localement
        m_ExplosionParticles = Instantiate(m_ExplosionPrefab).GetComponent<ParticleSystem>();
        m_ExplosionAudio = m_ExplosionParticles.GetComponent<AudioSource>();
        m_ExplosionParticles.gameObject.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        // Initialise le slider
        if (m_Slider != null)
            m_Slider.maxValue = m_StartingHealth;

        // S'abonne aux changements de sant�
        m_CurrentHealth.OnValueChanged += OnHealthChanged;

        // Le serveur initialise la sant�
        if (IsServer)
        {
            m_CurrentHealth.Value = m_StartingHealth;
            m_Dead = false;
        }

        // Affiche la sant� initiale
        SetHealthUI();
    }

    public override void OnNetworkDespawn()
    {
        m_CurrentHealth.OnValueChanged -= OnHealthChanged;
    }

    // Appel� automatiquement sur tous les clients quand la sant� change
    private void OnHealthChanged(float oldValue, float newValue)
    {
        SetHealthUI();

        // IPSSI-WAR : vignette rouge UNIQUEMENT si le tank du joueur LOCAL prend
        // des degats. En BotMode, le host est techniquement IsOwner du tank-bot,
        // donc on filtre aussi les tanks pilotes par TankBotAI.
        bool isBot = GetComponent<TankBotAI>() != null;
        if (IsOwner && !isBot && newValue < oldValue && DamageVignette.Instance != null)
            DamageVignette.Instance.Flash(oldValue - newValue, m_StartingHealth);

        // Seulement le serveur d�cide de la mort (�vite les doubles appels)
        if (newValue <= 0f && !m_Dead && IsServer)
        {
            m_Dead = true;
            // Joue les effets de mort sur TOUS les clients
            ShowDeathEffectsClientRpc(transform.position);
        }
    }

    // Appel�e depuis ShellExplosion (seulement sur le serveur)
    public void TakeDamage(float amount)
    {
        if (!IsServer || m_Dead) return;
        m_CurrentHealth.Value = Mathf.Max(0f, m_CurrentHealth.Value - amount);
    }

    // Remet le tank en �tat pour un nouveau round
    public void ResetTank()
    {
        if (!IsServer) return;
        m_Dead = false;
        m_CurrentHealth.Value = m_StartingHealth;
    }

    public bool IsDead() => m_Dead;

    // IPSSI-WAR : expose la sante courante (lue par HealthHud sans reflection,
    // robuste aux strippings IL2CPP des builds).
    public float CurrentHealth => m_CurrentHealth.Value;

    // IPSSI-WAR : soin via power-up (cap à m_StartingHealth)
    public void HealServer(float amount)
    {
        if (!IsServer || m_Dead) return;
        m_CurrentHealth.Value = Mathf.Min(m_StartingHealth, m_CurrentHealth.Value + amount);
    }

    private void SetHealthUI()
    {
        if (m_Slider == null) return;
        // IPSSI-WAR : on force maxValue a chaque update pour qu'un client qui a
        // initialise son slider trop tot (avant sync NetworkVariable) ne reste pas
        // sur une mauvaise echelle. Garantit que tous les tanks affichent la meme
        // jauge max en multi.
        m_Slider.maxValue = m_StartingHealth;
        m_Slider.value = m_CurrentHealth.Value;
        m_FillImage.color = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor,
            m_CurrentHealth.Value / m_StartingHealth);
    }

    // Ex�cut�e sur tous les clients pour afficher l'explosion et d�sactiver le tank
    [ClientRpc]
    private void ShowDeathEffectsClientRpc(Vector3 position)
    {
        m_ExplosionParticles.transform.position = position;
        m_ExplosionParticles.gameObject.SetActive(true);
        m_ExplosionParticles.Play();
        m_ExplosionAudio.Play();
        gameObject.SetActive(false);
    }
}
