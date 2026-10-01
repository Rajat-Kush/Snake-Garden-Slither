using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// ============================================================================
//  MAIN MENU - built entirely at runtime (the scene itself never changes).
//
//  Opens before the round starts.  PLAY launches the game, OPTIONS toggles
//  music / sfx (PlayerPrefs - audio gets wired in later), CREDITS shows the
//  author, EXIT quits.  Visuals: blurred gameplay screenshot + vignette,
//  grass-textured panels, snake-skin pattern ribbons and a slithering
//  decorative snake made from the actual skin texture.
// ============================================================================
public class MainMenu : MonoBehaviour
{
    public static bool Open { get; private set; }

    // --- audio state (sounds arrive later; settings already stick) ---
    public static bool MusicOn => PlayerPrefs.GetInt("MusicOn", 1) == 1;
    public static bool SfxOn => PlayerPrefs.GetInt("SfxOn", 1) == 1;
    public static void UiClick() { /* hook: play click SFX when audio lands */ }
    public static void UiHover() { /* hook: play hover SFX when audio lands */ }

    // --- palette (drawn from the snake + garden) ---
    static readonly Color Gold = new Color(0.94f, 0.76f, 0.29f);
    static readonly Color GoldBright = new Color(1.00f, 0.87f, 0.45f);
    static readonly Color Cream = new Color(0.95f, 0.92f, 0.82f);
    static readonly Color PanelFill = new Color(0.085f, 0.066f, 0.048f, 0.94f);
    static readonly Color BtnFill = new Color(0.10f, 0.078f, 0.055f, 0.88f);
    static readonly Color BtnFillHover = new Color(0.17f, 0.13f, 0.085f, 0.95f);
    static readonly Color BtnBorder = new Color(0.62f, 0.48f, 0.18f, 0.90f);
    static readonly Color DeepBrown = new Color(0.16f, 0.095f, 0.045f);
    static readonly Color DangerHover = new Color(0.45f, 0.13f, 0.10f, 0.94f);

    static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    Font _font;
    Font Fnt
    {
        get
        {
            if (_font == null)
            {
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            }
            return _font;
        }
    }

    // runtime refs
    RectTransform _mainPage;
    Image _fade;
    float _fadeA = 1f, _fadeTarget;
    int _phase;                          // 0 idle | 1 fading to black | 2 fading back in over the game
    GameManager _gm;
    GameObject _activePage;
    RectTransform _activePanel;
    float _pageT;
    bool _pageOpening;

    // ================================================================ boot
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        Show();
    }

    /// Re-creates the menu over a running game — used by ESC during play or
    /// after game over. Rebuilds the full UI; Awake pauses the clock.
    public static void Show()
    {
        if (FindFirstObjectByType<MainMenu>() != null) return;
        var go = new GameObject("MainMenu");
        go.AddComponent<MainMenu>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        Open = true;
        Time.timeScale = 0f;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        BuildUI();
    }

    void OnDestroy()
    {
        Time.timeScale = 1f;             // never leave time frozen if we die mid-fade
    }

    // ============================================================== build
    void BuildUI()
    {
        // --- canvas ---
        var cgo = new GameObject("MenuCanvas", typeof(RectTransform));
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();

        if (FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        // --- background stack ---
        Sprite bgSpr = Tex("Menu/menu_bg");
        var bg = Img(cgo.transform, "Background", bgSpr,
                     bgSpr != null ? Color.white : new Color(0.06f, 0.11f, 0.05f, 1f), true);
        Stretch(bg.rectTransform);

        var tint = Img(cgo.transform, "Tint", null, new Color(0.02f, 0.055f, 0.03f, 0.45f), false);
        Stretch(tint.rectTransform);

        var grad = Img(cgo.transform, "BottomFade",
            Gradient(new Color(0f, 0f, 0f, 0f), new Color(0.01f, 0.03f, 0.015f, 0.75f)),
            Color.white, false);
        Stretch(grad.rectTransform);

        var vig = Img(cgo.transform, "Vignette", Vignette(0.62f), Color.white, false);
        Stretch(vig.rectTransform);

        BuildSpecks(cgo.transform);

        // --- main page -----------------------------------------------------
        var mp = Rect(cgo.transform, "MainPage", Vector2.zero, Vector2.one,
                      new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _mainPage = mp;

        BuildDecorSnake(mp);
        BuildLeftColumn(mp);
        BuildCard(mp);

        // --- modal pages ---------------------------------------------------
        BuildOptionsPage(cgo.transform);
        BuildCreditsPage(cgo.transform);

        // --- fade (always on top) ------------------------------------------
        var fade = Img(cgo.transform, "Fade", null, new Color(0.01f, 0.02f, 0.01f, 1f), false);
        Stretch(fade.rectTransform);
        fade.raycastTarget = true;
        _fade = fade;
    }

    // ---------------------------------------------------------- left column
    void BuildLeftColumn(RectTransform parent)
    {
        // container: anchored middle-left, fixed design box (canvas = 1920x1080 ref)
        var col = Rect(parent, "Column", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                       new Vector2(0f, 0.5f), new Vector2(150f, 25f), new Vector2(660f, 640f));

        // soft golden glow behind the title
        var glow = Img(col, "TitleGlow", Glow(new Color(0.94f, 0.76f, 0.29f, 0.17f)), Color.white, false);
        glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        glow.rectTransform.sizeDelta = new Vector2(620f, 300f);
        glow.rectTransform.anchoredPosition = new Vector2(0f, 215f);

        // TITLE
        var titleRt = Rect(col, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(0.5f, 0.5f), new Vector2(0f, 245f), new Vector2(660f, 150f));
        var title = NewText(titleRt, "SNAKE", 132, Gold, TextAnchor.MiddleCenter, true);
        title.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(4f, -6f);
        title.gameObject.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.8f);

        // pattern ribbon (actual snake skin, tiled)
        var rib = Rect(col, "Ribbon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                       new Vector2(0.5f, 0.5f), new Vector2(0f, 157f), new Vector2(480f, 14f));
        var ribImg = rib.gameObject.AddComponent<Image>();
        ribImg.sprite = PatternRibbon();
        ribImg.type = Image.Type.Tiled;
        ribImg.color = Color.white;
        ribImg.raycastTarget = false;

        // SUBTITLE
        var subRt = Rect(col, "Subtitle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                         new Vector2(0.5f, 0.5f), new Vector2(0f, 123f), new Vector2(660f, 34f));
        NewText(subRt, "A  G A R D E N   S L I T H E R", 23,
                new Color(Cream.r, Cream.g, Cream.b, 0.82f), TextAnchor.MiddleCenter, false);

        // PLAY --------------------------------------------------------------
        var playRt = Rect(col, "PlayBtn", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                          new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(330f, 82f));
        // drop shadow behind the button
        var sh = Img(col, "PlayShadow", Rounded(330, 82, 20f, new Color(0f, 0f, 0f, 0.45f), 0f, Color.clear, "btnSh"),
                     Color.white, false);
        sh.rectTransform.anchorMin = sh.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        sh.rectTransform.sizeDelta = new Vector2(330f, 82f);
        sh.rectTransform.anchoredPosition = new Vector2(0f, 5f);
        sh.rectTransform.SetSiblingIndex(playRt.GetSiblingIndex());

        var play = playRt.gameObject.AddComponent<Image>();
        play.sprite = Rounded(330, 82, 20f, Gold, 0f, Color.clear, "play");
        play.color = Color.white;
        var playBtn = playRt.gameObject.AddComponent<Button>();
        playBtn.transition = Selectable.Transition.None;
        playBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        playBtn.onClick.AddListener(LaunchGame);
        var playLbl = Rect(playRt, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var playTxt = NewText(playLbl, "PLAY", 40, DeepBrown, TextAnchor.MiddleCenter, true);
        playTxt.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, 2f);
        playTxt.gameObject.GetComponent<Shadow>().effectColor = new Color(1f, 1f, 1f, 0.35f);
        var playFx = playRt.gameObject.AddComponent<HoverFX>();
        playFx.Init(play, Color.white, new Color(1.08f, 1.02f, 0.9f), playRt);
        playFx.hoverScale = 1.045f;

        // OPTIONS -----------------------------------------------------------
        var optRt = Rect(col, "OptionsBtn", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                         new Vector2(0.5f, 0.5f), new Vector2(0f, -75f), new Vector2(330f, 58f));
        MakeMenuButton(optRt, "O P T I O N S", BtnFill, BtnFillHover, Cream, 25,
                       () => ShowPage("options"), false);

        // CREDITS -----------------------------------------------------------
        var credRt = Rect(col, "CreditsBtn", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                          new Vector2(0.5f, 0.5f), new Vector2(0f, -149f), new Vector2(330f, 58f));
        MakeMenuButton(credRt, "C R E D I T S", BtnFill, BtnFillHover, Cream, 25,
                       () => ShowPage("credits"), true);

        // EXIT --------------------------------------------------------------
        var exitRt = Rect(col, "ExitBtn", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                          new Vector2(0.5f, 0.5f), new Vector2(0f, -223f), new Vector2(330f, 58f));
        MakeMenuButton(exitRt, "E X I T", BtnFill, DangerHover, new Color(0.93f, 0.62f, 0.52f), 25,
                       DoExit, true);

        // FOOTER hint -------------------------------------------------------
        var footRt = Rect(col, "Footer", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                          new Vector2(0.5f, 0.5f), new Vector2(0f, -299f), new Vector2(660f, 26f));
        NewText(footRt, "A R R O W   K E Y S   /   W A S D     t o   s l i t h e r", 16,
                new Color(0.78f, 0.84f, 0.74f, 0.6f), TextAnchor.MiddleCenter, false);

        // staggered entrance for every column element (top to bottom)
        for (int i = 0; i < col.childCount; i++)
        {
            var child = col.GetChild(i) as RectTransform;
            if (child != null) RegisterEnter(child, 0.12f + i * 0.06f, new Vector2(0f, -26f));
        }
    }

    void MakeMenuButton(RectTransform rt, string label, Color baseCol, Color hoverCol,
                        Color textCol, int fontSize, UnityEngine.Events.UnityAction onClick, bool border)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = Rounded((int)rt.rect.width, (int)rt.rect.height, 16f, Color.white,
                             border ? 3f : 0f, border ? BtnBorder : Color.clear, "btn" + (int)rt.rect.width);
        img.color = baseCol;
        var b = rt.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.navigation = new Navigation { mode = Navigation.Mode.None };
        b.onClick.AddListener(() => { UiClick(); onClick(); });

        var lblRt = Rect(rt, "Label", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        NewText(lblRt, label, fontSize, textCol, TextAnchor.MiddleCenter, true);

        var fx = rt.gameObject.AddComponent<HoverFX>();
        fx.Init(img, baseCol, hoverCol, rt);
    }

    // ---------------------------------------------------------- photo card
    void BuildCard(RectTransform parent)
    {
        var card = Rect(parent, "Card", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(1f, 0.5f), new Vector2(-135f, 40f), new Vector2(470f, 400f));
        var le = card.gameObject.AddComponent<CanvasGroup>();
        le.alpha = 1f;

        card.localEulerAngles = new Vector3(0f, 0f, -6f);

        // shadow
        var sh = Img(card, "Shadow", Rounded(470, 400, 14f, new Color(0f, 0f, 0f, 0.5f), 0f, Color.clear, "cardSh"),
                     Color.white, false);
        Stretch(sh.rectTransform);
        sh.rectTransform.anchoredPosition = new Vector2(7f, -9f);

        // polaroid frame
        var frame = Img(card, "Frame", Rounded(470, 400, 14f, new Color(0.94f, 0.91f, 0.83f, 0.97f), 0f, Color.clear, "card"),
                        Color.white, false);
        Stretch(frame.rectTransform);

        // photo
        var photoRt = Rect(card, "Photo", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                           new Vector2(0.5f, 0.5f), new Vector2(0f, 32f), new Vector2(442f, 316f));
        var photo = photoRt.gameObject.AddComponent<Image>();
        var photoSpr = Tex("Menu/menu_card");
        photo.sprite = photoSpr;
        if (photoSpr == null) photo.color = new Color(0.12f, 0.20f, 0.10f, 1f);
        photo.preserveAspect = true;
        photo.raycastTarget = false;

        // caption inside the polaroid
        var capRt = Rect(card, "Caption", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                         new Vector2(0.5f, 0.5f), new Vector2(0f, -163f), new Vector2(440f, 40f));
        NewText(capRt, "I N   T H E   G A R D E N", 19,
                new Color(0.28f, 0.20f, 0.10f, 0.95f), TextAnchor.MiddleCenter, true);

        RegisterEnter(card, 0.80f, new Vector2(70f, 12f));   // card slides in from the right
    }

    // ------------------------------------------------- ambient floating specks
    void BuildSpecks(Transform canvas)
    {
        var cont = Rect(canvas, "Specks", Vector2.zero, Vector2.one,
                        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var sp = cont.gameObject.AddComponent<MenuSpecks>();
        sp.Init(cont);
    }

    // ------------------------------------------------- staggered entrance
    class Enter
    {
        public RectTransform rt;
        public CanvasGroup cg;
        public Vector2 pos;
        public float delay;
        public Vector2 slide;
    }
    readonly List<Enter> _enter = new List<Enter>();
    float _enterT;

    void RegisterEnter(RectTransform rt, float delay, Vector2 slideFrom)
    {
        var cg = rt.gameObject.GetComponent<CanvasGroup>();
        if (cg == null) cg = rt.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        _enter.Add(new Enter { rt = rt, cg = cg, pos = rt.anchoredPosition, delay = delay, slide = slideFrom });
    }

    // ------------------------------------------------- decorative slither
    void BuildDecorSnake(RectTransform parent)
    {
        var cont = Rect(parent, "DecorSnake", Vector2.zero, Vector2.one,
                        new Vector2(0f, 0f), Vector2.zero, Vector2.zero);

        const int N = 16;
        var dots = new Image[N];
        var sizes = new float[N];
        var dotSpr = PatternDot();

        for (int i = 0; i < N; i++)
        {
            float k = i / (float)(N - 1);
            sizes[i] = Mathf.Lerp(34f, 12f, k * k * 0.9f + k * 0.1f);
            var rt = Rect(cont, "D" + i, new Vector2(0f, 0f), new Vector2(0f, 0f),
                          new Vector2(0.5f, 0.5f), new Vector2(-200f, 110f),
                          new Vector2(sizes[i], sizes[i]));
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = dotSpr;
            img.raycastTarget = false;
            img.color = Color.Lerp(Color.white, new Color(0.8f, 0.8f, 0.8f), k * 0.35f); // slight tail dim
            dots[i] = img;

            if (i == 0)   // head with eyes
            {
                for (int e = 0; e < 2; e++)
                {
                    float ey = e == 0 ? 6f : -6f;
                    var eye = Rect(rt, "Eye" + e, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0.5f, 0.5f), new Vector2(7f, ey), new Vector2(11f, 11f));
                    var ei = eye.gameObject.AddComponent<Image>();
                    ei.sprite = Rounded(11, 11, 5.5f, Color.white, 0f, Color.clear, "eyeW");
                    ei.color = Color.white;
                    ei.raycastTarget = false;
                    var pu = Rect(eye, "Pupil", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0.5f, 0.5f), new Vector2(2.5f, 0f), new Vector2(5f, 5f));
                    var pi = pu.gameObject.AddComponent<Image>();
                    pi.sprite = Rounded(5, 5, 2.5f, Color.black, 0f, Color.clear, "eyeB");
                    pi.color = new Color(0.05f, 0.04f, 0.03f);
                    pi.raycastTarget = false;
                }
            }
        }

        var snake = cont.gameObject.AddComponent<MenuSnake>();
        snake.Init(cont, dots, sizes, 110f, 42f);
    }

    // ------------------------------------------------------------ options
    RectTransform _optionsRoot, _creditsRoot;

    void BuildOptionsPage(Transform canvas)
    {
        var root = ModalRoot(canvas, "OptionsPage", out var panel);

        // header
        var hRt = Rect(panel, "Header", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                       new Vector2(0f, 190f), new Vector2(560f, 60f));
        var h = NewText(hRt, "OPTIONS", 44, Gold, TextAnchor.MiddleCenter, true);
        h.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -3f);
        h.gameObject.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.7f);

        Hairline(panel, 155f);

        // music row
        BuildToggleRow(panel, 70f, "M U S I C", MusicOn, on => PlayerPrefs.SetInt("MusicOn", on ? 1 : 0));
        // sfx row
        BuildToggleRow(panel, -25f, "S O U N D   E F F E C T S", SfxOn, on => PlayerPrefs.SetInt("SfxOn", on ? 1 : 0));

        // note removed: audio now exists (was "audio arrives in a future update...")

        // back
        var bRt = Rect(panel, "BackBtn", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                       new Vector2(0f, -195f), new Vector2(230f, 54f));
        MakeMenuButton(bRt, "B A C K", BtnFill, BtnFillHover, Cream, 23, ClosePage, true);

        _optionsRoot = root;
        root.gameObject.SetActive(false);
    }

    void BuildCreditsPage(Transform canvas)
    {
        var root = ModalRoot(canvas, "CreditsPage", out var panel);

        var hRt = Rect(panel, "Header", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                       new Vector2(0f, 200f), new Vector2(560f, 60f));
        var h = NewText(hRt, "CREDITS", 44, Gold, TextAnchor.MiddleCenter, true);
        h.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(2f, -3f);
        h.gameObject.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.7f);

        Hairline(panel, 165f);

        var byRt = Rect(panel, "MadeBy", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                        new Vector2(0f, 128f), new Vector2(560f, 30f));
        NewText(byRt, "M A D E   B Y", 19, new Color(0.70f, 0.72f, 0.66f, 0.9f), TextAnchor.MiddleCenter, false);

        var nameRt = Rect(panel, "Name", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                          new Vector2(0f, 72f), new Vector2(560f, 84f));
        var name = NewText(nameRt, "Rajat", 68, Cream, TextAnchor.MiddleCenter, true);
        name.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(3f, -4f);
        name.gameObject.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.75f);

        var rib = Rect(panel, "Ribbon", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                       new Vector2(0f, 24f), new Vector2(300f, 12f));
        var ribImg = rib.gameObject.AddComponent<Image>();
        ribImg.sprite = PatternRibbon();
        ribImg.type = Image.Type.Tiled;
        ribImg.raycastTarget = false;

        var msgRt = Rect(panel, "Message", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                         new Vector2(0f, -62f), new Vector2(580f, 130f));
        var msg = NewText(msgRt,
            "Thanks for playing!\n\nGrass, rocks, fence and one determined snake -\nbuilt in Unity, with care.",
            21, new Color(0.90f, 0.87f, 0.79f, 0.95f), TextAnchor.MiddleCenter, false);
        msg.lineSpacing = 1.3f;

        var hintRt = Rect(panel, "Hint", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                          new Vector2(0f, -152f), new Vector2(560f, 26f));
        NewText(hintRt, "press  ESC  to go back", 16, new Color(0.65f, 0.67f, 0.60f, 0.85f),
                TextAnchor.MiddleCenter, false);

        var bRt = Rect(panel, "BackBtn", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                       new Vector2(0f, -205f), new Vector2(230f, 54f));
        MakeMenuButton(bRt, "B A C K", BtnFill, BtnFillHover, Cream, 23, ClosePage, true);

        _creditsRoot = root;
        root.gameObject.SetActive(false);
    }

    RectTransform ModalRoot(Transform canvas, string name, out RectTransform panel)
    {
        var root = Rect(canvas, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                        Vector2.zero, Vector2.zero);
        root.gameObject.AddComponent<CanvasGroup>();

        var back = Img(root, "Backdrop", null, new Color(0.005f, 0.012f, 0.006f, 0.68f), true);
        Stretch(back.rectTransform);

        panel = Rect(root, "Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Center,
                     Vector2.zero, new Vector2(700f, 500f));
        var pimg = panel.gameObject.AddComponent<Image>();
        pimg.sprite = Rounded(700, 500, 26f, Color.white, 3f, BtnBorder, "modal700");
        pimg.color = PanelFill;

        // grass texture inside the panel (the real floor grass, low opacity)
        Sprite grassSpr = TiledFromTex("Menu/grass", 4f);
        if (grassSpr != null)
        {
            var g = Img(panel, "GrassTex", grassSpr, new Color(1f, 1f, 1f, 0.09f), false);
            Stretch(g.rectTransform);
            g.type = Image.Type.Tiled;
        }
        return root;
    }

    void Hairline(RectTransform panel, float y)
    {
        var hl = Rect(panel, "Hairline", Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                      new Vector2(0f, y), new Vector2(560f, 2f));
        var i = hl.gameObject.AddComponent<Image>();
        i.color = new Color(Gold.r, Gold.g, Gold.b, 0.55f);
        i.raycastTarget = false;
    }

    void BuildToggleRow(RectTransform panel, float y, string label, bool on,
                        System.Action<bool> save)
    {
        var row = Rect(panel, "Row_" + label, Anchor(0.5f, 0.5f), Anchor(0.5f, 0.5f), Center,
                       new Vector2(0f, y), new Vector2(560f, 70f));

        var lRt = Rect(row, "Label", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                       new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(340f, 44f));
        NewText(lRt, label, 25, Cream, TextAnchor.MiddleLeft, true);

        // pill
        var pill = Rect(row, "Pill", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                        new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(140f, 52f));
        var pbg = pill.gameObject.AddComponent<Image>();
        pbg.sprite = Rounded(140, 52, 26f, Color.white, 0f, Color.clear, "pill");
        pbg.color = on ? Gold : new Color(0.22f, 0.20f, 0.18f, 0.9f);
        var pb = pill.gameObject.AddComponent<Button>();
        pb.transition = Selectable.Transition.None;
        pb.navigation = new Navigation { mode = Navigation.Mode.None };

        var knobRt = Rect(pill, "Knob", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                          new Vector2(0.5f, 0.5f), new Vector2(on ? 44f : -44f, 0f), new Vector2(44f, 44f));
        var knob = knobRt.gameObject.AddComponent<Image>();
        knob.sprite = Rounded(44, 44, 22f, Color.white, 0f, Color.clear, "knob");
        knob.color = new Color(0.98f, 0.96f, 0.90f, 0.98f);
        knob.raycastTarget = false;

        var tog = pill.gameObject.AddComponent<MenuToggle>();
        tog.Init(pbg, knobRt, on, v => { save(v); PlayerPrefs.Save(); UiClick(); SoundManager.RefreshPrefs(); });
        pb.onClick.AddListener(tog.Toggle);
    }

    // ============================================================== actions
    void ShowPage(string which)
    {
        if (_phase != 0 || _activePage != null) return;
        var page = which == "options" ? _optionsRoot : _creditsRoot;
        if (page == null) return;
        _activePage = page.gameObject;
        _activePanel = page.Find("Panel") as RectTransform;
        _activePage.SetActive(true);
        var cg = _activePage.GetComponent<CanvasGroup>();
        if (cg != null) { cg.alpha = 0f; cg.blocksRaycasts = true; }
        _pageT = 0f;
        _pageOpening = true;
    }

    void ClosePage()
    {
        if (_activePage == null) return;
        _pageOpening = false;
    }

    void LaunchGame()
    {
        if (_phase != 0) return;
        _phase = 1;
        _fadeTarget = 1f;
        if (_mainPage != null) _mainPage.gameObject.SetActive(false);
        if (_activePage != null) { _activePage.SetActive(false); _activePage = null; }
    }

    void DoExit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ============================================================== update
    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // staggered entrance of the title / buttons / card
        if (_enter.Count > 0)
        {
            _enterT += dt;
            bool done = true;
            for (int i = 0; i < _enter.Count; i++)
            {
                var e = _enter[i];
                float p = Mathf.Clamp01((_enterT - e.delay) / 0.5f);
                if (p < 1f) done = false;
                float ease = 1f - Mathf.Pow(1f - p, 3);
                e.cg.alpha = ease;
                e.rt.anchoredPosition = e.pos + e.slide * (1f - ease);
            }
            if (done)
            {
                for (int i = 0; i < _enter.Count; i++)
                {
                    _enter[i].cg.alpha = 1f;
                    _enter[i].rt.anchoredPosition = _enter[i].pos;
                }
                _enter.Clear();
            }
        }

        // fade in / out (menu runs on unscaled time)
        if (!Mathf.Approximately(_fadeA, _fadeTarget))
        {
            _fadeA = Mathf.MoveTowards(_fadeA, _fadeTarget, dt * 2.4f);
            if (_fade != null)
            {
                var c = _fade.color; c.a = _fadeA; _fade.color = c;
                _fade.raycastTarget = _fadeA > 0.02f;
            }
        }

        if (_phase == 1 && _fadeA >= 0.999f && Mathf.Approximately(_fadeA, _fadeTarget))
        {
            // black reached: hand over to the game
            Open = false;
            Time.timeScale = 1f;
            if (_gm == null) _gm = FindFirstObjectByType<GameManager>();
            // a round already in progress (ESC-pause) resumes where it left
            // off; otherwise this is the first launch / a restart.
            if (_gm != null && !_gm.InProgress) _gm.StartGame();
            _phase = 2;
            _fadeTarget = 0f;
        }
        else if (_phase == 2 && _fadeA <= 0.001f && Mathf.Approximately(_fadeA, _fadeTarget))
        {
            Destroy(gameObject);          // fully revealed the game
        }

        // modal page pop-in / pop-out
        if (_activePage != null)
        {
            _pageT = Mathf.Clamp01(_pageT + dt * (_pageOpening ? 4.5f : -5.5f));
            float e = 1f - Mathf.Pow(1f - _pageT, 3);
            var cg = _activePage.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = e;
            if (_activePanel != null) _activePanel.localScale = Vector3.one * Mathf.Lerp(0.90f, 1f, e);
            if (!_pageOpening && _pageT <= 0f)
            {
                _activePage.SetActive(false);
                _activePage = null;
                _activePanel = null;
            }
        }

        // keys
        if (_phase == 0)
        {
            if (_activePage != null && Input.GetKeyDown(KeyCode.Escape)) ClosePage();
            else if (_activePage == null && Input.GetKeyDown(KeyCode.Return)) LaunchGame();
        }
    }

    // ============================================================== helpers
    static Vector2 Anchor(float x, float y) => new Vector2(x, y);
    static readonly Vector2 Center = new Vector2(0.5f, 0.5f);

    RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax,
                       Vector2 pivot, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = Vector2.zero;
    }

    Image Img(Transform parent, string name, Sprite spr, Color col, bool raycast)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = spr;
        if (spr == null) img.useSpriteMesh = false;
        img.color = col;
        img.raycastTarget = raycast;
        return img;
    }

    Text NewText(RectTransform rt, string str, int size, Color col, TextAnchor align, bool bold)
    {
        var t = rt.gameObject.AddComponent<Text>();
        t.font = Fnt;
        t.text = str;
        t.fontSize = size;
        t.color = col;
        t.alignment = align;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    Sprite Tex(string path)
    {
        var key = "tex:" + path;
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        var t = Resources.Load<Texture2D>(path);
        if (t == null) return null;
        s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
        Cache[key] = s;
        return s;
    }

    Sprite TiledFromTex(string path, float ppu)
    {
        var key = "tile:" + path + ":" + ppu;
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        var t = Resources.Load<Texture2D>(path);
        if (t == null) return null;
        t.wrapMode = TextureWrapMode.Repeat;
        s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), ppu);
        Cache[key] = s;
        return s;
    }

    // rounded rectangle sprite with optional inner border
    static Sprite Rounded(int w, int h, float radius, Color fill, float borderW, Color border, string key)
    {
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color32[w * h];
        float hw = w * 0.5f - 0.5f, hh = h * 0.5f - 0.5f;
        float r = Mathf.Min(radius, Mathf.Min(hw, hh));
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float cx = x - (w - 1) * 0.5f;
            float cy = y - (h - 1) * 0.5f;
            float qx = Mathf.Abs(cx) - (hw - r);
            float qy = Mathf.Abs(cy) - (hh - r);
            float ax = Mathf.Max(qx, 0f), ay = Mathf.Max(qy, 0f);
            float d = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            float a = Mathf.Clamp01(0.5f - d);
            Color c = fill;
            if (borderW > 0.5f)
            {
                float t = Mathf.Clamp01(-d / borderW);
                c = Color.Lerp(border, fill, t);
            }
            c.a *= a;
            px[y * w + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        Cache[key] = s;
        return s;
    }

    static Sprite Gradient(Color top, Color bot)
    {
        const string key = "grad";
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        var tex = new Texture2D(4, 128, TextureFormat.RGBA32, false);
        for (int y = 0; y < 128; y++)
        {
            var c = Color.Lerp(bot, top, y / 127f);
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, c);
        }
        tex.Apply(false, false);
        s = Sprite.Create(tex, new Rect(0, 0, 4, 128), new Vector2(0.5f, 0.5f), 100f);
        Cache[key] = s;
        return s;
    }

    static Sprite Vignette(float strength)
    {
        const string key = "vig";
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        const int W = 192, H = 108;
        var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float nx = (x / (W - 1f) - 0.5f) * 2f;
            float ny = (y / (H - 1f) - 0.5f) * 2f;
            float d = Mathf.Sqrt(nx * nx + ny * ny) / 1.4142f;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.32f, 1f, d)) * strength;
            tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
        }
        tex.Apply(false, false);
        s = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100f);
        Cache[key] = s;
        return s;
    }

    static Sprite Glow(Color col)
    {
        const string key = "glow";
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        const int S = 160;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float nx = (x / (S - 1f) - 0.5f) * 2f;
            float ny = (y / (S - 1f) - 0.5f) * 2f;
            float d = Mathf.Sqrt(nx * nx + ny * ny);
            float a = Mathf.Clamp01(1f - d);
            a = a * a;
            tex.SetPixel(x, y, new Color(col.r, col.g, col.b, col.a * a));
        }
        tex.Apply(false, false);
        s = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        Cache[key] = s;
        return s;
    }

    // actual snake-skin pattern as a tiled ribbon
    static Sprite PatternRibbon()
    {
        const string key = "prib";
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        var tex = SnakeSkin.Pattern;
        s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 4f);
        Cache[key] = s;
        return s;
    }

    // circular crop of the snake skin (dorsal diamond band) for the decor dots
    static Sprite PatternDot()
    {
        const string key = "pdot";
        Sprite s;
        if (Cache.TryGetValue(key, out s)) return s;
        var pat = SnakeSkin.Pattern;
        int srcS = pat.width;
        var src = pat.GetPixels32();
        const int N = 96;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        var dst = new Color32[N * N];
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float cx = x - (N - 1) * 0.5f;
            float cy = y - (N - 1) * 0.5f;
            float d = Mathf.Sqrt(cx * cx + cy * cy) - (N * 0.5f - 2f);
            float a = Mathf.Clamp01(0.5f - d);
            int sx = ((srcS - 48) + x * srcS / N) % srcS;   // window centred on the dorsal ridge (x=0)
            int sy = (y * srcS / N) % srcS;
            var c = src[sy * srcS + sx];
            c.a = (byte)Mathf.RoundToInt(c.a * a);
            dst[y * N + x] = c;
        }
        tex.SetPixels32(dst);
        tex.Apply(false, false);
        s = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        Cache[key] = s;
        return s;
    }

    // small soft circle used by the ambient floating specks
    public static Sprite SpeckDot()
    {
        return Rounded(16, 16, 8f, Color.white, 0f, Color.clear, "speck");
    }
}

// ============================================================================
//  Hover feedback for menu buttons
// ============================================================================
public class HoverFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                       IPointerDownHandler, IPointerUpHandler
{
    Image _img;
    RectTransform _rt;
    Color _base, _hover;
    bool _hov, _down;
    public float hoverScale = 1.035f;

    public void Init(Image img, Color baseCol, Color hoverCol, RectTransform rt)
    {
        _img = img; _base = baseCol; _hover = hoverCol; _rt = rt;
    }

    public void OnPointerEnter(PointerEventData e) { _hov = true; MainMenu.UiHover(); }
    public void OnPointerExit(PointerEventData e) { _hov = false; _down = false; }
    public void OnPointerDown(PointerEventData e) { _down = true; }
    public void OnPointerUp(PointerEventData e) { _down = false; }

    void Update()
    {
        if (_img == null) return;
        float k = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
        Color tgt = _hov ? _hover : _base;
        if (_down) tgt = Color.Lerp(tgt, Color.black, 0.18f);
        _img.color = Color.Lerp(_img.color, tgt, k);
        Vector3 s = _hov ? Vector3.one * (_down ? 0.985f : hoverScale) : Vector3.one;
        _rt.localScale = Vector3.Lerp(_rt.localScale, s, k);
    }
}

// ============================================================================
//  Sliding pill toggle for the options page
// ============================================================================
public class MenuToggle : MonoBehaviour
{
    Image _bg;
    RectTransform _knob;
    bool _on;
    float _travel = 44f;
    Color _onCol = new Color(0.94f, 0.76f, 0.29f);
    Color _offCol = new Color(0.22f, 0.20f, 0.18f, 0.9f);
    System.Action<bool> _changed;

    public void Init(Image bg, RectTransform knob, bool on, System.Action<bool> changed)
    {
        _bg = bg; _knob = knob; _on = on; _changed = changed;
        _bg.color = on ? _onCol : _offCol;
        var p = _knob.anchoredPosition;
        p.x = on ? _travel : -_travel;
        _knob.anchoredPosition = p;
    }

    public void Toggle()
    {
        _on = !_on;
        if (_changed != null) _changed(_on);
    }

    void Update()
    {
        if (_bg == null || _knob == null) return;
        float k = 1f - Mathf.Exp(-13f * Time.unscaledDeltaTime);
        _bg.color = Color.Lerp(_bg.color, _on ? _onCol : _offCol, k);
        var p = _knob.anchoredPosition;
        p.x = Mathf.Lerp(p.x, _on ? _travel : -_travel, k);
        _knob.anchoredPosition = p;
    }
}

// ============================================================================
//  The decorative snake that slithers across the menu (visual only)
// ============================================================================
public class MenuSnake : MonoBehaviour
{
    RectTransform _rt;
    Image[] _dots;
    float[] _sizes;
    float _baseY, _amp, _t;
    float _prevLen = 1920f;

    public void Init(RectTransform rt, Image[] dots, float[] sizes, float baseY, float amp)
    {
        _rt = rt; _dots = dots; _sizes = sizes; _baseY = baseY; _amp = amp;
    }

    void Update()
    {
        if (_dots == null) return;
        float dt = Time.unscaledDeltaTime;
        _t += dt * 0.05f;
        if (_t > 1.4f) _t -= 1.4f;

        float w = _rt != null ? _rt.rect.width : 0f;
        if (w < 100f) w = 1920f;
        float span = w + 240f;
        _prevLen = span;

        float p = _t;
        for (int i = 0; i < _dots.Length; i++)
        {
            if (i > 0)
            {
                float gapPx = (_sizes[i - 1] + _sizes[i]) * 0.44f;
                p -= gapPx / span;
            }
            var d = _dots[i];
            if (d == null) continue;
            bool vis = p > -0.03f && p < 1.07f;
            if (d.gameObject.activeSelf != vis) d.gameObject.SetActive(vis);
            if (!vis) continue;

            float fade = 1f;
            if (p < 0.06f) fade = Mathf.Clamp01(p / 0.06f);
            else if (p > 0.99f) fade = Mathf.Clamp01((1.07f - p) / 0.08f);
            var col = d.color;
            col.a = fade;
            d.color = col;

            float x = -120f + p * span;
            float y = _baseY + Mathf.Sin(p * 14.5f + 1.1f) * _amp;
            d.rectTransform.anchoredPosition = new Vector2(x, y);
        }
    }
}

// ============================================================================
//  Ambient floating specks - slow drifting motes that keep the dark areas
//  of the menu alive (gold / grass-green / cream, low opacity).
// ============================================================================
public class MenuSpecks : MonoBehaviour
{
    RectTransform _rt;
    Image[] _dots;
    float[] _nx, _ny, _rise, _sway, _ph, _ab;
    float _t;

    public void Init(RectTransform rt)
    {
        _rt = rt;
        var rnd = new System.Random(20260927);
        const int N = 16;
        _dots = new Image[N];
        _nx = new float[N]; _ny = new float[N]; _rise = new float[N];
        _sway = new float[N]; _ph = new float[N]; _ab = new float[N];

        for (int i = 0; i < N; i++)
        {
            _nx[i] = (float)rnd.NextDouble() * 0.96f + 0.02f;
            _ny[i] = (float)rnd.NextDouble() * 0.96f + 0.02f;
            _rise[i] = 3f + (float)rnd.NextDouble() * 8f;
            _sway[i] = 0.25f + (float)rnd.NextDouble() * 0.5f;
            _ph[i] = (float)rnd.NextDouble() * 6.28f;
            _ab[i] = 0.10f + (float)rnd.NextDouble() * 0.20f;
            float size = 4f + (float)rnd.NextDouble() * 5f;

            var g = new GameObject("S" + i, typeof(RectTransform));
            var r = (RectTransform)g.transform;
            r.SetParent(rt, false);
            r.anchorMin = r.anchorMax = new Vector2(0f, 0f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(size, size);

            var img = g.AddComponent<Image>();
            img.sprite = MainMenu.SpeckDot();
            img.raycastTarget = false;

            int kind = rnd.Next(10);
            Color c = kind < 6 ? new Color(0.95f, 0.78f, 0.32f)
                    : kind < 9 ? new Color(0.55f, 0.85f, 0.45f)
                               : new Color(0.95f, 0.92f, 0.80f);
            c.a = _ab[i];
            img.color = c;
            _dots[i] = img;
        }
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        float w = _rt != null ? _rt.rect.width : 0f;
        float h = _rt != null ? _rt.rect.height : 0f;
        if (w < 100f || h < 100f) return;

        for (int i = 0; i < _dots.Length; i++)
        {
            float y = (_ny[i] * h + _t * _rise[i]) % (h + 60f) - 30f;
            float x = _nx[i] * w + Mathf.Sin(_t * _sway[i] + _ph[i]) * 14f;
            var d = _dots[i];
            d.rectTransform.anchoredPosition = new Vector2(x, y);
            var c = d.color;
            c.a = _ab[i] * (0.55f + 0.45f * Mathf.Sin(_t * 0.7f + _ph[i] * 2f));
            d.color = c;
        }
    }
}
