using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Placez ce composant sur un objet de la scène (avec NetworkObject).
/// Au démarrage du serveur, instancie des power-ups enregistrés dans NetworkManager.
/// IPSSI-WAR
/// </summary>
public class PowerUpSpawner : NetworkBehaviour
{
    public NetworkObject m_PowerUpPrefab;
    public Transform[] m_SpawnPoints;
    [Min(0)] public int m_SpawnsAtStart = 4;

    public override void OnNetworkSpawn()
    {
        if (!IsServer || m_PowerUpPrefab == null) return;
        if (m_SpawnPoints == null || m_SpawnPoints.Length == 0) return;
        for (int n = 0; n < m_SpawnsAtStart; n++)
        {
            Transform t = m_SpawnPoints[n % m_SpawnPoints.Length];
            NetworkObject p = Instantiate(m_PowerUpPrefab, t.position, Quaternion.identity);
            p.Spawn();
        }
    }
}
