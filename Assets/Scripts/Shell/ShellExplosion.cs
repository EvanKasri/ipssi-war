using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ShellExplosion : NetworkBehaviour
{
    public LayerMask m_TankMask;
    public ParticleSystem m_ExplosionParticles;
    public AudioSource m_ExplosionAudio;
    public float m_MaxDamage = 48f;
    public float m_ExplosionForce = 1000f;
    public float m_MaxLifeTime = 2f;
    public float m_ExplosionRadius = 5f;

    // Défini par le serveur au spawn (boost dégâts côté tireur) — non synchronisé, serveur seul
    private float m_DamageMultiplier = 1f;
    private bool m_Exploded = false;
    private ulong m_ShooterId;
    private bool m_HasShooter = false;
    // IPSSI-WAR : NetworkObjectId du tank tireur (unique meme en BotMode ou
    // host possede plusieurs tanks). Sert a exclure le tank du tireur lui-meme
    // du test "ennemi touche" car OwnerClientId peut etre identique entre
    // joueur et bot quand l'host controle les deux.
    private ulong m_ShooterTankNetId;

    public void SetDamageMultiplier(float value)
    {
        m_DamageMultiplier = Mathf.Max(0f, value);
    }

    // IPSSI-WAR : identifie le tireur pour pouvoir lui envoyer un hit marker
    public void SetShooter(ulong clientId, ulong shooterTankNetId)
    {
        m_ShooterId = clientId;
        m_ShooterTankNetId = shooterTankNetId;
        m_HasShooter = true;
    }


    private void Start()
    {
        // Seul le serveur g�re la dur�e de vie du projectile
        if (IsServer)
            Invoke(nameof(TimeoutDestroy), m_MaxLifeTime);
    }

    private void TimeoutDestroy()
    {
        if (!m_Exploded)
        {
            m_Exploded = true;
            ExplodeClientRpc(transform.position);
            StartCoroutine(DespawnNextFrame());
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Seul le serveur traite les collisions et les d�g�ts
        if (!IsServer || m_Exploded) return;

        // IPSSI-WAR : si on touche le tank du tireur lui-meme (ex : marche arriere,
        // l'obus qui spawne tres pres du fire point peut accrocher la coque du
        // proprio), on ignore l'evenement et on laisse l'obus continuer sa route.
        // Sans ca, l'obus explose juste devant le tireur et le perd inutilement.
        if (m_HasShooter)
        {
            Rigidbody otherRb = other.attachedRigidbody;
            if (otherRb != null)
            {
                NetworkObject otherNet = otherRb.GetComponent<NetworkObject>();
                if (otherNet != null && otherNet.NetworkObjectId == m_ShooterTankNetId)
                    return; // on n'explose pas, l'obus continue
            }
        }

        m_Exploded = true;

        // Cherche tous les tanks dans le rayon d'explosion
        Collider[] colliders = Physics.OverlapSphere(transform.position, m_ExplosionRadius, m_TankMask);

        bool hitEnemy = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Rigidbody targetRigidbody = colliders[i].GetComponent<Rigidbody>();
            if (!targetRigidbody) continue;

            // IPSSI-WAR : on identifie le tank touche pour distinguer le tank tireur
            // (auquel on n'inflige pas de degats : evite le self-damage en marche
            // arriere ou contre les murs).
            NetworkObject targetNet = targetRigidbody.GetComponent<NetworkObject>();
            bool isShooterSelf = m_HasShooter && targetNet != null
                                 && targetNet.NetworkObjectId == m_ShooterTankNetId;

            targetRigidbody.AddExplosionForce(m_ExplosionForce, transform.position, m_ExplosionRadius);

            TankHealth targetHealth = targetRigidbody.GetComponent<TankHealth>();
            if (!targetHealth) continue;

            // Pas de degats sur soi-meme.
            if (isShooterSelf) continue;

            float damage = CalculateDamage(targetRigidbody.position) * m_DamageMultiplier;
            // IPSSI-WAR : mode One Shot — tout impact dans le rayon d'explosion
            // tue instantanement la cible (charge max obligatoire cote tireur).
            if (LobbyConfig.OneShotMode && damage > 0f)
                damage = targetHealth.m_StartingHealth + 1f;
            targetHealth.TakeDamage(damage);

            // Hit marker : tout tank touche != tireur compte comme ennemi
            if (damage > 0f && m_HasShooter && targetNet != null)
                hitEnemy = true;
        }

        // Hit marker chez le tireur uniquement
        if (hitEnemy)
        {
            ClientRpcParams target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { m_ShooterId } }
            };
            ShowHitMarkerClientRpc(target);
        }

        // Envoie l'effet visuel � tous les clients AVANT de d�truire le projectile
        ExplodeClientRpc(transform.position);
        StartCoroutine(DespawnNextFrame());
    }

    // Attend une frame pour s'assurer que le ClientRpc est bien envoy� avant la destruction
    private IEnumerator DespawnNextFrame()
    {
        yield return null;
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn(true);
    }

    // IPSSI-WAR : affiche le hit marker sur l'ecran du tireur uniquement
    [ClientRpc]
    private void ShowHitMarkerClientRpc(ClientRpcParams rpcParams = default)
    {
        if (HitMarkerUI.Instance != null)
            HitMarkerUI.Instance.Trigger();
    }

    // Ex�cut�e sur tous les clients : affiche l'explosion
    [ClientRpc]
    private void ExplodeClientRpc(Vector3 position)
    {
        // D�tache les particules du projectile avant qu'il soit d�truit
        m_ExplosionParticles.transform.parent = null;
        m_ExplosionParticles.transform.position = position;
        m_ExplosionParticles.Play();
        m_ExplosionAudio.Play();
        Destroy(m_ExplosionParticles.gameObject, m_ExplosionParticles.duration);
    }

    private float CalculateDamage(Vector3 targetPosition)
    {
        Vector3 explosionToTarget = targetPosition - transform.position;
        float explosionDistance = explosionToTarget.magnitude;
        float relativeDistance = (m_ExplosionRadius - explosionDistance) / m_ExplosionRadius;
        float damage = relativeDistance * m_MaxDamage;
        return Mathf.Max(0f, damage);
    }
}
