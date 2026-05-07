using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class TankMovement : NetworkBehaviour
{
    public float m_Speed = 12f;
    public float m_TurnSpeed = 180f;
    public AudioSource m_MovementAudio;
    public AudioClip m_EngineIdling;
    public AudioClip m_EngineDriving;
    public float m_PitchRange = 0.2f;

    // Le serveur contr�le si les joueurs peuvent bouger (synch� automatiquement)
    public NetworkVariable<bool> m_ControlEnabled = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // IPSSI-WAR : si true, ce tank est pilote par TankBotAI (cote serveur).
    // Bypass la lecture du clavier dans Update; les valeurs d'input sont ecrites par l'IA.
    [HideInInspector] public bool m_IsBot;

    public float MovementInputValue { get { return m_MovementInputValue; } set { m_MovementInputValue = value; } }
    public float TurnInputValue     { get { return m_TurnInputValue; }     set { m_TurnInputValue = value; } }

    private Rigidbody m_Rigidbody;
    private float m_MovementInputValue;
    private float m_TurnInputValue;
    private float m_OriginalPitch;
    private float m_SpeedMultiplier = 1f;
    private Coroutine m_SpeedBoostCoroutine;


    private void Awake()
    {
        m_Rigidbody = GetComponent<Rigidbody>();
    }

    private void Start()
    {
        m_OriginalPitch = m_MovementAudio.pitch;
    }

    private void OnEnable()
    {
        m_Rigidbody.isKinematic = false;
        m_MovementInputValue = 0f;
        m_TurnInputValue = 0f;
    }

    private void OnDisable()
    {
        m_Rigidbody.isKinematic = true;
    }

    private void Update()
    {
        // Seulement le propri�taire du tank (le vrai joueur) peut le contr�ler
        if (!IsOwner || !m_ControlEnabled.Value) return;
        // Bot : input pilote par TankBotAI, on saute juste la lecture clavier (et on joue le son moteur).
        if (m_IsBot) { EngineAudio(); return; }
        // IPSSI-WAR : bloque les inputs quand le panneau reglages est ouvert
        if (SensitivitySettingsUI.IsOpen) { m_MovementInputValue = 0f; m_TurnInputValue = 0f; return; }

        // ZQSD (AZERTY) + WASD (QWERTY) + fleches directionnelles.
        // /!\ Unity KeyCode = position PHYSIQUE sur QWERTY :
        //   AZERTY-Z -> KeyCode.W ; AZERTY-Q -> KeyCode.A ; AZERTY-D -> KeyCode.D
        //   QWERTY-W -> KeyCode.W ; QWERTY-A -> KeyCode.A ; QWERTY-D -> KeyCode.D
        // Donc KeyCode.A = gauche, KeyCode.D = droite dans les deux cas.
        float forward  = (Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    ? 1f : 0f;
        float backward = (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  ? 1f : 0f;
        float right    = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) ? 1f : 0f;
        float left     = (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow))  ? 1f : 0f;
        m_MovementInputValue = forward - backward;
        m_TurnInputValue     = right - left;
        EngineAudio();
    }

    private void EngineAudio()
    {
        if (Mathf.Abs(m_MovementInputValue) < 0.1f && Mathf.Abs(m_TurnInputValue) < 0.1f)
        {
            if (m_MovementAudio.clip == m_EngineDriving)
            {
                m_MovementAudio.clip = m_EngineIdling;
                m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                m_MovementAudio.Play();
            }
        }
        else
        {
            if (m_MovementAudio.clip == m_EngineIdling)
            {
                m_MovementAudio.clip = m_EngineDriving;
                m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                m_MovementAudio.Play();
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner || !m_ControlEnabled.Value) return;
        Move();
        Turn();
    }

    private void Move()
    {
        // Composante avant/arrière (Z/S)
        Vector3 forward = transform.forward * m_MovementInputValue;

        // IPSSI-WAR : Q/D = strafe lateral.
        //  - Avec Z/S : lateral attenue (0.55) -> mouvement diagonal naturel.
        //  - Sans Z/S : strafe pur (full speed) pour aller completement gauche/droite.
        bool hasFwd = Mathf.Abs(m_MovementInputValue) > 0.1f;
        float lateralFactor = hasFwd ? 0.55f : 1f;
        Vector3 lateral = transform.right * m_TurnInputValue * lateralFactor;

        Vector3 dir = forward + lateral;
        if (dir.sqrMagnitude < 0.001f) return;

        // Vitesse basee sur l'input dominant (avant/arriere ou strafe).
        float intensity = Mathf.Max(Mathf.Abs(m_MovementInputValue),
                                    Mathf.Abs(m_TurnInputValue) * lateralFactor);
        float speed = intensity * m_Speed * m_SpeedMultiplier * Time.deltaTime;
        m_Rigidbody.MovePosition(m_Rigidbody.position + dir.normalized * speed);
    }

    // IPSSI-WAR : appelé par le serveur depuis PowerUp
    public void ApplySpeedBoostServer(float multiplier, float duration)
    {
        if (!IsServer) return;
        multiplier = Mathf.Clamp(multiplier, 1f, 3f);
        if (m_SpeedBoostCoroutine != null)
            StopCoroutine(m_SpeedBoostCoroutine);
        m_SpeedBoostCoroutine = StartCoroutine(SpeedBoostRoutine(multiplier, duration));
    }

    private IEnumerator SpeedBoostRoutine(float multiplier, float duration)
    {
        m_SpeedMultiplier = multiplier;
        yield return new WaitForSeconds(duration);
        m_SpeedMultiplier = 1f;
        m_SpeedBoostCoroutine = null;
    }

    // Appelé par le serveur pour téléporter le tank côté owner (ClientNetworkTransform = owner-authoritative)
    [ClientRpc]
    public void TeleportClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams rpcParams = default)
    {
        transform.position = position;
        transform.rotation = rotation;
        if (m_Rigidbody != null)
        {
            m_Rigidbody.velocity        = Vector3.zero;
            m_Rigidbody.angularVelocity = Vector3.zero;
        }
    }

    public void ResetSpeedBoostServer()
    {
        if (!IsServer) return;
        if (m_SpeedBoostCoroutine != null)
        {
            StopCoroutine(m_SpeedBoostCoroutine);
            m_SpeedBoostCoroutine = null;
        }
        m_SpeedMultiplier = 1f;
    }

    private void Turn()
    {
        // IPSSI-WAR : rotation du chassis uniquement en avancant/reculant.
        // Quand seul Q ou D est presse, on strafe (Move) sans tourner.
        if (Mathf.Abs(m_MovementInputValue) < 0.1f) return;
        float turn = m_TurnInputValue * m_TurnSpeed * Time.deltaTime;
        Quaternion turnRotation = Quaternion.Euler(0f, turn, 0f);
        m_Rigidbody.MoveRotation(m_Rigidbody.rotation * turnRotation);
    }
}
