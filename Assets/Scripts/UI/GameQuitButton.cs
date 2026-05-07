using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Overlay "Quitter la partie" affiché quand on appuie sur Échap pendant la partie.
/// S'auto-détruit quand le NetworkManager n'est plus actif (retour au menu).
/// </summary>
public class GameQuitButton : MonoBehaviour
{
    private Canvas    _canvas;
    private bool      _open = false;

    private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

    private void Start()
    {
        // Ne construire l'UI que si une session est active
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Destroy(gameObject);
            return;
        }
        BuildUI();
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            Destroy(gameObject);
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape))
            SetOpen(!_open);
    }

    private void SetOpen(bool open)
    {
        _open = open;
        if (_canvas != null) _canvas.gameObject.SetActive(open);
    }

    private void DoQuit()
    {
        SetOpen(false);
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
        // NetworkSessionWatcher rechargera la scène automatiquement
    }

    private void BuildUI()
    {
        var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Canvas overlay (au-dessus du jeu, sous le menu)
        var cgo = new GameObject("QuitOverlayCanvas");
        DontDestroyOnLoad(cgo);
        var cv = cgo.AddComponent<Canvas>();
        cv.renderMode   = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 150;
        cgo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        _canvas = cv;

        // Fond semi-transparent
        var bg = new GameObject("BG");
        bg.transform.SetParent(cgo.transform, false);
        var bgRT = bg.AddComponent<RectTransform>();
        bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = C(0, 0, 0, 0.55f);
        var bgBtn = bg.AddComponent<Button>();
        bgBtn.onClick.AddListener(() => SetOpen(false)); // clic hors panneau = ferme

        // Panneau centré
        var panel = new GameObject("Panel");
        panel.transform.SetParent(cgo.transform, false);
        var pRT = panel.AddComponent<RectTransform>();
        pRT.anchorMin = new Vector2(0.3f, 0.35f); pRT.anchorMax = new Vector2(0.7f, 0.65f);
        pRT.offsetMin = pRT.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = C(0.07f, 0.08f, 0.14f, 0.97f);

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding              = new RectOffset(30, 30, 24, 24);
        vlg.spacing              = 16;
        vlg.childControlWidth    = true; vlg.childControlHeight  = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment       = TextAnchor.MiddleCenter;

        // Titre
        AddTxt(panel.transform, "PAUSE", f, 32, C(1f, 0.83f, 0f), FontStyle.Bold, 44);
        AddTxt(panel.transform, "Voulez-vous quitter la partie ?", f, 14, Color.white, FontStyle.Normal, 22);
        AddSp(panel.transform, 8);

        // Bouton Quitter (rouge)
        AddBtn(panel.transform, "QUITTER LA PARTIE", f, C(0.72f, 0.10f, 0.10f), DoQuit, 46);
        AddSp(panel.transform, 4);
        // Bouton Reprendre (vert)
        AddBtn(panel.transform, "REPRENDRE", f, C(0.10f, 0.55f, 0.18f), () => SetOpen(false), 40);

        // Hint ESC en bas du panneau
        AddSp(panel.transform, 4);
        AddTxt(panel.transform, "[ ECHAP ] pour fermer", f, 10, C(0.5f, 0.5f, 0.6f), FontStyle.Normal, 14);

        cgo.SetActive(false); // caché par défaut
    }

    // ── Helpers UI ────────────────────────────────────────────────────
    private static void AddTxt(Transform parent, string text, Font f, int size, Color col, FontStyle style, float h)
    {
        var go = new GameObject("T"); go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>(); le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0;
        var t  = go.AddComponent<Text>();
        t.font = f; t.fontSize = size; t.color = col; t.fontStyle = style;
        t.alignment = TextAnchor.MiddleCenter; t.text = text; t.supportRichText = false;
    }

    private static void AddBtn(Transform parent, string label, Font f, Color col, System.Action cb, int h)
    {
        var go = new GameObject("Btn"); go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le  = go.AddComponent<LayoutElement>(); le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0;
        var img = go.AddComponent<Image>(); img.color = col;
        var btn = go.AddComponent<Button>();
        var bc  = btn.colors;
        bc.highlightedColor = Color.Lerp(col, Color.white, 0.2f);
        bc.pressedColor     = Color.Lerp(col, Color.black, 0.2f);
        btn.colors = bc; btn.targetGraphic = img;
        btn.onClick.AddListener(() => cb?.Invoke());

        var tgo = new GameObject("L"); tgo.transform.SetParent(go.transform, false);
        var trt = tgo.AddComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4, 2); trt.offsetMax = new Vector2(-4, -2);
        var t = tgo.AddComponent<Text>();
        t.font = f; t.fontSize = 15; t.fontStyle = FontStyle.Bold;
        t.color = Color.white; t.alignment = TextAnchor.MiddleCenter; t.text = label;
    }

    private static void AddSp(Transform parent, float h)
    {
        var go = new GameObject("Sp"); go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>();
        var le = go.AddComponent<LayoutElement>(); le.minHeight = h; le.preferredHeight = h; le.flexibleHeight = 0;
    }
}
