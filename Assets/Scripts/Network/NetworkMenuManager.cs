using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menu IPSSI-WAR — construit entièrement au runtime.
/// États : Menu ➜ Waiting (après host/join) ➜ Playing (canvas caché).
/// </summary>
public class NetworkMenuManager : MonoBehaviour
{
    // ── State ─────────────────────────────────────────────────────────
    private enum State { Menu, Waiting, Playing }
    private State _state = State.Menu;

    // ── UI refs ───────────────────────────────────────────────────────
    private Canvas     _canvas;
    private GameObject _menuCard;
    private GameObject _waitCard;
    private InputField _ipInput;
    private Text       _waitText;
    private Text       _scanStatusText;
    private Transform  _lobbyListRoot;
    private Text       _bottomText;

    // ── LAN ───────────────────────────────────────────────────────────
    private LanLobbyDiscovery                       _disco;
    private readonly Dictionary<string, GameObject> _lobbyRows   = new();
    private readonly Dictionary<string, float>      _lastSeen    = new();
    private float _nextAutoScan = 0f;

    // ── Style ─────────────────────────────────────────────────────────
    private Font _f;
    private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);
    private static readonly Color CARD      = C(0.07f, 0.08f, 0.12f, 0.96f);
    private static readonly Color LOGO      = C(1.00f, 0.83f, 0.00f);
    private static readonly Color ACCENT    = C(1.00f, 0.50f, 0.00f);
    private static readonly Color CREDITS   = C(0.68f, 0.73f, 0.90f);
    private static readonly Color GREY      = C(0.48f, 0.52f, 0.62f);
    private static readonly Color CGREEN    = C(0.10f, 0.65f, 0.20f);
    private static readonly Color CBLUE     = C(0.14f, 0.40f, 0.78f);
    private static readonly Color CRED      = C(0.72f, 0.10f, 0.10f);
    private static readonly Color LOBBYROW  = C(0.10f, 0.25f, 0.48f, 0.95f);
    private static readonly Color LOBBYHOVER= C(0.18f, 0.42f, 0.72f, 1f);

    // ═════════════════════════════════════════════════════════════════
    //  LIFECYCLE
    // ═════════════════════════════════════════════════════════════════
    private void Awake()
    {
        _f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        foreach (var n in new[] { "NetworkMenuCanvas", "IPSSIWarMenuCanvas" })
        {
            var old = GameObject.Find(n);
            if (old != null) Destroy(old);
        }

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        // Reset systematique des flags quand on revient au menu.
        LobbyConfig.BotMode = false;
        LobbyConfig.OneShotMode = false;

        if (NetworkManager.Singleton != null &&
            NetworkManager.Singleton.GetComponent<NetworkSessionWatcher>() == null)
            NetworkManager.Singleton.gameObject.AddComponent<NetworkSessionWatcher>();

        _disco = GetComponent<LanLobbyDiscovery>()
              ?? FindObjectOfType<LanLobbyDiscovery>()
              ?? gameObject.AddComponent<LanLobbyDiscovery>();
        _disco.StopAll();

        BuildUI();
        ShowMenu();
    }

    private void Start()
    {
        UpdateBottomBar();
        StartAutoScan();
    }

    private bool _countdownStarted = false;

    private void Update()
    {
        var nm = NetworkManager.Singleton;

        if (_state == State.Waiting && nm != null && nm.IsListening)
        {
            if (nm.IsHost)
            {
                int cur = nm.ConnectedClients.Count;
                int req = LobbyConfig.RequiredPlayers;

                if (!_countdownStarted)
                {
                    // Mise à jour du texte en direct avec le compte de joueurs
                    SetWait(
                        $"<b>En attente des joueurs...</b>\n\n" +
                        $"<size=30><color=#FFD700>{cur} / {req}</color></size> joueurs connectés\n\n" +
                        $"<size=12><color=#8899BB>Donnez votre IP à vos amis :\n" +
                        $"<color=#FFD700>{GetLocalIPs()}</color></color></size>"
                    );

                    if (cur >= req)
                    {
                        _countdownStarted = true;
                        StartCoroutine(CountdownThenHide());
                    }
                }
            }
            else if (nm.IsClient && nm.IsConnectedClient && !_countdownStarted)
            {
                _countdownStarted = true;
                StartCoroutine(HideAfterDelay(2.5f));
            }
        }

        if (_state != State.Menu) return;

        // Auto-refresh toutes les 4 secondes
        if (Time.realtimeSinceStartup >= _nextAutoScan)
        {
            _nextAutoScan = Time.realtimeSinceStartup + 4f;
            DoScan();
        }

        // Expirer les lobbies non vus depuis 12s
        float now = Time.realtimeSinceStartup;
        List<string> expired = null;
        foreach (var kv in _lastSeen)
            if (now - kv.Value > 12f)
                (expired ??= new List<string>()).Add(kv.Key);
        if (expired != null)
            foreach (var ip in expired)
            {
                _lastSeen.Remove(ip);
                if (_lobbyRows.TryGetValue(ip, out var r) && r) Destroy(r);
                _lobbyRows.Remove(ip);
                RefreshScanStatus();
            }
    }

    private void OnDestroy()
    {
        _disco?.StopAll();
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerJoinedAsHost;
    }

    // ═════════════════════════════════════════════════════════════════
    //  STATE MACHINE
    // ═════════════════════════════════════════════════════════════════
    private void ShowMenu()
    {
        _state = State.Menu;
        _countdownStarted = false;
        StopAllCoroutines();
        if (_canvas   != null) _canvas.gameObject.SetActive(true);
        if (_menuCard != null) _menuCard.SetActive(true);
        if (_waitCard != null) _waitCard.SetActive(false);
        // IPSSI-WAR : libere le curseur (en game il est verrouille pour la cam FPS).
        // Sans ca, apres une partie le curseur reste invisible/lock dans le menu.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
        StartAutoScan();
    }

    private void ShowWaiting(string msg)
    {
        _state = State.Waiting;
        if (_canvas   != null) _canvas.gameObject.SetActive(true);
        if (_menuCard != null) _menuCard.SetActive(false);
        if (_waitCard != null) { _waitCard.SetActive(true); SetWait(msg); }
        // Curseur visible pour interagir avec le bouton ANNULER.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible   = true;
    }

    private void HideForGame()
    {
        _state = State.Playing;
        if (_canvas != null) _canvas.gameObject.SetActive(false);
    }

    /// <summary>Appelé par GameManager via ClientRpc quand la partie démarre vraiment.</summary>
    public void ForceHide()
    {
        StopAllCoroutines();
        HideForGame();
    }

    private void SetWait(string msg) { if (_waitText) _waitText.text = msg; }

    // ═════════════════════════════════════════════════════════════════
    //  NETWORKING
    // ═════════════════════════════════════════════════════════════════
    // Mode 1 joueur vs Bot : on demarre comme host, RequiredPlayers=1, sans broadcast LAN.
    private void OnSoloBotClicked()
    {
        LobbyConfig.RequiredPlayers = 1;
        LobbyConfig.BotMode = true;
        LobbyConfig.OneShotMode = false;
        _disco?.StopAll(); // pas de broadcast LAN pour une partie solo

        var tr = NetworkManager.Singleton.GetComponent<UnityTransport>();
        tr.SetConnectionData("127.0.0.1", 7777);
        NetworkManager.Singleton.StartHost();

        ShowWaiting("<b>Mode 1 joueur</b>\n\nPreparation du combat contre le bot...");
        UpdateBottomBar(isHost: true);
    }

    // IPSSI-WAR : mode One Shot — charge max obligatoire, un impact tue.
    private void OnOneShotBotClicked()
    {
        LobbyConfig.RequiredPlayers = 1;
        LobbyConfig.BotMode = true;
        LobbyConfig.OneShotMode = true;
        _disco?.StopAll();

        var tr = NetworkManager.Singleton.GetComponent<UnityTransport>();
        tr.SetConnectionData("127.0.0.1", 7777);
        NetworkManager.Singleton.StartHost();

        ShowWaiting("<b>Mode One Shot</b>\n\nCharge complete obligatoire.\nUn impact, une mort.");
        UpdateBottomBar(isHost: true);
    }

    private void OnHostClicked(int requiredPlayers)
    {
        LobbyConfig.BotMode = false;
        LobbyConfig.OneShotMode = false;
        LobbyConfig.RequiredPlayers = requiredPlayers;
        _disco?.StopAll();
        if (_disco != null) _disco.CurrentMaxPlayers = requiredPlayers;

        var tr = NetworkManager.Singleton.GetComponent<UnityTransport>();
        tr.SetConnectionData("0.0.0.0", 7777);
        NetworkManager.Singleton.StartHost();
        _disco?.StartHostBeacon(7777);
        NetworkManager.Singleton.OnClientConnectedCallback += OnPlayerJoinedAsHost;

        ShowWaiting(
            $"<b>En attente des joueurs...</b>\n\n" +
            $"<size=30><color=#FFD700>1 / {requiredPlayers}</color></size> joueurs connectés\n\n" +
            $"<size=12><color=#8899BB>Donnez votre IP à vos amis :\n" +
            $"<color=#FFD700>{GetLocalIPs()}</color></color></size>"
        );
        UpdateBottomBar(isHost: true);

        // Timeout lobby
        StartCoroutine(LobbyTimeout());
    }

    private void OnPlayerJoinedAsHost(ulong clientId)
    {
        // Le texte est mis à jour en temps réel dans Update()
        // Le countdown se lance aussi dans Update() quand le count est atteint
    }

    private IEnumerator LobbyTimeout()
    {
        float remaining = LobbyConfig.LobbyTimeoutSeconds;
        while (remaining > 0f && _state == State.Waiting && !_countdownStarted)
        {
            yield return new WaitForSeconds(1f);
            remaining -= 1f;
        }

        // Si toujours en attente et countdown pas lancé = timeout
        if (_state == State.Waiting && !_countdownStarted)
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.Shutdown();
            ShowMenu();
            SetWait("Lobby expiré : pas assez de joueurs.\nRetour au menu.");
        }
    }

    private IEnumerator CountdownThenHide()
    {
        for (int i = 3; i > 0; i--)
        {
            SetWait($"<b>La partie commence dans...</b>\n\n<size=80><color=#FFD700>{i}</color></size>");
            yield return new WaitForSeconds(1f);
        }
        HideForGame();
    }

    private void DoCancel()
    {
        StopAllCoroutines();
        NetworkManager.Singleton.OnClientConnectedCallback -= OnPlayerJoinedAsHost;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
        _disco?.StopAll();
        ShowMenu();
    }

    private void JoinServer(string ip, int port)
    {
        LobbyConfig.OneShotMode = false; // l'host est seul a decider du mode
        _disco?.StopAll();
        var tr = NetworkManager.Singleton.GetComponent<UnityTransport>();
        tr.SetConnectionData(ip, (ushort)port, "0.0.0.0");
        NetworkManager.Singleton.StartClient();
        ShowWaiting($"<b>Connexion à {ip}...</b>\n\nEn attente de l'hôte...");
        UpdateBottomBar();
    }

    private IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (_state == State.Waiting) HideForGame();
    }

    // ── LAN ──────────────────────────────────────────────────────────
    private void StartAutoScan()
    {
        if (_disco == null) return;
        _disco.StartClientListen(OnServerFound);
        _nextAutoScan = Time.realtimeSinceStartup;
    }

    private void DoScan()
    {
        if (_disco == null) return;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;
        _disco.SendDiscoveryRequest(7777);
        if (_scanStatusText && _lobbyRows.Count == 0)
            _scanStatusText.text = "Recherche de parties en cours...";
    }

    private void OnServerFound(LanLobbyDiscovery.LanServerEntry e)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening) return;

        _lastSeen[e.Address] = Time.realtimeSinceStartup;

        if (!_lobbyRows.TryGetValue(e.Address, out var row) || !row)
        {
            row = BuildLobbyRow(e.Address, e.HostName, e.Port, e.MaxPlayers);
            _lobbyRows[e.Address] = row;
            if (_lobbyListRoot != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    _lobbyListRoot.GetComponent<RectTransform>());
        }
        else
        {
            var lbl = row.transform.Find("Info")?.GetComponent<Text>();
            if (lbl) lbl.text = FormatLobbyLabel(e.HostName, e.Address, e.MaxPlayers);
        }
        RefreshScanStatus();
    }

    private void RefreshScanStatus()
    {
        if (!_scanStatusText) return;
        if (_lobbyRows.Count == 0)
            _scanStatusText.text = "Aucune partie sur ce réseau.\n<size=10>Même Wi-Fi / hotspot que l'hôte requis.</size>";
        else
            _scanStatusText.text = $"<color=#00FFB0>{_lobbyRows.Count} partie(s) — cliquez pour rejoindre</color>";
    }

    private static string FormatLobbyLabel(string host, string ip, int maxPlayers)
        => $"<b>{host}</b>  <size=11><color=#8899BB>{ip}</color></size>  <color=#FFD700>[{maxPlayers}J]</color>";

    private void UpdateBottomBar(bool isHost = false)
    {
        if (!_bottomText) return;
        string role = isHost ? "  -  HOTE ACTIF" : "";
        _bottomText.text = $"IPSSI-WAR  |  {Environment.MachineName}  |  {GetLocalIPs()}{role}";
    }

    private static string GetLocalIPs()
    {
        var withGw = new StringBuilder();
        var noGw   = new StringBuilder();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                var props = ni.GetIPProperties();
                bool hasIPv4Gw = false;
                if (props.GatewayAddresses != null)
                    foreach (var gw in props.GatewayAddresses)
                        if (gw.Address.AddressFamily == AddressFamily.InterNetwork)
                        { hasIPv4Gw = true; break; }

                if (!hasIPv4Gw)
                {
                    string desc = ni.Description.ToLowerInvariant();
                    string name = ni.Name.ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("hyper-v") ||
                        desc.Contains("vmware")  || desc.Contains("vpn")     ||
                        desc.Contains("tap ")    || name.Contains("vethernet")) continue;
                }

                foreach (var u in props.UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string a = u.Address.ToString();
                    if (a.StartsWith("169.254.")) continue;
                    var target = hasIPv4Gw ? withGw : noGw;
                    if (target.Length > 0) target.Append("  /  ");
                    target.Append(a);
                }
            }
        }
        catch { }
        if (withGw.Length > 0) return withGw.ToString();
        return noGw.Length > 0 ? noGw.ToString() : "(n/a)";
    }

    // ═════════════════════════════════════════════════════════════════
    //  UI BUILDER
    // ═════════════════════════════════════════════════════════════════
    private void BuildUI()
    {
        // ── Canvas ───────────────────────────────────────────────────
        var cgo = new GameObject("IPSSIWarMenuCanvas");
        var cv  = cgo.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 200;
        var sc = cgo.AddComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        sc.matchWidthOrHeight  = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        _canvas = cv;

        MkPanel(cgo.transform, "BG", C(0.02f, 0.02f, 0.06f, 0.68f), Vector2.zero, Vector2.one);

        // ══════════════════════════════════════════════════════════════
        //  MENU CARD
        // ══════════════════════════════════════════════════════════════
        _menuCard = MkPanel(cgo.transform, "MenuCard", CARD,
                            new Vector2(0.25f, 0.03f), new Vector2(0.75f, 0.97f));
        var vlg = _menuCard.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(28, 28, 18, 14);
        vlg.spacing              = 6;
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // Header
        MkTxt(_menuCard.transform, "IPSSI-WAR",                       54, LOGO,    FontStyle.Bold,   62);
        MkTxt(_menuCard.transform, "PROJET ETUDIANT  -  IPSSI  2026", 13, ACCENT,  FontStyle.Normal, 18);
        MkTxt(_menuCard.transform, "PAR  EVAN  -  WAEL  -  MATHIS",   11, CREDITS, FontStyle.Normal, 16);
        MkSep(_menuCard.transform, ACCENT, 2, 10);

        // ── Personnalisation tank ────────────────────────────────────
        MkTxt(_menuCard.transform, "COULEUR DU TANK", 12, ACCENT, FontStyle.Bold, 16);
        MkSp(_menuCard.transform, 3);
        BuildColorPickerRow(_menuCard.transform);
        MkSep(_menuCard.transform, GREY, 1, 6);

        // ── Mode solo vs bot ─────────────────────────────────────────
        MkTxt(_menuCard.transform, "MODE SOLO", 14, ACCENT, FontStyle.Bold, 18);
        MkSp(_menuCard.transform, 4);
        MkBtn(_menuCard.transform, "1 JOUEUR  (VS BOT)", ACCENT, OnSoloBotClicked, 44);
        MkSp(_menuCard.transform, 4);
        MkBtn(_menuCard.transform, "ONE SHOT  (VS BOT)", CRED, OnOneShotBotClicked, 44);
        MkSep(_menuCard.transform, GREY, 1, 8);

        // ── Sélecteur nombre de joueurs ──────────────────────────────
        MkTxt(_menuCard.transform, "CREER UNE PARTIE LAN", 14, CGREEN, FontStyle.Bold, 18);
        MkSp(_menuCard.transform, 4);
        BuildHostButtonRow(_menuCard.transform);
        MkSep(_menuCard.transform, GREY, 1, 8);

        // ── Lobbies disponibles ──────────────────────────────────────
        MkTxt(_menuCard.transform, "REJOINDRE UNE PARTIE SUR CE RESEAU",
              11, C(0f, 0.72f, 0.88f), FontStyle.Bold, 16);
        MkSp(_menuCard.transform, 4);

        var listContainer = MkFixedChild(_menuCard.transform, "LobbyContainer", 40);
        listContainer.GetComponent<LayoutElement>().flexibleHeight = 1;
        listContainer.AddComponent<Image>().color = C(0.04f, 0.05f, 0.09f, 0.6f);
        listContainer.AddComponent<RectMask2D>();
        var lcVLG = listContainer.AddComponent<VerticalLayoutGroup>();
        lcVLG.padding              = new RectOffset(4, 4, 6, 6);
        lcVLG.spacing              = 5;
        lcVLG.childControlWidth    = true;
        lcVLG.childControlHeight   = true;
        lcVLG.childForceExpandWidth  = true;
        lcVLG.childForceExpandHeight = false;
        _lobbyListRoot = listContainer.transform;

        _scanStatusText = MkTxt(_menuCard.transform,
            "Recherche de parties en cours...", 11, GREY, FontStyle.Normal, 28);

        MkSep(_menuCard.transform, GREY, 1, 6);
        MkTxt(_menuCard.transform, "ou saisir une IP manuellement", 10, GREY, FontStyle.Normal, 12);
        MkSp(_menuCard.transform, 3);
        BuildManualJoinRow(_menuCard.transform);
        MkSep(_menuCard.transform, GREY, 1, 4);
        MkSp(_menuCard.transform, 2);

        // Barre basse
        var bot = MkPanel(cgo.transform, "Bot", C(0, 0, 0, 0.55f),
                          Vector2.zero, new Vector2(1, 0));
        var brt = bot.GetComponent<RectTransform>();
        brt.pivot = new Vector2(0.5f, 0f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(0, 22);
        _bottomText = MkTxtAbs(bot.transform, "IPSSI-WAR...", 10, GREY,
                               new Vector2(12, 0), Vector2.zero, TextAnchor.MiddleLeft);

        // ══════════════════════════════════════════════════════════════
        //  WAIT CARD
        // ══════════════════════════════════════════════════════════════
        _waitCard = MkPanel(cgo.transform, "WaitCard", C(0.04f, 0.04f, 0.10f, 0.94f),
                            new Vector2(0.20f, 0.18f), new Vector2(0.80f, 0.82f));
        _waitCard.SetActive(false);

        var wvlg = _waitCard.AddComponent<VerticalLayoutGroup>();
        wvlg.padding              = new RectOffset(40, 40, 36, 36);
        wvlg.spacing              = 20;
        wvlg.childAlignment       = TextAnchor.MiddleCenter;
        wvlg.childControlWidth    = true;
        wvlg.childControlHeight   = true;
        wvlg.childForceExpandWidth  = true;
        wvlg.childForceExpandHeight = false;

        MkTxt(_waitCard.transform, "IPSSI-WAR", 36, LOGO, FontStyle.Bold, 50);
        MkSep(_waitCard.transform, ACCENT, 2, 6);
        var wt = MkTxt(_waitCard.transform, "...", 18, Color.white, FontStyle.Normal, 150);
        wt.gameObject.GetComponent<LayoutElement>().preferredHeight = 160;
        _waitText = wt;
        MkSep(_waitCard.transform, GREY, 1, 6);

        // Bouton Annuler (visible seulement pour l'hôte qui attend)
        MkBtn(_waitCard.transform, "ANNULER / RETOUR AU MENU", CRED, DoCancel, 40);
        MkTxt(_waitCard.transform, $"Lobby expire automatiquement après {(int)LobbyConfig.LobbyTimeoutSeconds}s",
              10, GREY, FontStyle.Normal, 14);
    }

    // ── Ligne 3 boutons HOST (2 / 3 / 4 joueurs) ──────────────────────
    private void BuildHostButtonRow(Transform parent)
    {
        var row = MkFixedChild(parent, "HostRow", 52);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 10;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.childControlWidth    = true;
        hlg.childControlHeight   = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;

        foreach (int n in new[] { 2, 3, 4 })
        {
            int count = n; // capture
            var btnGO = new GameObject($"Host{n}");
            btnGO.transform.SetParent(row.transform, false);
            btnGO.AddComponent<RectTransform>();
            btnGO.AddComponent<LayoutElement>(); // expand géré par HLG
            var img = btnGO.AddComponent<Image>(); img.color = CGREEN;
            var btn = btnGO.AddComponent<Button>();
            var bc  = btn.colors;
            bc.highlightedColor = Color.Lerp(CGREEN, Color.white, 0.22f);
            bc.pressedColor     = Color.Lerp(CGREEN, Color.black, 0.22f);
            btn.colors = bc; btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnHostClicked(count));

            var lgo = new GameObject("L"); lgo.transform.SetParent(btnGO.transform, false);
            var lrt = lgo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(4, 2); lrt.offsetMax = new Vector2(-4, -2);
            var lt = lgo.AddComponent<Text>();
            lt.font = _f; lt.fontSize = 15; lt.fontStyle = FontStyle.Bold;
            lt.color = Color.white; lt.alignment = TextAnchor.MiddleCenter;
            lt.text = $"{n}\nJOUEURS";
        }
    }

    // ── Ligne IP manuelle ──────────────────────────────────────────────
    private void BuildManualJoinRow(Transform parent)
    {
        var row = MkFixedChild(parent, "ManualRow", 40);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8;
        hlg.padding = new RectOffset(0, 0, 0, 0);
        hlg.childControlWidth    = true;
        hlg.childControlHeight   = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;

        var inputGO = new GameObject("IPInput");
        inputGO.transform.SetParent(row.transform, false);
        inputGO.AddComponent<RectTransform>();
        var ile = inputGO.AddComponent<LayoutElement>();
        ile.flexibleWidth = 1; ile.minHeight = 40; ile.preferredHeight = 40;
        inputGO.AddComponent<Image>().color = C(0.12f, 0.13f, 0.19f);
        var field = inputGO.AddComponent<InputField>();

        var phGO = new GameObject("PH");
        phGO.transform.SetParent(inputGO.transform, false);
        var phRT = phGO.AddComponent<RectTransform>();
        phRT.anchorMin = Vector2.zero; phRT.anchorMax = Vector2.one;
        phRT.offsetMin = new Vector2(8, 2); phRT.offsetMax = new Vector2(-4, -2);
        var pht = phGO.AddComponent<Text>();
        pht.font = _f; pht.fontSize = 12; pht.fontStyle = FontStyle.Italic;
        pht.color = GREY; pht.text = "Adresse IP de l'hote...";
        pht.alignment = TextAnchor.MiddleLeft; pht.supportRichText = false;

        var txGO = new GameObject("TX");
        txGO.transform.SetParent(inputGO.transform, false);
        var txRT = txGO.AddComponent<RectTransform>();
        txRT.anchorMin = Vector2.zero; txRT.anchorMax = Vector2.one;
        txRT.offsetMin = new Vector2(8, 2); txRT.offsetMax = new Vector2(-4, -2);
        var txt = txGO.AddComponent<Text>();
        txt.font = _f; txt.fontSize = 12; txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft; txt.supportRichText = false;

        field.textComponent = txt; field.placeholder = pht;
        field.text = "127.0.0.1"; field.caretColor = ACCENT;
        _ipInput = field;

        var btnGO = new GameObject("JoinBtn");
        btnGO.transform.SetParent(row.transform, false);
        btnGO.AddComponent<RectTransform>();
        var ble = btnGO.AddComponent<LayoutElement>();
        ble.minWidth = 120; ble.preferredWidth = 120;
        ble.minHeight = 40; ble.preferredHeight = 40;
        var bImg = btnGO.AddComponent<Image>(); bImg.color = CBLUE;
        var btn  = btnGO.AddComponent<Button>();
        var bc = btn.colors;
        bc.highlightedColor = Color.Lerp(CBLUE, Color.white, 0.22f);
        bc.pressedColor     = Color.Lerp(CBLUE, Color.black, 0.22f);
        btn.colors = bc; btn.targetGraphic = bImg;
        btn.onClick.AddListener(() =>
        {
            string ip = _ipInput != null ? _ipInput.text.Trim() : "127.0.0.1";
            if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
            JoinServer(ip, 7777);
        });

        var bTgo = new GameObject("L");
        bTgo.transform.SetParent(btnGO.transform, false);
        var bTrt = bTgo.AddComponent<RectTransform>();
        bTrt.anchorMin = Vector2.zero; bTrt.anchorMax = Vector2.one;
        bTrt.offsetMin = new Vector2(4, 2); bTrt.offsetMax = new Vector2(-4, -2);
        var bT = bTgo.AddComponent<Text>();
        bT.font = _f; bT.fontSize = 13; bT.fontStyle = FontStyle.Bold;
        bT.color = Color.white; bT.alignment = TextAnchor.MiddleCenter; bT.text = "REJOINDRE";
    }

    // ── Lobby row ─────────────────────────────────────────────────────
    private GameObject BuildLobbyRow(string ip, string host, int port, int maxPlayers)
    {
        var go = new GameObject("Lobby_" + ip);
        go.transform.SetParent(_lobbyListRoot, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = 48; le.preferredHeight = 48; le.flexibleHeight = 0;

        var bg  = go.AddComponent<Image>(); bg.color = LOBBYROW;
        var btn = go.AddComponent<Button>();
        var cb  = btn.colors;
        cb.highlightedColor = LOBBYHOVER;
        cb.pressedColor     = Color.Lerp(LOBBYROW, Color.black, 0.3f);
        btn.colors = cb; btn.targetGraphic = bg;
        string ipC = ip; int pC = port;
        btn.onClick.AddListener(() => JoinServer(ipC, pC));

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding              = new RectOffset(12, 12, 4, 4);
        hlg.spacing              = 8;
        hlg.childControlWidth    = true;  hlg.childControlHeight    = true;
        hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = true;

        var infoGO = new GameObject("Info");
        infoGO.transform.SetParent(go.transform, false);
        infoGO.AddComponent<RectTransform>();
        var iLE = infoGO.AddComponent<LayoutElement>(); iLE.flexibleWidth = 1;
        var infoT = infoGO.AddComponent<Text>();
        infoT.font = _f; infoT.fontSize = 14; infoT.color = Color.white;
        infoT.alignment = TextAnchor.MiddleLeft; infoT.supportRichText = true;
        infoT.text = FormatLobbyLabel(host, ip, maxPlayers);

        var badgeGO = new GameObject("Badge");
        badgeGO.transform.SetParent(go.transform, false);
        badgeGO.AddComponent<RectTransform>();
        var bLE = badgeGO.AddComponent<LayoutElement>(); bLE.minWidth = 80; bLE.preferredWidth = 80;
        var bT  = badgeGO.AddComponent<Text>();
        bT.font = _f; bT.fontSize = 10; bT.color = C(0.7f, 0.9f, 1f);
        bT.alignment = TextAnchor.MiddleRight; bT.text = "REJOINDRE >";
        bT.fontStyle = FontStyle.Bold;

        return go;
    }

    // ═════════════════════════════════════════════════════════════════
    //  HELPERS UI
    // ═════════════════════════════════════════════════════════════════
    private static GameObject MkPanel(Transform parent, string name, Color col, Vector2 amin, Vector2 amax)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = amin; rt.anchorMax = amax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = col;
        return go;
    }

    private GameObject MkFixedChild(Transform parent, string name, float h)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0;
        return go;
    }

    private Text MkTxt(Transform parent, string text, int size, Color col, FontStyle style, float h)
    {
        var go = new GameObject("Txt");
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>();
        le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0;
        var t = go.AddComponent<Text>();
        t.font = _f; t.fontSize = size; t.color = col;
        t.fontStyle = style; t.alignment = TextAnchor.MiddleCenter; t.text = text;
        t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap;
        return t;
    }

    private Text MkTxtAbs(Transform parent, string text, int size, Color col,
                           Vector2 offMin, Vector2 offMax, TextAnchor align)
    {
        var go = new GameObject("Txt");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        var t = go.AddComponent<Text>();
        t.font = _f; t.fontSize = size; t.color = col;
        t.fontStyle = FontStyle.Normal; t.alignment = align; t.text = text;
        t.supportRichText = true;
        return t;
    }

    private Button MkBtn(Transform parent, string label, Color col, Action cb, int h = 44)
    {
        var go = MkFixedChild(parent, "Btn", h);
        var img = go.AddComponent<Image>(); img.color = col;
        var btn = go.AddComponent<Button>();
        var c   = btn.colors;
        c.highlightedColor = Color.Lerp(col, Color.white, 0.22f);
        c.pressedColor     = Color.Lerp(col, Color.black, 0.22f);
        btn.colors = c; btn.targetGraphic = img;
        btn.onClick.AddListener(() => cb?.Invoke());

        var tgo = new GameObject("L"); tgo.transform.SetParent(go.transform, false);
        var trt = tgo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(6, 2); trt.offsetMax = new Vector2(-6, -2);
        var t = tgo.AddComponent<Text>();
        t.font = _f; t.fontSize = h >= 48 ? 19 : 14; t.fontStyle = FontStyle.Bold;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter; t.text = label;
        return btn;
    }

    private void MkSep(Transform parent, Color col, float thick, int margin)
    {
        MkSp(parent, margin);
        var go = MkFixedChild(parent, "Sep", thick);
        go.AddComponent<Image>().color = col;
        MkSp(parent, margin);
    }

    private void MkSp(Transform parent, float h) => MkFixedChild(parent, "Sp", h);

    // ═════════════════════════════════════════════════════════════════
    //  COLOR PICKER (personnalisation tank)
    // ═════════════════════════════════════════════════════════════════
    private GameObject[] _colorBtns;

    private void BuildColorPickerRow(Transform parent)
    {
        var row = MkFixedChild(parent, "ColorRow", 32);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 4;
        hlg.padding                = new RectOffset(6, 6, 2, 2);
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = true;
        hlg.childForceExpandHeight = true;

        int n = TankCustomization.Palette.Length;
        _colorBtns = new GameObject[n];
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            var go = new GameObject($"Color{i}");
            go.transform.SetParent(row.transform, false);
            go.AddComponent<RectTransform>();

            var img = go.AddComponent<Image>();
            img.color = TankCustomization.Palette[i];

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() => OnColorClicked(idx));

            _colorBtns[i] = go;
        }
        UpdateColorPickerVisuals();
    }

    private void OnColorClicked(int idx)
    {
        TankCustomization.SelectedColorIndex = TankCustomization.ClampIndex(idx);
        UpdateColorPickerVisuals();
    }

    // IPSSI-WAR : feedback visuel — la couleur selectionnee reste opaque,
    // les autres passent a 45% d'alpha pour signaler l'etat.
    private void UpdateColorPickerVisuals()
    {
        if (_colorBtns == null) return;
        for (int i = 0; i < _colorBtns.Length; i++)
        {
            if (_colorBtns[i] == null) continue;
            var img = _colorBtns[i].GetComponent<Image>();
            if (img == null) continue;
            Color c = TankCustomization.Palette[i];
            if (i != TankCustomization.SelectedColorIndex) c.a = 0.45f;
            img.color = c;
        }
    }
}
