using System;
using UnityEngine;

[Serializable]
public class TankManager
{
    public Color m_PlayerColor;
    public Transform m_SpawnPoint;
    [HideInInspector] public int m_PlayerNumber;
    [HideInInspector] public string m_ColoredPlayerText;
    [HideInInspector] public GameObject m_Instance;
    [HideInInspector] public int m_Wins;

    private TankMovement m_Movement;
    private TankShooting m_Shooting;


    public void Setup()
    {
        m_Movement = m_Instance.GetComponent<TankMovement>();
        m_Shooting = m_Instance.GetComponent<TankShooting>();

        // Cr�e le texte color� pour les messages de victoire
        m_ColoredPlayerText = "<color=#" + ColorUtility.ToHtmlStringRGB(m_PlayerColor) + ">PLAYER " + m_PlayerNumber + "</color>";
    }

    // Active le contr�le du tank (modification de la NetworkVariable sur le serveur)
    public void EnableControl()
    {
        if (m_Movement != null) m_Movement.m_ControlEnabled.Value = true;
        if (m_Shooting != null) m_Shooting.m_ControlEnabled.Value = true;
    }

    // D�sactive le contr�le du tank
    public void DisableControl()
    {
        if (m_Movement != null) m_Movement.m_ControlEnabled.Value = false;
        if (m_Shooting != null) m_Shooting.m_ControlEnabled.Value = false;
    }

    // R�initialise la position du tank pour un nouveau round
    public void Reset()
    {
        m_Instance.transform.position = m_SpawnPoint.position;
        m_Instance.transform.rotation = m_SpawnPoint.rotation;
        // La sant� est r�initialis�e par TankHealth.ResetTank() appel� depuis GameManager
    }
}
