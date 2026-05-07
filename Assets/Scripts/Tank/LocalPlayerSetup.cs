using Unity.Netcode;
using UnityEngine;

// Ce script est sur le prefab Tank.
// Quand le tank spawne, si c'est le tank du joueur local,
// il configure la camera, le hit marker, la trajectory preview, et les reglages.
public class LocalPlayerSetup : NetworkBehaviour
{
    // IPSSI-WAR : couleur du tank choisie au menu, sync sur tous les clients.
    // Serveur a l'autorite ; l'owner demande sa couleur via ServerRpc au spawn.
    private NetworkVariable<int> m_ColorIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private static readonly int s_ColorPropertyId = Shader.PropertyToID("_Color");

    public override void OnNetworkSpawn()
    {
        Debug.Log($"[LocalPlayerSetup] OnNetworkSpawn — IsOwner={IsOwner}, IsServer={IsServer}, ClientId={OwnerClientId}");

        // --- Customization couleur (s'applique a TOUS les tanks : owner, observers, bot) ---
        m_ColorIndex.OnValueChanged += OnColorIndexChanged;
        ApplyColor(TankCustomization.GetColor(m_ColorIndex.Value));

        var botAI = GetComponent<TankBotAI>();
        if (IsServer && botAI != null)
            m_ColorIndex.Value = TankCustomization.BotColorIndex;
        if (IsOwner && botAI == null)
            RequestColorServerRpc(TankCustomization.SelectedColorIndex);

        if (!IsOwner) return;
        // Si ce tank est un bot (host pilote IA), on ne setup PAS la cam ni l'UI joueur sur lui.
        if (botAI != null) return;

        // --- Camera 3eme personne ---
        CameraControl cam = FindObjectOfType<CameraControl>();
        if (cam != null)
        {
            cam.SetTarget(transform);
            cam.SetStartPositionAndSize();
            Debug.Log("[LocalPlayerSetup] Caméra configurée sur ce tank !");
        }
        else
        {
            Debug.LogError("[LocalPlayerSetup] CameraControl introuvable dans la scène !");
        }

        // --- Reticle au centre ---
        MouseCrosshair ch = Object.FindObjectOfType<MouseCrosshair>(true);
        if (ch != null)
        {
            ch.gameObject.SetActive(true);
            ch.enabled = true;
        }

        // --- Hit marker UI (singleton) ---
        if (HitMarkerUI.Instance == null)
        {
            GameObject hmGO = new GameObject("HitMarkerUI");
            hmGO.AddComponent<HitMarkerUI>();
            DontDestroyOnLoad(hmGO);
        }

        // --- Vignette degats + HUD vies (singletons) ---
        if (DamageVignette.Instance == null)
        {
            GameObject dvGO = new GameObject("DamageVignette");
            dvGO.AddComponent<DamageVignette>();
            DontDestroyOnLoad(dvGO);
            Debug.Log("[LocalPlayerSetup] DamageVignette cree.");
        }
        else Debug.Log("[LocalPlayerSetup] DamageVignette deja existant.");

        if (HealthHud.Instance == null)
        {
            GameObject hhGO = new GameObject("HealthHud");
            hhGO.AddComponent<HealthHud>();
            DontDestroyOnLoad(hhGO);
            Debug.Log("[LocalPlayerSetup] HealthHud cree.");
        }
        else Debug.Log("[LocalPlayerSetup] HealthHud deja existant.");

        // --- Settings UI (sensibilite) ---
        if (FindObjectOfType<SensitivitySettingsUI>() == null)
        {
            GameObject sgGO = new GameObject("SensitivitySettings");
            sgGO.AddComponent<SensitivitySettingsUI>();
            DontDestroyOnLoad(sgGO);
        }

        // --- Trajectory preview (LineRenderer enfant du tank, owner only) ---
        if (GetComponentInChildren<TrajectoryPreview>() == null)
        {
            GameObject tpGO = new GameObject("TrajectoryPreview");
            tpGO.transform.SetParent(transform, false);
            TrajectoryPreview tp = tpGO.AddComponent<TrajectoryPreview>();
            tp.m_Shooting = GetComponent<TankShooting>();
            tp.m_NetRef = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        m_ColorIndex.OnValueChanged -= OnColorIndexChanged;
    }

    [ServerRpc]
    private void RequestColorServerRpc(int colorIndex)
    {
        m_ColorIndex.Value = TankCustomization.ClampIndex(colorIndex);
    }

    private void OnColorIndexChanged(int oldVal, int newVal)
    {
        ApplyColor(TankCustomization.GetColor(newVal));
    }

    // IPSSI-WAR : MaterialPropertyBlock pour eviter de creer des instances de
    // material (sinon chaque tank instancie ses propres mats -> fuite memoire et
    // batching casse). Toutes les meshes du tank (corps, tourelle, canon, chenilles)
    // sont teintees ; les LineRenderer/TrailRenderer/Particles sont ignores.
    private void ApplyColor(Color col)
    {
        var mpb = new MaterialPropertyBlock();
        mpb.SetColor(s_ColorPropertyId, col);
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            if (r is LineRenderer) continue;
            if (r is TrailRenderer) continue;
            r.SetPropertyBlock(mpb);
        }
    }
}
