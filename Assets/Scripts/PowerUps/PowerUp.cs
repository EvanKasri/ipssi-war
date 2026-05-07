using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Power-up ramassable sur la map. Le serveur détecte la collision avec un tank
/// et applique l'effet, puis despawn le power-up.
/// IPSSI-WAR
/// </summary>
public class PowerUp : NetworkBehaviour
{
    public enum Type { Heal, SpeedBoost, RapidFire, Damage }

    public Type m_Type = Type.Heal;
    public float m_Amount = 50f;        // Heal: HP, Speed: multiplicateur (ex 1.8), RapidFire: ratio temps de charge, Damage: multiplicateur
    public float m_Duration = 8f;       // Pour les effets temporaires
    public float m_RotationSpeed = 90f;
    public float m_BobAmplitude = 0.3f;
    public float m_BobFrequency = 2f;

    private Vector3 m_StartPos;

    private void Start()
    {
        m_StartPos = transform.position;
    }

    private void Update()
    {
        // Animation visuelle locale (n'a pas besoin d'être réseau)
        transform.Rotate(Vector3.up, m_RotationSpeed * Time.deltaTime, Space.World);
        float bob = Mathf.Sin(Time.time * m_BobFrequency) * m_BobAmplitude;
        transform.position = m_StartPos + Vector3.up * bob;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (!NetworkObject.IsSpawned) return;

        // Cherche un TankHealth (donc un tank) sur l'objet ou ses parents
        TankHealth th = other.GetComponentInParent<TankHealth>();
        if (th == null) return;

        ulong ownerId = th.GetComponent<NetworkObject>().OwnerClientId;
        ApplyEffect(th);
        AnnouncePickupClientRpc(m_Type, ownerId);

        if (NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }

    private void ApplyEffect(TankHealth th)
    {
        switch (m_Type)
        {
            case Type.Heal:
                th.HealServer(m_Amount);
                break;
            case Type.SpeedBoost:
                var mv = th.GetComponent<TankMovement>();
                if (mv != null) mv.ApplySpeedBoostServer(m_Amount, m_Duration);
                break;
            case Type.RapidFire:
                var sh = th.GetComponent<TankShooting>();
                if (sh != null) sh.ApplyRapidFireServer(m_Amount, m_Duration);
                break;
            case Type.Damage:
                var sh2 = th.GetComponent<TankShooting>();
                if (sh2 != null) sh2.ApplyDamageBoostServer(m_Amount, m_Duration);
                break;
        }
    }

    [ClientRpc]
    private void AnnouncePickupClientRpc(Type t, ulong ownerId)
    {
        Debug.Log($"[PowerUp] Player {ownerId} a ramassé {t}");
        if (PowerUpFeed.Instance != null)
            PowerUpFeed.Instance.Announce(t, ownerId);
    }
}
