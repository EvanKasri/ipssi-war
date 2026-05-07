using Unity.Netcode;
using UnityEngine;

/// <summary>
/// IPSSI-WAR : IA pour un tank ennemi (mode 1 joueur / 1v1 contre bot).
/// MonoBehaviour (pas NetworkBehaviour) : ajoute dynamiquement APRES Spawn,
/// donc on s'appuie sur NetworkManager.Singleton.IsServer pour gater la logique.
///
/// Ameliorations :
///   - Estimation de la vitesse de la cible (lissee) -> preshot.
///   - Aim predit via convergence iterative (t = dist / shellSpeed).
///   - Erreur d'aim aleatoire pseudo-gaussienne, refresh par intervalles.
///   - Strafe aleatoire periodique en range d'engagement.
///   - Inputs lisses (Mathf.Lerp) pour eviter le suivi instantane "robot".
///   - Cooldown de tir randomise (anti-pattern previsible).
/// </summary>
[RequireComponent(typeof(TankMovement))]
[RequireComponent(typeof(TankShooting))]
public class TankBotAI : MonoBehaviour
{
    [Header("Comportement")]
    public float m_EngageDistance  = 18f;
    public float m_AlignDeg        = 5f;
    public float m_FireCooldownMin = 1.3f;
    public float m_FireCooldownMax = 2.0f;
    public float m_FireForce       = 24f;   // doit matcher la vitesse reelle de l'obus du bot
    public float m_AimNoiseDeg     = 4f;
    public float m_TurretRotSpeed  = 110f;
    public float m_InputSmooth     = 4f;    // plus haut = bot plus reactif
    public float m_StrafeChance    = 0.6f;  // proba de strafer en engagement

    private TankMovement m_Movement;
    private TankShooting m_Shooting;
    private Transform    m_Turret;
    private Transform    m_FireTransform;
    private float        m_FireTimer;
    private Transform    m_Target;
    private float        m_RetargetTimer;

    // Estimation vitesse cible
    private Vector3 m_LastTargetPos;
    private Vector3 m_TargetVelocity;
    private bool    m_HasLastTargetPos;

    // Erreur d'aim periodique
    private float m_AimErrorTimer;
    private float m_AimErrorYaw;

    // Strafe
    private float m_StrafeTimer;
    private float m_StrafeDirection;

    // Inputs lisses
    private float m_TargetMoveInput;
    private float m_TargetTurnInput;
    private float m_CurrentMoveInput;
    private float m_CurrentTurnInput;

    // CRITIQUE : Awake fire des l'AddComponent. On marque les composants tank
    // comme "bot" AVANT que TankMovement.Update lise le clavier.
    private void Awake()
    {
        m_Movement = GetComponent<TankMovement>();
        m_Shooting = GetComponent<TankShooting>();

        if (m_Movement != null)
        {
            m_Movement.m_IsBot = true;
            m_Movement.MovementInputValue = 0f;
            m_Movement.TurnInputValue     = 0f;
        }
        if (m_Shooting != null) m_Shooting.m_IsBot = true;

        var turretAim = GetComponent<TankTurretAim>();
        if (turretAim != null)
        {
            turretAim.m_IsBot = true;
            m_Turret = turretAim.m_Turret;
        }
        if (m_Shooting != null)
            m_FireTransform = m_Shooting.m_FireTransform;
        if (m_Turret == null && m_FireTransform != null && m_FireTransform.parent != null)
            m_Turret = m_FireTransform.parent;

        // IPSSI-WAR : en mode One Shot, le joueur ne peut tirer qu'a charge max (force 30).
        // Le bot doit donc rester loin (sinon le joueur tire par-dessus) et tirer aussi
        // a force 30 pour un duel longue portee equilibre. Cooldown un peu plus long
        // pour laisser le temps de bouger / charger.
        if (LobbyConfig.OneShotMode)
        {
            m_EngageDistance  = 35f;
            m_FireForce       = 30f;
            m_FireCooldownMin = 1.8f;
            m_FireCooldownMax = 2.6f;
        }

        // Premier cooldown randomise pour eviter qu'apparait + tire en synchro avec un autre bot.
        m_FireTimer = Random.Range(m_FireCooldownMin, m_FireCooldownMax);

        Debug.Log($"[TankBotAI] Awake : flags m_IsBot=true poses sur Movement/Shooting/TurretAim. Tank='{name}'. OneShot={LobbyConfig.OneShotMode}.");
    }

    private void Update()
    {
        // Seul le serveur (host) drive le bot.
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (m_Movement == null || !m_Movement.m_ControlEnabled.Value) return;

        float dt = Time.deltaTime;

        m_RetargetTimer -= dt;
        if (m_Target == null || m_RetargetTimer <= 0f)
        {
            m_Target = FindClosestPlayerTank();
            m_RetargetTimer = 1.0f;
            m_HasLastTargetPos = false;
            m_TargetVelocity   = Vector3.zero;
        }
        if (m_Target == null)
        {
            m_Movement.MovementInputValue = 0f;
            m_Movement.TurnInputValue     = 0f;
            return;
        }

        // --- Estimation vitesse cible (lissee) ---
        Vector3 targetPos = m_Target.position;
        if (m_HasLastTargetPos && dt > 0.0001f)
        {
            Vector3 instantVel = (targetPos - m_LastTargetPos) / dt;
            // Lissage : evite les pics quand le tank tourne brusquement.
            m_TargetVelocity = Vector3.Lerp(m_TargetVelocity, instantVel, 0.25f);
        }
        m_LastTargetPos = targetPos;
        m_HasLastTargetPos = true;

        Vector3 toTarget = targetPos - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        // --- Decision strafe periodique ---
        m_StrafeTimer -= dt;
        if (m_StrafeTimer <= 0f)
        {
            m_StrafeTimer = Random.Range(1.0f, 2.5f);
            bool engaging = dist < m_EngageDistance * 1.4f && dist > m_EngageDistance * 0.5f;
            if (engaging && Random.value < m_StrafeChance)
                m_StrafeDirection = Random.value < 0.5f ? -1f : 1f;
            else
                m_StrafeDirection = 0f;
        }

        // --- Calcul des inputs cible (avant lissage) ---
        float yawDelta = Vector3.SignedAngle(transform.forward, toTarget.normalized, Vector3.up);
        m_TargetTurnInput = Mathf.Clamp(yawDelta / 25f, -1f, 1f);

        if (dist > m_EngageDistance)
            m_TargetMoveInput =  1f;
        else if (dist < m_EngageDistance * 0.55f)
            m_TargetMoveInput = -0.6f;
        else
            m_TargetMoveInput =  0f;

        // Strafe pur : MovementInput=0 + TurnInput = direction (Q/D), uniquement
        // quand on n'a pas besoin d'avancer/reculer ET qu'on est globalement aligne.
        if (Mathf.Abs(m_TargetMoveInput) < 0.05f
            && Mathf.Abs(m_StrafeDirection) > 0.5f
            && Mathf.Abs(yawDelta) < 35f)
        {
            m_TargetMoveInput = 0f;
            m_TargetTurnInput = m_StrafeDirection;
        }

        // --- Lissage des inputs (humanise) ---
        float smooth = 1f - Mathf.Exp(-m_InputSmooth * dt);
        m_CurrentMoveInput = Mathf.Lerp(m_CurrentMoveInput, m_TargetMoveInput, smooth);
        m_CurrentTurnInput = Mathf.Lerp(m_CurrentTurnInput, m_TargetTurnInput, smooth);
        m_Movement.MovementInputValue = m_CurrentMoveInput;
        m_Movement.TurnInputValue     = m_CurrentTurnInput;

        // --- Aim predit + erreur aleatoire ---
        Vector3 shooterPos = m_FireTransform != null ? m_FireTransform.position : transform.position;
        Vector3 aimPoint = PredictAimPoint(shooterPos, targetPos, m_TargetVelocity, m_FireForce);
        Vector3 aimDir = aimPoint - shooterPos;
        aimDir.y = 0f;

        if (m_Turret != null && aimDir.sqrMagnitude > 0.01f)
        {
            // Refresh erreur d'aim par intervalles (pseudo-gaussien : somme de 3 uniformes).
            m_AimErrorTimer -= dt;
            if (m_AimErrorTimer <= 0f)
            {
                m_AimErrorTimer = Random.Range(0.4f, 1.0f);
                float u = (Random.value + Random.value + Random.value) / 3f - 0.5f;
                m_AimErrorYaw = u * 2f * m_AimNoiseDeg;
            }
            Quaternion baseRot = Quaternion.LookRotation(aimDir.normalized);
            Quaternion target = Quaternion.AngleAxis(m_AimErrorYaw, Vector3.up) * baseRot;
            target = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            m_Turret.rotation = Quaternion.RotateTowards(m_Turret.rotation, target,
                m_TurretRotSpeed * dt);
        }

        // --- Tir ---
        m_FireTimer -= dt;
        if (m_FireTimer <= 0f && m_FireTransform != null && m_Shooting != null
            && aimDir.sqrMagnitude > 0.01f)
        {
            Vector3 fireDir = m_FireTransform.forward;
            Vector3 fdFlat = new Vector3(fireDir.x, 0f, fireDir.z);
            float angleToAim = Vector3.Angle(fdFlat, aimDir);
            if (angleToAim < m_AlignDeg && dist < m_EngageDistance * 2.5f)
            {
                m_Shooting.FireFromBotServer(m_FireTransform.position, fireDir, m_FireForce);
                m_FireTimer = Random.Range(m_FireCooldownMin, m_FireCooldownMax);
            }
        }
    }

    // IPSSI-WAR : predit ou tirer en convergence iterative.
    // Resoud t = dist(shooter, target + vel*t) / shellSpeed (4 iterations suffisent).
    private static Vector3 PredictAimPoint(Vector3 shooterPos, Vector3 targetPos,
                                          Vector3 targetVel, float shellSpeed)
    {
        if (shellSpeed < 0.1f) return targetPos;
        Vector3 predicted = targetPos;
        for (int i = 0; i < 4; i++)
        {
            float d = Vector3.Distance(shooterPos, predicted);
            float t = d / shellSpeed;
            predicted = targetPos + targetVel * t;
        }
        return predicted;
    }

    private Transform FindClosestPlayerTank()
    {
        TankHealth[] all = FindObjectsOfType<TankHealth>();
        Transform best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] == null || all[i].gameObject == gameObject) continue;
            if (all[i].GetComponent<TankBotAI>() != null) continue;
            if (!all[i].gameObject.activeInHierarchy) continue;
            float d = (all[i].transform.position - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = all[i].transform;
            }
        }
        return best;
    }
}
