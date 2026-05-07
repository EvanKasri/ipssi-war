using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Spawne automatiquement des orbes de soin sur la carte.
/// MonoBehaviour simple (pas NetworkBehaviour) pour éviter les problèmes d'init réseau.
/// Cherche le prefab tout seul dans la liste NetworkManager. Aucune config requise.
/// </summary>
public class HealthPickupSpawner : MonoBehaviour
{
    [Tooltip("Rayon autour du centre de la carte où les orbes peuvent apparaître.")]
    public float m_SpawnRadius = 45f;

    [Tooltip("Centre de la zone de spawn (0,0,0 si la carte est centrée sur l'origine).")]
    public Vector3 m_SpawnCenter = Vector3.zero;

    [Tooltip("Secondes entre chaque orbe.")]
    public float m_SpawnInterval = 12f;

    private GameObject _prefab;
    private NetworkObject _currentPickup;
    private Coroutine _spawnCoroutine;

    // ── API appelée par GameManager ──────────────────────────────────────

    public void StartSpawning()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        _prefab = FindHealthPickupPrefab();
        if (_prefab == null)
        {
            Debug.LogWarning("[HealthPickupSpawner] Prefab HealthPickup introuvable dans NetworkPrefabs !");
            return;
        }

        Debug.Log("[HealthPickupSpawner] Démarrage du spawner.");
        if (_spawnCoroutine != null) StopCoroutine(_spawnCoroutine);
        _spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    public void StopSpawning()
    {
        if (_spawnCoroutine != null) { StopCoroutine(_spawnCoroutine); _spawnCoroutine = null; }
        if (_currentPickup != null) { _currentPickup.Despawn(true); _currentPickup = null; }
    }

    // ── Boucle ──────────────────────────────────────────────────────────

    private IEnumerator SpawnLoop()
    {
        yield return new WaitForSeconds(m_SpawnInterval);
        while (true)
        {
            if (_currentPickup == null)
                SpawnOrb();
            yield return new WaitForSeconds(m_SpawnInterval);
        }
    }

    private void SpawnOrb()
    {
        Vector3 pos = FindGroundPosition();
        GameObject go = Instantiate(_prefab, pos, Quaternion.identity);
        NetworkObject no = go.GetComponent<NetworkObject>();
        no.Spawn(true);
        _currentPickup = no;
        Debug.Log($"[HealthPickupSpawner] Orbe spawné à {pos}");
    }

    // ── Position aléatoire sur le sol ───────────────────────────────────

    private Vector3 FindGroundPosition()
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 r = Random.insideUnitCircle * m_SpawnRadius;
            Vector3 origin = m_SpawnCenter + new Vector3(r.x, 200f, r.y);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 300f))
            {
                if (hit.normal.y > 0.6f)
                    return hit.point + Vector3.up * 0.8f;
            }
        }
        // Fallback : hauteur fixe
        Vector2 fb = Random.insideUnitCircle * m_SpawnRadius;
        return m_SpawnCenter + new Vector3(fb.x, 1f, fb.y);
    }

    // ── Recherche automatique du prefab dans le NetworkManager ──────────

    private static GameObject FindHealthPickupPrefab()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return null;
        foreach (var entry in nm.NetworkConfig.Prefabs.Prefabs)
        {
            if (entry.Prefab != null && entry.Prefab.GetComponent<HealthPickup>() != null)
                return entry.Prefab;
        }
        return null;
    }
}
