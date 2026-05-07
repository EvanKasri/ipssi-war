using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;   // NetworkTransform est ici
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Script de configuration automatique du multijoueur r�seau.
/// Menu : Tools > Setup Network Multiplayer
/// </summary>
public static class NetworkAutoSetup
{
    [MenuItem("Tools/Setup Network Multiplayer (1-clic)")]
    public static void SetupAll()
    {
        Debug.Log("=== D�marrage de la configuration r�seau ===");

        GameObject tankPrefab  = SetupTankPrefab();
        GameObject shellPrefab = SetupShellPrefab();

        CreateOrUpdateNetworkManager(tankPrefab, shellPrefab);
        AddNetworkObjectToGameManager();
        CreateMenuUI();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("=== Configuration termin�e ! Appuie sur Play pour tester. ===");
        EditorUtility.DisplayDialog(
            "Setup Network Multiplayer",
            "Configuration termin�e !\n\n" +
            "IMPORTANT : Dans le NetworkManager (dans la Hi�rarchie),\n" +
            "v�rifie que Tank et Shell sont bien dans 'Network Prefabs'.\n\n" +
            "Consulte la console pour les d�tails.",
            "OK");
    }

    // -------------------------------------------------------------------------
    // ETAPE 1 : Tank Prefab
    // -------------------------------------------------------------------------
    private static GameObject SetupTankPrefab()
    {
        string path = FindPrefabPath("Tank", "Assets/Prefabs");
        if (path == null) return null;

        using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            GameObject root = scope.prefabContentsRoot;

            AddIfMissing<NetworkObject>(root);

            var nt = AddIfMissing<NetworkTransform>(root);
            if (nt != null) nt.InLocalSpace = false;

            AddIfMissing<LocalPlayerSetup>(root);
            AddIfMissing<TankTurretAim>(root);

            Debug.Log($"[Tank Prefab] NetworkObject + NetworkTransform + LocalPlayerSetup + TankTurretAim ajoutés.");
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // -------------------------------------------------------------------------
    // ETAPE 2 : Shell Prefab
    // -------------------------------------------------------------------------
    private static GameObject SetupShellPrefab()
    {
        string path = FindPrefabPath("Shell", "Assets/Prefabs");
        if (path == null) return null;

        using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
        {
            GameObject root = scope.prefabContentsRoot;

            AddIfMissing<NetworkObject>(root);

            var nt = AddIfMissing<NetworkTransform>(root);
            if (nt != null) nt.InLocalSpace = false;

            Debug.Log($"[Shell Prefab] NetworkObject + NetworkTransform ajout�s.");
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    // -------------------------------------------------------------------------
    // ETAPE 3 : NetworkManager dans la sc�ne
    // -------------------------------------------------------------------------
    private static void CreateOrUpdateNetworkManager(GameObject tankPrefab, GameObject shellPrefab)
    {
        NetworkManager existing = Object.FindObjectOfType<NetworkManager>();
        GameObject nmGo;

        if (existing != null)
        {
            nmGo = existing.gameObject;
            Debug.Log("[NetworkManager] D�j� pr�sent, mise � jour.");
        }
        else
        {
            nmGo = new GameObject("NetworkManager");
            nmGo.AddComponent<NetworkManager>();
            nmGo.AddComponent<UnityTransport>();
            Debug.Log("[NetworkManager] Cr�� dans la sc�ne.");
        }

        NetworkManager nm = nmGo.GetComponent<NetworkManager>();
        UnityTransport transport = nmGo.GetComponent<UnityTransport>();
        if (transport == null) transport = nmGo.AddComponent<UnityTransport>();

        // Configure le transport
        nm.NetworkConfig.NetworkTransport = transport;

        // Enregistre les prefabs
        RegisterPrefab(nm, tankPrefab,  "Tank");
        RegisterPrefab(nm, shellPrefab, "Shell");

        MarkSceneDirty();
    }

    private static void RegisterPrefab(NetworkManager nm, GameObject prefab, string label)
    {
        if (prefab == null) return;

        // V�rifie si d�j� enregistr�
        foreach (var np in nm.NetworkConfig.Prefabs.Prefabs)
        {
            if (np.Prefab == prefab)
            {
                Debug.Log($"[NetworkManager] {label} d�j� enregistr�.");
                return;
            }
        }

        nm.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
        Debug.Log($"[NetworkManager] {label} enregistr�.");
    }

    // -------------------------------------------------------------------------
    // ETAPE 4 : NetworkObject sur le GameManager de la sc�ne
    // -------------------------------------------------------------------------
    private static void AddNetworkObjectToGameManager()
    {
        GameManager gm = Object.FindObjectOfType<GameManager>();
        if (gm == null)
        {
            Debug.LogWarning("[GameManager] Introuvable dans la sc�ne.");
            return;
        }

        if (gm.GetComponent<NetworkObject>() == null)
        {
            gm.gameObject.AddComponent<NetworkObject>();
            Debug.Log("[GameManager] NetworkObject ajout�.");
        }
        else
        {
            Debug.Log("[GameManager] NetworkObject d�j� pr�sent.");
        }

        MarkSceneDirty();
    }

    // -------------------------------------------------------------------------
    // ETAPE 5 : UI Host / Join
    // -------------------------------------------------------------------------

    private static GameObject CreateFlexLabel(Transform parent, string name, string text, float height, int fontSize,
        Color color, FontStyle style = FontStyle.Normal)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = Mathf.Min(height, 22f);
        Text t = go.AddComponent<Text>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return go;
    }

    private static Button CreateFlexButton(Transform parent, string name, string label, float height, Color bgColor)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;
        Image img = go.AddComponent<Image>();
        img.color = bgColor;
        Button btn = go.AddComponent<Button>();
        GameObject txtGO = new GameObject("Text");
        txtGO.transform.SetParent(go.transform, false);
        RectTransform txtRt = txtGO.AddComponent<RectTransform>();
        txtRt.anchorMin = Vector2.zero;
        txtRt.anchorMax = Vector2.one;
        txtRt.offsetMin = Vector2.zero;
        txtRt.offsetMax = Vector2.zero;
        Text txt = txtGO.AddComponent<Text>();
        txt.text = label;
        txt.fontSize = 14;
        txt.fontStyle = FontStyle.Bold;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        btn.targetGraphic = img;
        return btn;
    }

    private static void CreateMenuUI()
    {
        // Supprime les anciens objets menu s'ils existent
        foreach (string n in new[] { "NetworkMenuCanvas", "IPSSIWarMenuCanvas", "NetworkMenu" })
        {
            var old = GameObject.Find(n);
            if (old != null) Object.DestroyImmediate(old);
        }

        // NetworkMenuManager construit son propre canvas au runtime.
        // On crée juste le GameObject porteur du composant.
        GameObject nmMenuGO = new GameObject("NetworkMenu");
        nmMenuGO.AddComponent<LanLobbyDiscovery>();
        nmMenuGO.AddComponent<NetworkMenuManager>();

        MarkSceneDirty();
        Debug.Log("[UI] NetworkMenu créé. Le canvas IPSSI-WAR sera construit au runtime par NetworkMenuManager.");
    }

    // Ancienne méthode CreateMenuUI_OBSOLETE conservée pour référence uniquement
    [System.Obsolete]
    private static void CreateMenuUI_OBSOLETE_UNUSED()
    {
        // Canvas principal
        GameObject canvasGO = new GameObject("NetworkMenuCanvas");
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // Panel : deux colonnes (LAN | formulaire)
        GameObject panelGO = CreateUIElement<Image>("MenuPanel", canvasGO.transform);
        RectTransform panelRT = panelGO.GetComponent<RectTransform>();
        SetAnchors(panelRT, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.94f));
        panelGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        GameObject row = new GameObject("MenuInnerRow");
        row.transform.SetParent(panelGO.transform, false);
        RectTransform rowRt = row.AddComponent<RectTransform>();
        SetAnchors(rowRt, Vector2.zero, Vector2.one);
        HorizontalLayoutGroup rowH = row.AddComponent<HorizontalLayoutGroup>();
        rowH.spacing = 18;
        rowH.padding = new RectOffset(6, 6, 6, 6);
        rowH.childAlignment = TextAnchor.UpperCenter;
        rowH.childForceExpandHeight = true;

        GameObject lanCol = new GameObject("LanColumn");
        lanCol.transform.SetParent(row.transform, false);
        lanCol.AddComponent<RectTransform>();
        LayoutElement lanColLe = lanCol.AddComponent<LayoutElement>();
        lanColLe.preferredWidth = 296f;
        lanColLe.minWidth = 232f;
        lanColLe.flexibleWidth = 0f;
        VerticalLayoutGroup lanV = lanCol.AddComponent<VerticalLayoutGroup>();
        lanV.spacing = 10;
        lanV.padding = new RectOffset(10, 10, 12, 10);
        lanCol.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.11f, 0.92f);

        CreateFlexLabel(lanCol.transform, "LanHead", "Parties sur le reseau local\n(ipssi-war)", 54f, 13, new Color(0.95f, 0.95f, 1f), FontStyle.Bold);

        Button refreshBtn = CreateFlexButton(lanCol.transform, "RefreshLanButton", "ACTUALISER LE SCAN LAN", 40f, new Color(0.18f, 0.42f, 0.68f));

        GameObject lanListHolder = new GameObject("LanServerButtonHolder");
        lanListHolder.transform.SetParent(lanCol.transform, false);
        lanListHolder.AddComponent<RectTransform>();
        LayoutElement listLe = lanListHolder.AddComponent<LayoutElement>();
        listLe.flexibleHeight = 1f;
        listLe.minHeight = 160f;
        VerticalLayoutGroup listV = lanListHolder.AddComponent<VerticalLayoutGroup>();
        listV.spacing = 6;
        listV.padding = new RectOffset(6, 6, 6, 6);
        listV.childAlignment = TextAnchor.UpperCenter;
        lanListHolder.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

        GameObject hintGO = CreateFlexLabel(lanCol.transform, "LanDiscoveryHint", "", 80f, 11, new Color(0.82f, 0.86f, 1f));
        Text hintTxt = hintGO.GetComponent<Text>();
        hintTxt.alignment = TextAnchor.UpperLeft;

        GameObject mainCol = new GameObject("MainColumn");
        mainCol.transform.SetParent(row.transform, false);
        mainCol.AddComponent<RectTransform>();
        LayoutElement mainColLe = mainCol.AddComponent<LayoutElement>();
        mainColLe.flexibleWidth = 1f;
        mainColLe.minWidth = 280f;
        VerticalLayoutGroup mainVlg = mainCol.AddComponent<VerticalLayoutGroup>();
        mainVlg.spacing = 12;
        mainVlg.padding = new RectOffset(14, 14, 10, 10);
        mainCol.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

        CreateFlexLabel(mainCol.transform, "TitleText", "ipssi-war", 46f, 32, Color.yellow, FontStyle.Bold);
        CreateFlexLabel(mainCol.transform, "SubTitle", "Projet etudiant — IPSSI\nBataille de tanks multijoueur (LAN)\nmade by Evan, Wael, Mathis", 82f, 13, new Color(0.95f, 0.95f, 1f));

        CreateFlexLabel(mainCol.transform, "LabelIP", "Adresse IP du serveur a rejoindre :", 28f, 15, Color.white);

        GameObject inputGO = new GameObject("IPInput");
        inputGO.transform.SetParent(mainCol.transform, false);
        RectTransform inputRT = inputGO.AddComponent<RectTransform>();
        inputRT.anchorMin = new Vector2(0f, 1f);
        inputRT.anchorMax = new Vector2(1f, 1f);
        inputRT.pivot = new Vector2(0.5f, 1f);
        inputRT.sizeDelta = new Vector2(0f, 40f);
        LayoutElement inputLe = inputGO.AddComponent<LayoutElement>();
        inputLe.preferredHeight = 40f;
        inputLe.minHeight = 40f;
        inputGO.AddComponent<Image>().color = new Color(0.18f, 0.18f, 0.2f, 1f);
        InputField inputField = inputGO.AddComponent<InputField>();
        GameObject phGO = CreateUIText("Placeholder", inputGO.transform,
            "127.0.0.1",
            Vector2.zero, Vector2.one, 16, new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
        phGO.GetComponent<Text>().fontStyle = FontStyle.Italic;
        GameObject txtGO = CreateUIText("Text", inputGO.transform,
            "127.0.0.1",
            Vector2.zero, Vector2.one, 16, Color.white, TextAnchor.MiddleCenter);
        inputField.textComponent = txtGO.GetComponent<Text>();
        inputField.placeholder = phGO.GetComponent<Text>();
        inputField.text = "127.0.0.1";

        Button hostBtn = CreateFlexButton(mainCol.transform, "HostButton", "CREER LA PARTIE (HOST)", 44f, new Color(0.1f, 0.55f, 0.16f));
        Button joinBtn = CreateFlexButton(mainCol.transform, "JoinButton", "REJOINDRE (JOIN)", 44f, new Color(0.12f, 0.32f, 0.72f));

        GameObject statusGO = CreateFlexLabel(mainCol.transform, "StatusText", "", 40f, 13, new Color(1f, 0.85f, 0.35f));

        // Bandeau réseau (hors panneau : reste visible quand le menu est caché)
        GameObject persistGO = CreateUIText("PersistentInfoText", canvasGO.transform,
            "ipssi-war",
            new Vector2(0f, 0f), new Vector2(1f, 0.07f),
            11, new Color(0.8f, 0.85f, 1f), TextAnchor.LowerLeft);

        // Réticule + fil d'annonces power-up (désactivé jusqu'au spawn joueur)
        GameObject chRoot = new GameObject("CrosshairRoot");
        chRoot.transform.SetParent(canvasGO.transform, false);
        RectTransform chRootRt = chRoot.AddComponent<RectTransform>();
        chRootRt.anchorMin = Vector2.zero;
        chRootRt.anchorMax = Vector2.one;
        chRootRt.offsetMin = chRootRt.offsetMax = Vector2.zero;
        chRoot.SetActive(false);
        GameObject reticle = CreateUIElement<Image>("Reticle", chRoot.transform);
        RectTransform retRt = reticle.GetComponent<RectTransform>();
        retRt.anchorMin = retRt.anchorMax = new Vector2(0.5f, 0.5f);
        retRt.sizeDelta = new Vector2(24f, 24f);
        reticle.GetComponent<Image>().color = new Color(1f, 0.9f, 0.2f, 0.9f);
        MouseCrosshair mc = chRoot.AddComponent<MouseCrosshair>();
        mc.m_Reticle = retRt;
        reticle.transform.SetAsLastSibling();

        GameObject puf = CreateUIText("PowerUpFeed", canvasGO.transform,
            "",
            new Vector2(0.15f, 0.86f), new Vector2(0.85f, 0.95f),
            16, new Color(0.4f, 1f, 0.7f, 1f), TextAnchor.UpperCenter);
        puf.AddComponent<PowerUpFeed>();

        // Obsolète — remplacé par NetworkMenuManager auto-building
        MarkSceneDirty();
        Debug.Log("[UI] NetworkMenuCanvas (obsolète) créé.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------
    private static string FindPrefabPath(string name, string folder)
    {
        string[] guids = AssetDatabase.FindAssets($"{name} t:Prefab", new[] { folder });
        if (guids.Length == 0)
        {
            Debug.LogError($"Prefab '{name}' introuvable dans {folder}!");
            return null;
        }
        return AssetDatabase.GUIDToAssetPath(guids[0]);
    }

    private static T AddIfMissing<T>(GameObject go) where T : Component
    {
        if (go.GetComponent<T>() == null)
            return go.AddComponent<T>();
        return null;
    }

    private static GameObject CreateUIElement<T>(string name, Transform parent) where T : Component
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        go.AddComponent<T>();
        return go;
    }

    private static GameObject CreateUIText(string name, Transform parent, string text,
        Vector2 anchorMin, Vector2 anchorMax, int fontSize, Color color, TextAnchor alignment)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, anchorMin, anchorMax);
        var t = go.AddComponent<Text>();
        t.text = text;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = alignment;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        return go;
    }

    private static GameObject CreateButton(string name, Transform parent, string label,
        Vector2 anchorMin, Vector2 anchorMax, Color bgColor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        SetAnchors(rt, anchorMin, anchorMax);
        var img = go.AddComponent<Image>();
        img.color = bgColor;
        var btn = go.AddComponent<Button>();

        // Texte du bouton
        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(go.transform, false);
        var txtRT = txtGO.AddComponent<RectTransform>();
        txtRT.anchorMin = Vector2.zero;
        txtRT.anchorMax = Vector2.one;
        txtRT.offsetMin = Vector2.zero;
        txtRT.offsetMax = Vector2.zero;
        var txt = txtGO.AddComponent<Text>();
        txt.text = label;
        txt.fontSize = 16;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontStyle = FontStyle.Bold;

        btn.targetGraphic = img;
        return go;
    }

    private static void SetAnchors(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void MarkSceneDirty()
    {
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }
}
