using Unity.Netcode;
using UnityEngine;

/// <summary>
/// IPSSI-WAR : trace la trajectoire prevue du prochain obus pour le joueur local.
/// Simulation physique simple (gravite Unity). Visible uniquement pour IsOwner.
/// Cree son propre LineRenderer si absent.
/// </summary>
public class TrajectoryPreview : MonoBehaviour
{
    [Header("Refs")]
    public TankShooting m_Shooting;
    public NetworkBehaviour m_NetRef; // pour IsOwner

    [Header("Simulation")]
    public int m_Steps = 50;
    public float m_StepTime = 0.04f;
    public float m_GroundY = 0.05f;
    public LayerMask m_HitMask = ~0;

    [Header("Look")]
    public Color m_StartColor = new Color(1f, 0.85f, 0.2f, 0.95f);
    public Color m_EndColor   = new Color(1f, 0.35f, 0.1f, 0.15f);
    public float m_Width = 0.18f;

    private LineRenderer m_Line;

    private void Awake()
    {
        m_Line = GetComponent<LineRenderer>();
        if (m_Line == null) m_Line = gameObject.AddComponent<LineRenderer>();
        m_Line.positionCount = 0;
        m_Line.widthMultiplier = m_Width;
        m_Line.material = new Material(Shader.Find("Sprites/Default"));
        m_Line.startColor = m_StartColor;
        m_Line.endColor   = m_EndColor;
        m_Line.numCapVertices = 4;
        m_Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_Line.receiveShadows = false;
    }

    private void LateUpdate()
    {
        if (m_Shooting == null || m_NetRef == null || !m_NetRef.IsOwner)
        {
            if (m_Line.positionCount != 0) m_Line.positionCount = 0;
            return;
        }
        if (!m_Shooting.m_ControlEnabled.Value || m_Shooting.m_FireTransform == null)
        {
            if (m_Line.positionCount != 0) m_Line.positionCount = 0;
            return;
        }

        // Force courante : si on charge, m_CurrentLaunchForce monte ; sinon = min
        float force = Mathf.Max(m_Shooting.CurrentLaunchForce, m_Shooting.MinLaunchForce);
        Simulate(m_Shooting.m_FireTransform.position, m_Shooting.m_FireTransform.forward * force);
    }

    private void Simulate(Vector3 origin, Vector3 velocity)
    {
        Vector3 g = Physics.gravity;
        Vector3 pos = origin;
        Vector3 vel = velocity;

        Vector3[] pts = new Vector3[m_Steps + 1];
        int count = 0;
        pts[count++] = pos;

        for (int i = 0; i < m_Steps; i++)
        {
            Vector3 nextPos = pos + vel * m_StepTime + 0.5f * g * (m_StepTime * m_StepTime);

            // Collision avec le decor
            if (Physics.Linecast(pos, nextPos, out RaycastHit hit, m_HitMask, QueryTriggerInteraction.Ignore))
            {
                pts[count++] = hit.point;
                break;
            }
            if (nextPos.y < m_GroundY)
            {
                // Interpole le point d'impact au sol
                float t = (m_GroundY - pos.y) / Mathf.Max(0.0001f, (nextPos.y - pos.y));
                pts[count++] = Vector3.Lerp(pos, nextPos, Mathf.Clamp01(t));
                break;
            }

            pts[count++] = nextPos;
            vel += g * m_StepTime;
            pos = nextPos;
        }

        m_Line.positionCount = count;
        for (int i = 0; i < count; i++) m_Line.SetPosition(i, pts[i]);
    }
}
