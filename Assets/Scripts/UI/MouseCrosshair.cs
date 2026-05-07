using UnityEngine;

/// <summary>
/// IPSSI-WAR : reticle fixe au centre de l'ecran (curseur verrouille FPS-style).
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class MouseCrosshair : MonoBehaviour
{
    public RectTransform m_Reticle;
    public bool m_HideSystemCursor = true;

    private void Start()
    {
        if (m_HideSystemCursor)
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
    }

    private void Update()
    {
        if (m_Reticle == null) return;
        // Reticle toujours au centre
        m_Reticle.position = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
    }

    private void OnDisable()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
}
