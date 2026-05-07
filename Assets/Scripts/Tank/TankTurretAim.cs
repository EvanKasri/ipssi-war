using Unity.Netcode;
using UnityEngine;

/// <summary>
/// IPSSI-WAR : la tourelle suit le yaw de la camera FPS.
/// Plus de raycast cursor->ground (qui partait en spin si la souris descendait).
/// </summary>
public class TankTurretAim : NetworkBehaviour
{
    [Tooltip("Transform de la tourelle. Si vide, on prend le parent du FireTransform.")]
    public Transform m_Turret;

    [Tooltip("FireTransform du TankShooting (fallback pour trouver la tourelle).")]
    public Transform m_FireTransform;

    public float m_RotationSpeed = 720f;

    // IPSSI-WAR : tank pilote par l'IA (la tourelle est dirigee par TankBotAI).
    [HideInInspector] public bool m_IsBot;

    private CameraControl m_CamControl;

    private void Start()
    {
        if (m_Turret == null)
        {
            var shooting = GetComponent<TankShooting>();
            if (shooting != null && shooting.m_FireTransform != null)
                m_FireTransform = shooting.m_FireTransform;
            if (m_FireTransform != null && m_FireTransform.parent != null)
                m_Turret = m_FireTransform.parent;
        }
    }

    private void Update()
    {
        if (!IsOwner || m_Turret == null) return;
        // Bot : tourelle pilotee par TankBotAI.
        if (m_IsBot) return;
        if (m_CamControl == null) m_CamControl = FindObjectOfType<CameraControl>();
        if (m_CamControl == null) return;

        // Tourelle horizontale = yaw camera
        Quaternion target = Quaternion.Euler(0f, m_CamControl.Yaw, 0f);
        m_Turret.rotation = Quaternion.RotateTowards(m_Turret.rotation, target,
            m_RotationSpeed * Time.deltaTime);
    }
}
