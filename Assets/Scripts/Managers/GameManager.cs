using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : NetworkBehaviour
{
    public int m_NumRoundsToWin = 5;
    public float m_StartDelay = 3f;
    public float m_EndDelay = 3f;
    public Text m_MessageText;
    public GameObject m_TankPrefab;
    public TankManager[] m_Tanks;           // Ã  configurer dans l'inspector avec les spawn points

    [Header("Power-ups")]
    public HealthPickupSpawner m_HealthPickupSpawner;

    [Header("Spawns (LAN / rounds)")]
    [Tooltip("Place plusieurs points vides sur la carte. Ã€ chaque round (2 joueurs), la paire la plus Ã©loignÃ©e est choisie au hasard (qui est joueur 1 ou 2). Sinon les spawns fixes ci-dessus sont utilisÃ©s.")]
    public Transform[] m_RoundSpawnPoints;

    private int m_RoundNumber = 0;
    private WaitForSeconds m_StartWait;
    private WaitForSeconds m_EndWait;
    private TankManager m_RoundWinner;
    private TankManager m_GameWinner;
    private List<ulong> m_ConnectedClients = new List<ulong>();
    private bool m_GameStarted = false;
    private bool m_ShuttingDown = false;

    private Vector3[] m_SpawnPosCache;
    private Quaternion[] m_SpawnRotCache;


    private void Awake()
    {
        // Efface le texte par dÃ©faut "TANKS!" dÃ¨s le dÃ©marrage sur tous les clients
        if (m_MessageText != null)
            m_MessageText.text = "";
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        m_StartWait = new WaitForSeconds(m_StartDelay);
        m_EndWait = new WaitForSeconds(m_EndDelay);

        // S'abonne aux connexions et dÃ©connexions
        NetworkManager.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

        // Cas d'un reload de scÃ¨ne : des joueurs sont peut-Ãªtre dÃ©jÃ  lÃ 
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!m_ConnectedClients.Contains(clientId))
                m_ConnectedClients.Add(clientId);
        }

        Debug.Log($"[GameManager] OnNetworkSpawn serveur. Joueurs dÃ©jÃ  connectÃ©s : {m_ConnectedClients.Count}");

        if (m_ConnectedClients.Count >= LobbyConfig.RequiredPlayers)
        {
            m_GameStarted = true;
            Debug.Log("[GameManager] 2 joueurs dÃ©jÃ  lÃ  â€” dÃ©marrage immÃ©diat.");
            StartCoroutine(StartGameDelayed());
        }
        else
        {
            SetMessageClientRpc("EN ATTENTE DU JOUEUR 2...");
            Debug.Log("[GameManager] En attente du joueur 2...");
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer || !m_GameStarted || m_ShuttingDown) return;
        // Ignore si ce n'est pas un joueur de la partie
        if (!m_ConnectedClients.Contains(clientId)) return;

        m_ShuttingDown = true;
        StopAllCoroutines();
        DisableTankControl();
        SetMessageClientRpc("Un joueur s'est dÃ©connectÃ©...\nRetour au menu dans 3s");
        StartCoroutine(ShutdownAfterDelay(3f));
    }

    private IEnumerator ShutdownAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
        // NetworkSessionWatcher dÃ©tecte la fin et recharge la scÃ¨ne sur toutes les machines
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer || m_GameStarted) return;

        // Evite le double-comptage (Host peut dÃ©clencher ce callback pour lui-mÃªme)
        if (m_ConnectedClients.Contains(clientId))
        {
            Debug.Log($"[GameManager] Client {clientId} dÃ©jÃ  comptÃ© â€” ignorÃ©.");
            return;
        }

        m_ConnectedClients.Add(clientId);
        Debug.Log($"[GameManager] Nouveau joueur connectÃ© : clientId={clientId}. Total={m_ConnectedClients.Count}");

        if (m_ConnectedClients.Count >= LobbyConfig.RequiredPlayers)
        {
            m_GameStarted = true;
            Debug.Log("[GameManager] 2 joueurs connectÃ©s â€” dÃ©marrage de la partie !");
            StartCoroutine(StartGameDelayed());
        }
        else
        {
            Debug.Log("[GameManager] Toujours en attente d'un 2Ã¨me joueur...");
        }
    }

    private IEnumerator StartGameDelayed()
    {
        Debug.Log("[GameManager] StartGameDelayed â€” attente 0.5s avant spawn...");
        // Attend que tous les clients soient stables avant de spawner
        yield return new WaitForSeconds(1.5f);
        SpawnAllTanks();
        // Cache le menu sur tous les clients au moment oÃ¹ la partie dÃ©marre
        HideMenuClientRpc();
        // Lance le bouton quitter sur tous les clients
        CreateQuitButtonClientRpc();
        Debug.Log("[GameManager] Tanks spawnÃ©s. Attente 0.5s avant GameLoop...");
        yield return new WaitForSeconds(0.5f);
        Debug.Log("[GameManager] DÃ©marrage du GameLoop !");
        StartCoroutine(GameLoop());
    }

    private void SpawnAllTanks()
    {
        Debug.Log($"[GameManager] SpawnAllTanks â€” m_Tanks.Length={m_Tanks.Length}, clients={m_ConnectedClients.Count}, BotMode={LobbyConfig.BotMode}");

        if (m_TankPrefab == null)
        {
            Debug.LogError("[GameManager] m_TankPrefab est NULL ! Assigne le prefab Tank dans l'Inspector du GameManager.");
            return;
        }

        // En mode 1 joueur (Bot), on alloue 2 slots : 1 humain + 1 bot.
        int totalSlots = LobbyConfig.BotMode
            ? Mathf.Min(m_Tanks.Length, m_ConnectedClients.Count + 1)
            : Mathf.Min(m_Tanks.Length, m_ConnectedClients.Count);
        ComputeSpawnTransformsForPlayers(totalSlots);

        for (int i = 0; i < totalSlots; i++)
        {
            bool isBotSlot = LobbyConfig.BotMode && i >= m_ConnectedClients.Count;
            ulong ownerId = isBotSlot
                ? NetworkManager.Singleton.LocalClientId  // host pilote le bot
                : m_ConnectedClients[i];

            Vector3 spawnPos;
            Quaternion spawnRot;
            if (m_SpawnPosCache != null && i < m_SpawnPosCache.Length)
            {
                spawnPos = m_SpawnPosCache[i];
                spawnRot = m_SpawnRotCache != null && i < m_SpawnRotCache.Length
                    ? m_SpawnRotCache[i]
                    : Quaternion.identity;
            }
            else if (m_Tanks[i].m_SpawnPoint != null)
            {
                spawnPos = m_Tanks[i].m_SpawnPoint.position;
                spawnRot = m_Tanks[i].m_SpawnPoint.rotation;
            }
            else
            {
                Debug.LogError($"[GameManager] Pas de position de spawn pour le tank {i} (pool + SpawnPoint vides).");
                continue;
            }

            Debug.Log($"[GameManager] Spawn tank {i} pour client {ownerId} Ã  {spawnPos}");

            // Crï¿½e le tank sur le serveur et l'assigne au bon joueur
            GameObject tankObj = Instantiate(
                m_TankPrefab,
                spawnPos,
                spawnRot);

            // CRITIQUE : on ajoute TankBotAI AVANT Spawn pour que son Awake() pose les
            // flags m_IsBot=true AVANT que LocalPlayerSetup.OnNetworkSpawn ne tourne
            // (sinon la cam se configure sur le tank-bot au lieu du tank joueur).
            if (isBotSlot)
            {
                tankObj.AddComponent<TankBotAI>();
                Debug.Log($"[GameManager] Slot {i} : tank-bot pilote par TankBotAI (host).");
            }

            NetworkObject netObj = tankObj.GetComponent<NetworkObject>();
            netObj.SpawnWithOwnership(ownerId);

            m_Tanks[i].m_Instance = tankObj;
            m_Tanks[i].m_PlayerNumber = i + 1;
            m_Tanks[i].Setup();

            // Applique la couleur sur tous les clients
            ApplyTankColorClientRpc(netObj.NetworkObjectId, m_Tanks[i].m_PlayerColor);
        }
    }

    // Boucle principale du jeu (tourne uniquement sur le serveur)
    private IEnumerator GameLoop()
    {
        // Cherche le spawner automatiquement si pas assigné dans l'inspector
        if (m_HealthPickupSpawner == null)
            m_HealthPickupSpawner = GetComponent<HealthPickupSpawner>();
        if (m_HealthPickupSpawner != null)
            m_HealthPickupSpawner.StartSpawning();

        while (true)
        {
            yield return StartCoroutine(RoundStarting());
            yield return StartCoroutine(RoundPlaying());
            yield return StartCoroutine(RoundEnding());

            if (m_GameWinner != null)
            {
                // Attend que le message de victoire soit lu, puis retour menu
                yield return new WaitForSeconds(4f);
                if (!m_ShuttingDown)
                {
                    m_ShuttingDown = true;
                    if (m_HealthPickupSpawner != null)
                        m_HealthPickupSpawner.StopSpawning();
                    SetMessageClientRpc("Retour au menu...");
                    yield return new WaitForSeconds(1f);
                    if (NetworkManager.Singleton != null)
                        NetworkManager.Singleton.Shutdown();
                    // NetworkSessionWatcher gÃ¨re le rechargement de scÃ¨ne sur tous les clients
                }
                yield break;
            }
        }
    }

    private IEnumerator RoundStarting()
    {
        Debug.Log($"[GameManager] RoundStarting â€” Round {m_RoundNumber + 1}");
        ResetAllTanks();
        DisableTankControl();

        m_RoundNumber++;
        SetMessageClientRpc("ROUND " + m_RoundNumber);
        Debug.Log($"[GameManager] Message envoyÃ© : ROUND {m_RoundNumber}");

        yield return m_StartWait;
    }

    private IEnumerator RoundPlaying()
    {
        Debug.Log("[GameManager] RoundPlaying â€” contrÃ´les activÃ©s !");
        EnableTankControl();
        SetMessageClientRpc("");

        while (!OneTankLeft())
            yield return null;
    }

    private IEnumerator RoundEnding()
    {
        DisableTankControl();

        m_RoundWinner = null;
        m_RoundWinner = GetRoundWinner();

        if (m_RoundWinner != null)
            m_RoundWinner.m_Wins++;

        m_GameWinner = GetGameWinner();

        string message = EndMessage();
        SetMessageClientRpc(message);

        yield return m_EndWait;
    }

    private bool OneTankLeft()
    {
        int numTanksLeft = 0;
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf)
                numTanksLeft++;
        }
        return numTanksLeft <= 1;
    }

    private TankManager GetRoundWinner()
    {
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance != null && m_Tanks[i].m_Instance.activeSelf)
                return m_Tanks[i];
        }
        return null;
    }

    private TankManager GetGameWinner()
    {
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Wins >= m_NumRoundsToWin)
                return m_Tanks[i];
        }
        return null;
    }

    private string EndMessage()
    {
        string message = "DRAW!";

        if (m_RoundWinner != null)
            message = m_RoundWinner.m_ColoredPlayerText + " WINS THE ROUND!";

        message += "\n\n\n\n";

        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance != null)
                message += m_Tanks[i].m_ColoredPlayerText + ": " + m_Tanks[i].m_Wins + " WINS\n";
        }

        if (m_GameWinner != null)
            message = m_GameWinner.m_ColoredPlayerText + " WINS THE GAME!";

        return message;
    }

    /// <summary>
    /// Choisit des positions pour ce round : si plusieurs points dans m_RoundSpawnPoints,
    /// pour 2 joueurs on prend la paire la plus Ã©loignÃ©e (ordre joueurs alÃ©atoire).
    /// </summary>
    private void ComputeSpawnTransformsForPlayers(int playerCount)
    {
        if (playerCount <= 0)
        {
            m_SpawnPosCache = null;
            m_SpawnRotCache = null;
            return;
        }

        m_SpawnPosCache = new Vector3[playerCount];
        m_SpawnRotCache = new Quaternion[playerCount];

        var validIndices = new List<int>();
        if (m_RoundSpawnPoints != null)
        {
            for (int i = 0; i < m_RoundSpawnPoints.Length; i++)
            {
                if (m_RoundSpawnPoints[i] != null)
                    validIndices.Add(i);
            }
        }

        if (playerCount == 2 && validIndices.Count >= 2)
        {
            float bestDist = -1f;
            int idxA = validIndices[0];
            int idxB = validIndices[1];
            for (int x = 0; x < validIndices.Count; x++)
            {
                Transform ta = m_RoundSpawnPoints[validIndices[x]];
                for (int y = x + 1; y < validIndices.Count; y++)
                {
                    Transform tb = m_RoundSpawnPoints[validIndices[y]];
                    float d = Vector3.Distance(ta.position, tb.position);
                    if (d > bestDist)
                    {
                        bestDist = d;
                        idxA = validIndices[x];
                        idxB = validIndices[y];
                    }
                }
            }

            bool swapSides = UnityEngine.Random.value > 0.5f;
            int first = swapSides ? idxB : idxA;
            int second = swapSides ? idxA : idxB;

            m_SpawnPosCache[0] = m_RoundSpawnPoints[first].position;
            m_SpawnRotCache[0] = m_RoundSpawnPoints[first].rotation;
            m_SpawnPosCache[1] = m_RoundSpawnPoints[second].position;
            m_SpawnRotCache[1] = m_RoundSpawnPoints[second].rotation;
            return;
        }

        for (int i = 0; i < playerCount; i++)
        {
            if (validIndices.Count > i)
            {
                Transform t = m_RoundSpawnPoints[validIndices[i]];
                m_SpawnPosCache[i] = t.position;
                m_SpawnRotCache[i] = t.rotation;
                continue;
            }

            if (i < m_Tanks.Length && m_Tanks[i].m_SpawnPoint != null)
            {
                m_SpawnPosCache[i] = m_Tanks[i].m_SpawnPoint.position;
                m_SpawnRotCache[i] = m_Tanks[i].m_SpawnPoint.rotation;
            }
            else
            {
                m_SpawnPosCache[i] = Vector3.zero;
                m_SpawnRotCache[i] = Quaternion.identity;
            }
        }
    }

    private void ResetAllTanks()
    {
        int alive = 0;
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance != null)
                alive++;
        }

        ComputeSpawnTransformsForPlayers(alive);

        int tankSlot = 0;
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance == null) continue;

            Vector3 pos = m_SpawnPosCache != null && tankSlot < m_SpawnPosCache.Length
                ? m_SpawnPosCache[tankSlot]
                : (m_Tanks[i].m_SpawnPoint != null ? m_Tanks[i].m_SpawnPoint.position : Vector3.zero);
            Quaternion rot = m_SpawnRotCache != null && tankSlot < m_SpawnRotCache.Length
                ? m_SpawnRotCache[tankSlot]
                : (m_Tanks[i].m_SpawnPoint != null ? m_Tanks[i].m_SpawnPoint.rotation : Quaternion.identity);

            tankSlot++;

            // Repositionne le tank â€” on envoie aussi un ClientRpc au propriÃ©taire
            // car ClientNetworkTransform est owner-authoritative (le server seul ne suffit pas)
            m_Tanks[i].m_Instance.transform.position = pos;
            m_Tanks[i].m_Instance.transform.rotation = rot;
            var netObj2 = m_Tanks[i].m_Instance.GetComponent<NetworkObject>();
            var move    = m_Tanks[i].m_Instance.GetComponent<TankMovement>();
            if (move != null && netObj2 != null)
            {
                var rpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                        { TargetClientIds = new[] { netObj2.OwnerClientId } }
                };
                move.TeleportClientRpc(pos, rot, rpcParams);
            }

            // Rï¿½initialise la santï¿½
            m_Tanks[i].m_Instance.GetComponent<TankHealth>().ResetTank();

            if (move != null) move.ResetSpeedBoostServer();
            TankShooting shooting = m_Tanks[i].m_Instance.GetComponent<TankShooting>();
            if (shooting != null) shooting.ResetPowerUpBuffsServer();

            // Rï¿½active le tank sur tous les clients
            ReactivateTankClientRpc(m_Tanks[i].m_Instance.GetComponent<NetworkObject>().NetworkObjectId);
        }
    }

    private void EnableTankControl()
    {
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance != null)
                m_Tanks[i].EnableControl();
        }
    }

    private void DisableTankControl()
    {
        for (int i = 0; i < m_Tanks.Length; i++)
        {
            if (m_Tanks[i].m_Instance != null)
                m_Tanks[i].DisableControl();
        }
    }

    // --- ClientRpcs : appelï¿½es par le serveur, exï¿½cutï¿½es sur tous les clients ---

    [ClientRpc]
    private void SetMessageClientRpc(string message)
    {
        if (m_MessageText != null)
            m_MessageText.text = message;
    }

    [ClientRpc]
    private void ApplyTankColorClientRpc(ulong networkObjectId, Color color)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            MeshRenderer[] renderers = netObj.GetComponentsInChildren<MeshRenderer>();
            foreach (var r in renderers)
                r.material.color = color;
        }
    }

    [ClientRpc]
    private void ReactivateTankClientRpc(ulong networkObjectId)
    {
        if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            netObj.gameObject.SetActive(true);
        }
    }

    // Cache le menu lobby sur tous les clients quand la partie démarre
    [ClientRpc]
    private void HideMenuClientRpc()
    {
        // Tous les NetworkMenuManagers (au cas où il y en a plusieurs dans la scène)
        foreach (var menu in FindObjectsOfType<NetworkMenuManager>())
            menu.ForceHide();
        // Fallback : cache directement le canvas par son nom
        var canvas = GameObject.Find("IPSSIWarMenuCanvas");
        if (canvas != null) canvas.SetActive(false);
    }

    // Crée le bouton Quitter sur tous les clients quand la partie commence
    [ClientRpc]
    private void CreateQuitButtonClientRpc()
    {
        new GameObject("GameQuitButton").AddComponent<GameQuitButton>();
    }
}
