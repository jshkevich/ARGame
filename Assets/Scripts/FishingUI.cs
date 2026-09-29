using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Мобильный UI для AR-рыбалки. Весь Canvas строится кодом —
/// ничего перетаскивать в Inspector не нужно. Большие кнопки под палец.
/// </summary>
public class FishingUI : MonoBehaviour
{
    Text hintText;
    Text scoreText;
    Text fishLabel;
    Button actionButton;
    Image actionButtonImage;
    Text actionLabel;
    Button replaceButton;
    Slider tensionSlider;
    GameObject biteFlash;
    Text centerText;
    Sprite roundSprite;

    System.Action onAction;
    float flashTimer;

    void Awake()
    {
        BuildUI();
    }

    void Update()
    {
        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            if (biteFlash != null)
                biteFlash.SetActive(Mathf.Sin(Time.time * 15f) > 0f && flashTimer > 0f);
            if (flashTimer <= 0f && biteFlash != null)
                biteFlash.SetActive(false);
        }
    }

    void BuildUI()
    {
        // EventSystem чиним через EventSystemFixer (сносит StandaloneInputModule,
        // который падает при New Input System). Здесь — страховка.
        EventSystemFixer.FixAll();
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            GameObject es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        GameObject canvasGo = new GameObject("FishingCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(720, 1280);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Палитра
        Color cPanel = new Color(0.04f, 0.09f, 0.14f, 0.72f);
        Color cPanelSoft = new Color(0.04f, 0.09f, 0.14f, 0.45f);
        Color cAccent = new Color(0.16f, 0.78f, 0.55f);
        Color cAccentDark = new Color(0.09f, 0.48f, 0.34f);
        Color cWarn = new Color(1f, 0.35f, 0.32f);
        Color cText = new Color(0.96f, 0.98f, 1f);
        Color cTextDim = new Color(0.78f, 0.86f, 0.92f);

        // --- Верхняя панель: счёт ---
        // Высота задаётся долей экрана, а не пикселями: иначе на нестандартном
        // соотношении сторон (в редакторе окно обычно шире, чем телефон) панель
        // растягивалась и наезжала на кнопки под собой.
        GameObject top = NewPanel("TopPanel", canvasGo.transform, cPanel);
        TopBlockP(top.GetComponent<RectTransform>(), 16f, 16f, 10f, 0.145f);
        ApplyRound(top, 26f);

        // Счёт — крупно слева, левая половина панели
        GameObject scoreGo = NewText("Score", top.transform, cText, 46, FontStyle.Bold);
        TopBlockP(scoreGo.GetComponent<RectTransform>(), 0f, 0.5f, 22f, 0f, 0.46f, 0.94f);
        scoreText = scoreGo.GetComponent<Text>();
        scoreText.alignment = TextAnchor.MiddleLeft;
        scoreText.text = "0";

        // Подпись «рыб / рекорд» — правая половина той же строки
        GameObject fishGo = NewText("FishCount", top.transform, cTextDim, 27, FontStyle.Normal);
        TopBlockP(fishGo.GetComponent<RectTransform>(), 0.5f, 1f, 22f, 0f, 0.46f, 0.94f);
        fishLabel = fishGo.GetComponent<Text>();
        fishLabel.alignment = TextAnchor.MiddleRight;
        fishLabel.text = "рыб: 0   рекорд: 0";

        // Разделитель
        GameObject line = NewPanel("AccentLine", top.transform, cAccent);
        RectTransform lrt = line.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0.56f);
        lrt.anchorMax = new Vector2(0f, 0.56f);
        lrt.pivot = new Vector2(0f, 0.5f);
        lrt.anchoredPosition = new Vector2(24f, 0f);
        lrt.sizeDelta = new Vector2(64f, 4f);
        ApplyRound(line, 2f);

        // Подсказка — на всю ширину панели, под разделителем
        GameObject hintGo = NewText("Hint", top.transform, cText, 30, FontStyle.Normal);
        TopBlockP(hintGo.GetComponent<RectTransform>(), 0f, 1f, 20f, 0f, 0.06f, 0.44f);
        hintText = hintGo.GetComponent<Text>();
        hintText.alignment = TextAnchor.UpperCenter;
        hintText.lineSpacing = 1.2f;
        hintText.text = "";

        // --- Центральная надпись ---
        GameObject centerGo = NewText("Center", canvasGo.transform, cAccent, 60, FontStyle.Bold);
        RectTransform crt = centerGo.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0f, 0.34f); crt.anchorMax = new Vector2(1f, 0.66f);
        crt.offsetMin = Vector2.zero; crt.offsetMax = Vector2.zero;
        centerText = centerGo.GetComponent<Text>();
        centerText.alignment = TextAnchor.MiddleCenter;
        centerText.lineSpacing = 1.1f;
        centerText.gameObject.SetActive(false);

        // --- Вспышка КЛЁВ ---
        GameObject flashGo = NewPanel("BiteFlash", canvasGo.transform, new Color(cWarn.r, cWarn.g, cWarn.b, 0.30f));
        RectTransform frt = flashGo.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0.06f, 0.60f); frt.anchorMax = new Vector2(0.94f, 0.76f);
        frt.offsetMin = Vector2.zero; frt.offsetMax = Vector2.zero;
        ApplyRound(flashGo, 20f);

        GameObject flashTxtGo = NewText("Txt", flashGo.transform, Color.white, 66, FontStyle.Bold);
        Stretch(flashTxtGo.GetComponent<RectTransform>(), 8f, -8f, 8f, -8f);
        flashTxtGo.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
        biteFlash = flashGo;
        biteFlash.SetActive(false);

        // --- Шкала натяжения ---
        GameObject sliderGo = NewPanel("Tension", canvasGo.transform, cPanelSoft);
        RectTransform slrt = sliderGo.GetComponent<RectTransform>();
        slrt.anchorMin = new Vector2(0.10f, 0.245f); slrt.anchorMax = new Vector2(0.90f, 0.295f);
        slrt.offsetMin = Vector2.zero; slrt.offsetMax = Vector2.zero;
        ApplyRound(sliderGo, 14f);

        GameObject fill = NewPanel("Fill", sliderGo.transform, cAccent);
        RectTransform fillRT = fill.GetComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(4f, 4f); fillRT.offsetMax = new Vector2(-4f, -4f);
        ApplyRound(fill, 10f);

        GameObject fillImg = new GameObject("FillImg");
        fillImg.transform.SetParent(sliderGo.transform, false);
        var fiRT = fillImg.AddComponent<RectTransform>();
        fiRT.anchorMin = Vector2.zero; fiRT.anchorMax = Vector2.one;
        fiRT.offsetMin = new Vector2(4f, 4f); fiRT.offsetMax = new Vector2(-4f, -4f);
        var fillGraphic = fillImg.AddComponent<Image>();
        fillGraphic.color = new Color(1f, 1f, 1f, 0f); // невидимый, нужен только для графики
        var slider = sliderGo.AddComponent<Slider>();
        slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0f;
        slider.fillRect = fillRT;
        slider.targetGraphic = fillGraphic;
        tensionSlider = slider;
        sliderGo.SetActive(false);

        // --- Большая кнопка действия ---
        GameObject btnGo = NewPanel("ActionButton", canvasGo.transform, cAccent);
        RectTransform brt = btnGo.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.08f, 0.055f); brt.anchorMax = new Vector2(0.92f, 0.195f);
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;
        ApplyRound(btnGo, 24f);
        var btnShadow = btnGo.AddComponent<Shadow>();
        btnShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
        btnShadow.effectDistance = new Vector2(0f, -6f);
        actionButton = btnGo.AddComponent<Button>();
        actionButton.targetGraphic = btnGo.GetComponent<Image>();
        var colors = actionButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.fadeDuration = 0.08f;
        actionButton.colors = colors;
        actionButton.onClick.AddListener(() => onAction?.Invoke());
        actionButtonImage = btnGo.GetComponent<Image>();

        GameObject lblGo = NewText("Label", btnGo.transform, Color.white, 50, FontStyle.Bold);
        Stretch(lblGo.GetComponent<RectTransform>(), 8f, -8f, 4f, -4f);
        actionLabel = lblGo.GetComponent<Text>();
        actionLabel.alignment = TextAnchor.MiddleCenter;

        // --- Кнопка перестановки озера ---
        // Ниже верхней панели (она кончается на ~13% высоты), иначе наезжала на счёт
        GameObject repGo = NewPanel("ReplaceButton", canvasGo.transform, cPanelSoft);
        RectTransform rrt = repGo.GetComponent<RectTransform>();
        rrt.anchorMin = new Vector2(0.04f, 0.775f); rrt.anchorMax = new Vector2(0.27f, 0.835f);
        rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
        ApplyRound(repGo, 16f);
        replaceButton = repGo.AddComponent<Button>();
        replaceButton.targetGraphic = repGo.GetComponent<Image>();

        GameObject repLbl = NewText("Label", repGo.transform, cTextDim, 26, FontStyle.Bold);
        Stretch(repLbl.GetComponent<RectTransform>(), 6f, -6f, 4f, -4f);
        repLbl.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;
        repLbl.GetComponent<Text>().text = "Озеро";
    }

    // ---------- Вспомогательные конструкторы UI ----------

    Font GetFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    GameObject NewPanel(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    GameObject NewText(string name, Transform parent, Color color, int size, FontStyle style)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font = GetFont();
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.raycastTarget = false;
        t.supportRichText = true;
        return go;
    }

    /// <summary>
    /// Блок у верхнего края с высотой в долях экрана. Так раскладка не
    /// «едет» на нестандартном соотношении сторон (окно редактора шире телефона).
    /// </summary>
    void TopBlockP(RectTransform rt, float left, float right, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f - height);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, -top);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// Блок у верхнего края с явной долями разметкой:
    /// anchorMin=(left, bottom), anchorMax=(right, top).
    /// </summary>
    void TopBlockP(RectTransform rt, float left, float right, float padX, float padY,
                   float bottom, float top)
    {
        rt.anchorMin = new Vector2(left, bottom);
        rt.anchorMax = new Vector2(right, top);
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }

    /// <summary>Растянуть RectTransform на всю площадь с отступами.</summary>
    void Stretch(RectTransform rt, float left, float right, float bottom, float top)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>
    /// Настоящие скруглённые углы: генерируем спрайт со скруглением
    /// и растягиваем через 9-slice, поэтому углы не растягиваются.
    /// </summary>
    void ApplyRound(GameObject panel, float radius)
    {
        if (radius <= 1f) return;
        var img = panel.GetComponent<Image>();
        if (img == null) return;
        if (roundSprite == null) roundSprite = MakeRoundSprite(48, 24f);
        img.sprite = roundSprite;
        img.type = UnityEngine.UI.Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
    }

    Sprite MakeRoundSprite(int size, float radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "UI_Round",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        float r = Mathf.Min(radius, size * 0.5f);
        var px = new Color32[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Расстояние до ближайшего угла по знаковой координате
                float dx = Mathf.Max(r - x, x - (size - 1 - r), 0f);
                float dy = Mathf.Max(r - y, y - (size - 1 - r), 0f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                // Плавный переход на границе радиуса — сглаженный угол
                float a = Mathf.Clamp01(r - dist);
                byte alpha = (byte)Mathf.RoundToInt(a * 255f);
                px[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        tex.SetPixels32(px);
        tex.Apply();

        // Границы 9-slice = радиус, чтобы углы не растягивались
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(r, r, r, r));
    }

    // --- API для менеджера ---

    public void SetHint(string s) { if (hintText != null) hintText.text = s; }
    public void SetScore(int score, int count, int best)
    {
        if (scoreText != null)
            scoreText.text = $"<b>{score}</b>";
        if (fishLabel != null)
            fishLabel.text = $"рыб: {count}   рекорд: {best}";
    }

    public void SetAction(string label, System.Action cb, Color? color = null)
    {
        if (actionLabel != null) actionLabel.text = label;
        onAction = cb;
        if (color.HasValue && actionButtonImage != null)
            actionButtonImage.color = color.Value;
        if (actionButton != null) actionButton.interactable = !string.IsNullOrEmpty(label);
    }

    public void OnReplace(System.Action cb)
    {
        if (replaceButton != null)
        {
            replaceButton.onClick.RemoveAllListeners();
            replaceButton.onClick.AddListener(() => cb?.Invoke());
        }
    }

    public void ShowTension(bool show)
    {
        if (tensionSlider != null)
            tensionSlider.gameObject.SetActive(show);
    }

    public void SetTension(float v)
    {
        if (tensionSlider != null) tensionSlider.value = Mathf.Clamp01(v);
    }

    public void FlashBite(float duration)
    {
        flashTimer = duration;
    }

    public void ShowCenter(string text, Color color, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(CenterRoutine(text, color, duration));
    }

    System.Collections.IEnumerator CenterRoutine(string text, Color color, float duration)
    {
        centerText.text = text;
        centerText.color = color;
        centerText.gameObject.SetActive(true);
        // Пульсация
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float s = 1f + Mathf.Sin(t * 8f) * 0.05f;
            centerText.transform.localScale = Vector3.one * s;
            yield return null;
        }
        centerText.gameObject.SetActive(false);
        // сбрасываем таймер вспышки чтобы не мигала вечно
        flashTimer = 0f;
        if (biteFlash != null) biteFlash.SetActive(false);
    }

    /// <summary>Был ли тап по UI (чтобы не считать его забросом). Вызывать из менеджера.</summary>
    public static bool IsPointerOverUI()
    {
        return ARInput.IsPointerOverUI();
    }
}
