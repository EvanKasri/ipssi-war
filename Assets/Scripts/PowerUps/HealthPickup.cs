using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Orbe de soin — spawné par GameManager.
/// Guérit le premier tank qui passe dessus, puis disparaît.
/// </summary>
public class HealthPickup : NetworkBehaviour
{
    public float m_HealAmount = 120f;

    private void Update()
    {
        // Rotation visuelle (côté client, purement cosmétique)
        transform.Rotate(0f, 90f * Time.deltaTime, 0f);
        transform.position += Vector3.up * Mathf.Sin(Time.time * 2f) * 0.003f;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        var health = other.GetComponentInParent<TankHealth>();
        if (health == null || health.IsDead()) return;

        health.HealServer(m_HealAmount);
        NetworkObject.Despawn(true); // détruit sur tous les clients
    }
}
