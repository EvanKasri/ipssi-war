using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif

// IPSSI-WAR : caméra 3eme personne FPS-style.
// Le curseur est verrouille au centre, le mouvement souris fait tourner la cam (yaw + pitch).
// La tourelle du tank suit le yaw de la cam (voir TankTurretAim).
public class CameraControl : MonoBehaviour
{
    [Header("Suivi 3eme personne")]
    public Vector3 m_PivotOffset = new Vector3(0f, 4.5f, 0f);
    public float m_Distance = 9f;
    public float m_FollowSmooth = 14f;
    public float m_MinHeight = 1.2f;

    [Header("Limites verticales")]
    public float m_PitchMin = -15f;
    // IPSSI-WAR : pitch max limite a 35° pour empecher la vue plongeante "carte
    // entiere" (top-down) et garder un feeling 3eme personne realiste.
    public float m_PitchMax = 35f;
    public float m_DefaultPitch = 18f;

    [Header("Camera")]
    public float m_FieldOfView = 60f;

    // Compatibilite ancien code
    [HideInInspector] public Transform[] m_Targets;

    public static float MouseSensitivity = 2.5f;

    public float Yaw   => m_Yaw;
    public float Pitch => m_Pitch;
    public Camera ActiveCamera => m_Camera;
    public Transform Target => m_Target;

    private Camera m_Camera;
    private Transform m_CamT;
    private Transform m_Target;
    private float m_Yaw;
    private float m_Pitch;
    private bool m_DebugOnce;
    private Vector3 m_LastMousePos;
    private bool m_HasLastMouse;
    private bool m_MouseAxesAvailable;
    private CursorLockMode m_DesiredLock;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] private static extern System.IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(System.IntPtr hWnd, out RECT lpRect);
#endif

    private void Awake()
    {
        // Cherche la cam : enfant en priorite, sinon Camera.main
        m_Camera = GetComponentInChildren<Camera>();
        if (m_Camera == null) m_Camera = Camera.main;
        if (m_Camera == null)
        {
            Debug.LogError("[CameraControl] Aucune Camera trouvee (ni enfant, ni Camera.main).");
        }
        else
        {
            m_CamT = m_Camera.transform;
            m_Camera.orthographic = false;
            m_Camera.fieldOfView = m_FieldOfView;
            m_Camera.nearClipPlane = 0.3f;
            m_Camera.farClipPlane  = 500f;
            Debug.Log($"[CameraControl] Cam trouvee : {m_Camera.name} (parent={m_Camera.transform.parent})");
        }

        // IPSSI-WAR : safety, on cap a 35° meme si l'inspector a une valeur plus
        // haute heritee d'une vieille version du prefab/scene.
        m_PitchMax = Mathf.Min(m_PitchMax, 35f);
        m_Pitch = Mathf.Clamp(m_DefaultPitch, m_PitchMin, m_PitchMax);
        MouseSensitivity = PlayerPrefs.GetFloat("MouseSensitivity", 2.5f);

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // Sur Windows on utilise TOUJOURS le path Win32 (recentrage curseur).
        // C'est fiable peu importe la config d'Input Manager (Unity 2022+ ne throw plus
        // pour un axe absent, il renvoie 0, donc impossible de detecter sans tester).
        m_MouseAxesAvailable = false;
        m_DesiredLock = CursorLockMode.None;
        Debug.Log("[CameraControl] Mode Win32 (GetCursorPos/SetCursorPos) actif.");
#else
        // Sur autres plateformes : test classique des axes.
        try
        {
            Input.GetAxisRaw("Mouse X");
            Input.GetAxisRaw("Mouse Y");
            m_MouseAxesAvailable = true;
            m_DesiredLock = CursorLockMode.Locked;
        }
        catch (System.ArgumentException)
        {
            m_MouseAxesAvailable = false;
            m_DesiredLock = CursorLockMode.Confined;
        }
#endif
    }

    public void SetTarget(Transform target)
    {
        m_Target = target;
        m_Targets = new Transform[] { target };
        if (target != null) m_Yaw = target.eulerAngles.y;
        Debug.Log($"[CameraControl] SetTarget -> {(target == null ? "NULL" : target.name)}");
    }

    public void SetStartPositionAndSize()
    {
        if (m_Target == null || m_CamT == null) return;
        ApplyTransform(true);
    }

    private void Update()
    {
        if (m_Target == null || m_CamT == null) return;

        // Applique le mode de lock voulu (sauf menu ouvert) - coherent avec m_DesiredLock.
        if (SensitivitySettingsUI.IsOpen)
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            m_HasLastMouse = false;
            return;
        }
        if (Cursor.lockState != m_DesiredLock)
        {
            Cursor.lockState = m_DesiredLock;
            Cursor.visible = false;
            m_HasLastMouse = false;
        }

        float mx = 0f, my = 0f;

        if (m_MouseAxesAvailable)
        {
            mx = Input.GetAxisRaw("Mouse X") * MouseSensitivity;
            my = Input.GetAxisRaw("Mouse Y") * MouseSensitivity;
        }
        else
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Recentre le curseur chaque frame via Win32 -> vrai feeling FPS, pas de bord d'ecran.
            if (!Application.isFocused)
            {
                m_HasLastMouse = false;
            }
            else
            {
                Cursor.visible = false;
            POINT pt;
            if (GetCursorPos(out pt))
            {
                System.IntPtr hwnd = GetActiveWindow();
                int cx, cy;
                RECT r;
                if (hwnd != System.IntPtr.Zero && GetWindowRect(hwnd, out r))
                {
                    cx = (r.Left + r.Right) / 2;
                    cy = (r.Top + r.Bottom) / 2;
                }
                else
                {
                    // Fallback : centre du desktop principal.
                    cx = Display.main.systemWidth  / 2;
                    cy = Display.main.systemHeight / 2;
                }

                if (m_HasLastMouse)
                {
                    int dx = pt.X - cx;
                    int dy = pt.Y - cy;
                    mx =  dx * 0.1f * MouseSensitivity;
                    my = -dy * 0.1f * MouseSensitivity; // Y inverse (ecran vs monde)
                }
                SetCursorPos(cx, cy);
                m_HasLastMouse = true;
            }
            }
#else
            // Fallback non-Windows : delta mousePosition (mode Confined).
            Vector3 mp = Input.mousePosition;
            if (m_HasLastMouse)
            {
                mx = (mp.x - m_LastMousePos.x) * 0.15f * MouseSensitivity;
                my = (mp.y - m_LastMousePos.y) * 0.15f * MouseSensitivity;
            }
            m_LastMousePos = mp;
            m_HasLastMouse = true;
#endif
        }

        if (!m_DebugOnce && (Mathf.Abs(mx) > 0.001f || Mathf.Abs(my) > 0.001f))
        {
            Debug.Log($"[CameraControl] Premier input souris recu (mx={mx:F2}, my={my:F2}). OK.");
            m_DebugOnce = true;
        }

        m_Yaw   += mx;
        m_Pitch -= my;
        m_Pitch  = Mathf.Clamp(m_Pitch, m_PitchMin, m_PitchMax);
    }

    private void LateUpdate()
    {
        if (m_Target == null || m_CamT == null) return;
        ApplyTransform(false);
    }

    // Debug overlay : affiche l'etat de la cam en jeu (pour diagnostic build).
    private void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.label);
        style.fontSize = 14;
        style.normal.textColor = Color.yellow;
        string s = $"[Cam] axes={m_MouseAxesAvailable} lock={Cursor.lockState} focus={Application.isFocused} yaw={m_Yaw:F1} pitch={m_Pitch:F1}";
        GUI.Label(new Rect(10, Screen.height - 40, Screen.width - 20, 20), s, style);
    }

    private void ApplyTransform(bool snap)
    {
        Vector3 pivot = m_Target.position + m_PivotOffset;
        Quaternion rot = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        Vector3 desiredPos = pivot + rot * new Vector3(0f, 0f, -m_Distance);

        float groundY = m_Target.position.y + m_MinHeight;
        if (desiredPos.y < groundY) desiredPos.y = groundY;

        // Detache la cam de tout parent pour eviter qu'elle soit retraversee
        // par un mouvement du rig ou d'un ancien parent.
        if (m_CamT.parent != null)
            m_CamT.SetParent(null, true);

        if (snap)
            m_CamT.position = desiredPos;
        else
            m_CamT.position = Vector3.Lerp(m_CamT.position, desiredPos, m_FollowSmooth * Time.deltaTime);

        m_CamT.rotation = Quaternion.LookRotation(pivot - m_CamT.position);
    }
}
