using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class TankShooting : NetworkBehaviour
{
    public Rigidbody m_Shell;                   // Prefab du projectile (doit avoir NetworkObject)
    public Transform m_FireTransform;
    public Slider m_AimSlider;
    public AudioSource m_ShootingAudio;
    public AudioClip m_ChargingClip;
    public AudioClip m_FireClip;
    public float m_MinLaunchForce = 15f;
    public float m_MaxLaunchForce = 30f;
    public float m_MaxChargeTime = 0.75f;
    // IPSSI-WAR : delai minimum entre 2 tirs (anti-spam).
    public float m_FireCooldown = 0.6f;

    // Le serveur contr�le si le joueur peut tirer (synch� automatiquement)
    public NetworkVariable<bool> m_ControlEnabled = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 1f = normal ; valeur plus basse = charge plus rapide
    public NetworkVariable<float> m_RapidFireChargeScale = new NetworkVariable<float>(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // 1f = degats de base des obus ; >1 avec power-up
    public NetworkVariable<float> m_DamageShellMultiplier = new NetworkVariable<float>(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // IPSSI-WAR : tank pilote par l'IA (skip input clavier/souris).
    [HideInInspector] public bool m_IsBot;

    private float m_CurrentLaunchForce;
    private float m_ChargeSpeed;
    private bool m_Fired;
    private Coroutine m_RapidFireCoroutine;
    private Coroutine m_DamageBoostCoroutine;
    // IPSSI-WAR : timestamp avant lequel on ne peut pas re-tirer.
    private float m_NextFireTime;

    // Pour la trajectory preview (lue par TrajectoryPreview)
    public float CurrentLaunchForce => m_CurrentLaunchForce;
    public float MinLaunchForce => m_MinLaunchForce;
    public float MaxLaunchForce => m_MaxLaunchForce;


    private void OnEnable()
    {
        m_CurrentLaunchForce = m_MinLaunchForce;
        if (m_AimSlider != null)
            m_AimSlider.value = m_MinLaunchForce;
    }

    private void Start()
    {
        RefreshChargeSpeed();
    }

    private void RefreshChargeSpeed()
    {
        float scale = m_RapidFireChargeScale.Value;
        float effChargeTime = m_MaxChargeTime * Mathf.Clamp(scale, 0.12f, 1.5f);
        m_ChargeSpeed = (m_MaxLaunchForce - m_MinLaunchForce) / effChargeTime;
    }

    private void Update()
    {
        // Seulement le propri�taire du tank peut tirer
        if (!IsOwner || !m_ControlEnabled.Value) return;
        // Bot : tir gere par TankBotAI cote serveur.
        if (m_IsBot) return;
        // IPSSI-WAR : bloque le tir quand le panneau reglages est ouvert
        if (SensitivitySettingsUI.IsOpen) return;

        RefreshChargeSpeed();

        if (m_AimSlider != null)
            m_AimSlider.value = m_MinLaunchForce;

        bool fireDown = Input.GetButtonDown("Fire1") || Input.GetMouseButtonDown(0);
        bool fireHeld = Input.GetButton("Fire1") || Input.GetMouseButton(0);
        bool fireUp = Input.GetButtonUp("Fire1") || Input.GetMouseButtonUp(0);

        // IPSSI-WAR : cooldown anti-spam, pas de charge ni de tir tant qu'on n'a
        // pas attendu m_FireCooldown apres le dernier coup.
        bool onCooldown = Time.time < m_NextFireTime;
        if (onCooldown)
        {
            // Stoppe la charge en cours si l'utilisateur appuie pendant le cooldown.
            if (!m_Fired) m_CurrentLaunchForce = m_MinLaunchForce;
            m_Fired = true;
            if (m_AimSlider != null) m_AimSlider.value = m_MinLaunchForce;
            return;
        }

        if (m_CurrentLaunchForce >= m_MaxLaunchForce && !m_Fired)
        {
            m_CurrentLaunchForce = m_MaxLaunchForce;
            m_Fired = true;
            // Demande au serveur de tirer
            RequestFireServerRpc(m_FireTransform.position, m_FireTransform.forward, m_CurrentLaunchForce);
            m_CurrentLaunchForce = m_MinLaunchForce;
            m_NextFireTime = Time.time + m_FireCooldown;
        }
        else if (fireDown)
        {
            m_Fired = false;
            m_CurrentLaunchForce = m_MinLaunchForce;
            m_ShootingAudio.clip = m_ChargingClip;
            m_ShootingAudio.Play();
        }
        else if (fireHeld && !m_Fired)
        {
            m_CurrentLaunchForce += m_ChargeSpeed * Time.deltaTime;
            if (m_AimSlider != null)
                m_AimSlider.value = m_CurrentLaunchForce;
        }
        else if (fireUp && !m_Fired)
        {
            // IPSSI-WAR : en mode One Shot, on n'autorise PAS le tir au release
            // (charge incomplete). Seul le tir auto a charge max declenche.
            if (LobbyConfig.OneShotMode)
            {
                m_CurrentLaunchForce = m_MinLaunchForce;
                if (m_AimSlider != null) m_AimSlider.value = m_MinLaunchForce;
            }
            else
            {
                m_Fired = true;
                // Demande au serveur de tirer
                RequestFireServerRpc(m_FireTransform.position, m_FireTransform.forward, m_CurrentLaunchForce);
                m_CurrentLaunchForce = m_MinLaunchForce;
                m_NextFireTime = Time.time + m_FireCooldown;
            }
        }
    }

    // Appel� par le client, ex�cut� sur le serveur
    [ServerRpc]
    private void RequestFireServerRpc(Vector3 spawnPosition, Vector3 fireDirection, float launchForce,
        ServerRpcParams rpcParams = default)
    {
        // Le serveur cr�e et spawn le projectile pour tout le monde
        Rigidbody shellInstance = Instantiate(m_Shell, spawnPosition, Quaternion.LookRotation(fireDirection));
        ShellExplosion shellEx = shellInstance.GetComponent<ShellExplosion>();
        if (shellEx != null)
        {
            shellEx.SetDamageMultiplier(m_DamageShellMultiplier.Value);
            // IPSSI-WAR : memorise le tireur pour le hit marker (clientId + NetId
            // du tank tireur, necessaire pour distinguer joueur/bot quand l'host
            // possede les deux tanks).
            shellEx.SetShooter(rpcParams.Receive.SenderClientId, NetworkObjectId);
        }

        shellInstance.GetComponent<NetworkObject>().Spawn(true);
        shellInstance.velocity = launchForce * fireDirection;

        // Joue le son de tir sur toutes les machines
        PlayFireAudioClientRpc();
    }

    // IPSSI-WAR : tir du bot cote serveur (sans passer par ServerRpc).
    public void FireFromBotServer(Vector3 spawnPosition, Vector3 fireDirection, float launchForce)
    {
        if (!IsServer) return;
        Rigidbody shellInstance = Instantiate(m_Shell, spawnPosition, Quaternion.LookRotation(fireDirection));
        ShellExplosion shellEx = shellInstance.GetComponent<ShellExplosion>();
        if (shellEx != null)
        {
            shellEx.SetDamageMultiplier(m_DamageShellMultiplier.Value);
            shellEx.SetShooter(OwnerClientId, NetworkObjectId);
        }
        shellInstance.GetComponent<NetworkObject>().Spawn(true);
        shellInstance.velocity = launchForce * fireDirection;
        PlayFireAudioClientRpc();
    }

    // Appel� par le serveur, ex�cut� sur tous les clients
    [ClientRpc]
    private void PlayFireAudioClientRpc()
    {
        m_ShootingAudio.clip = m_FireClip;
        m_ShootingAudio.Play();
    }

    // IPSSI-WAR : power-up cadence (chargeTimeScale typ. 0.35-0.8)
    public void ApplyRapidFireServer(float chargeTimeScale, float duration)
    {
        if (!IsServer) return;
        chargeTimeScale = Mathf.Clamp(chargeTimeScale, 0.12f, 1f);
        if (m_RapidFireCoroutine != null)
            StopCoroutine(m_RapidFireCoroutine);
        m_RapidFireCoroutine = StartCoroutine(RapidFireRoutine(chargeTimeScale, duration));
    }

    private IEnumerator RapidFireRoutine(float scale, float duration)
    {
        m_RapidFireChargeScale.Value = scale;
        yield return new WaitForSeconds(duration);
        m_RapidFireChargeScale.Value = 1f;
        m_RapidFireCoroutine = null;
    }

    // IPSSI-WAR : power-up dégâts (multiplicateur typ. 1.4-2)
    public void ApplyDamageBoostServer(float damageMultiplier, float duration)
    {
        if (!IsServer) return;
        damageMultiplier = Mathf.Clamp(damageMultiplier, 1f, 3f);
        if (m_DamageBoostCoroutine != null)
            StopCoroutine(m_DamageBoostCoroutine);
        m_DamageBoostCoroutine = StartCoroutine(DamageBoostRoutine(damageMultiplier, duration));
    }

    private IEnumerator DamageBoostRoutine(float mult, float duration)
    {
        m_DamageShellMultiplier.Value = mult;
        yield return new WaitForSeconds(duration);
        m_DamageShellMultiplier.Value = 1f;
        m_DamageBoostCoroutine = null;
    }

    public void ResetPowerUpBuffsServer()
    {
        if (!IsServer) return;
        if (m_RapidFireCoroutine != null)
        {
            StopCoroutine(m_RapidFireCoroutine);
            m_RapidFireCoroutine = null;
        }
        if (m_DamageBoostCoroutine != null)
        {
            StopCoroutine(m_DamageBoostCoroutine);
            m_DamageBoostCoroutine = null;
        }
        m_RapidFireChargeScale.Value = 1f;
        m_DamageShellMultiplier.Value = 1f;
    }
}
