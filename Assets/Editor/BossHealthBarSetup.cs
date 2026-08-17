using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekranın üst ortasına kocaman bir boss can barı kurar: koyu çerçeve, kırmızı dolgu,
/// gecikmeli hasar izi, üstünde boss adı ve ortasında sayısal can.
/// Varsa sahnedeki HUD Canvas'ı kullanır, yoksa yenisini oluşturur.
/// Boss'ta Health yoksa onu da ekler (boss'a yakışır can değeriyle).
/// Menü: Tools/Boss/Create Boss Health Bar.
/// </summary>
public static class BossHealthBarSetup
{
    private static readonly Color RedFill = new Color(0.80f, 0.10f, 0.12f, 1f);
    private static readonly Color ChipColor = new Color(1f, 0.72f, 0.30f, 1f);
    private static readonly Color FrameColor = new Color(0.05f, 0.04f, 0.05f, 0.88f);

    private const float BarWidth = 1200f;
    private const float BarHeight = 54f;

    [MenuItem("Tools/Boss/Create Boss Health Bar")]
    public static void Create()
    {
        var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var font = GetBuiltinFont();

        Canvas canvas = FindHudCanvas();
        GameObject createdRoot = null;

        if (canvas == null)
        {
            var canvasGO = new GameObject("HUD Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            createdRoot = canvasGO;
        }

        // Aynı bar iki kez kurulmasın.
        var existing = canvas.GetComponentInChildren<BossHealthBarUI>(true);
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[BossHealthBarSetup] Boss can bari zaten var; mevcut olan secildi.", existing);
            return;
        }

        // --- Bar kökü: üst orta, kocaman ---
        var barGO = new GameObject("BossHealthBar", typeof(Image), typeof(CanvasGroup));
        barGO.transform.SetParent(canvas.transform, false);
        var barRt = barGO.GetComponent<RectTransform>();
        barRt.anchorMin = barRt.anchorMax = barRt.pivot = new Vector2(0.5f, 1f);
        barRt.anchoredPosition = new Vector2(0f, -84f);
        barRt.sizeDelta = new Vector2(BarWidth, BarHeight);

        var frame = barGO.GetComponent<Image>();
        frame.sprite = uiSprite;
        frame.type = Image.Type.Sliced;
        frame.color = FrameColor;
        frame.raycastTarget = false;

        // --- Hasar izi (dolgunun arkasında kalsın diye önce eklenir) ---
        Image chip = NewFill("Chip", barGO.transform, uiSprite, ChipColor);

        // --- Kırmızı dolgu ---
        Image fill = NewFill("Fill", barGO.transform, uiSprite, RedFill);

        // --- Boss adı (barın üstünde) ---
        var nameLabel = NewText("Name", barGO.transform, font, "BOSS", 40, FontStyle.Bold);
        nameLabel.color = new Color(0.95f, 0.85f, 0.85f);
        nameLabel.alignment = TextAnchor.MiddleCenter;
        var nameRt = nameLabel.rectTransform;
        nameRt.anchorMin = new Vector2(0f, 1f);
        nameRt.anchorMax = new Vector2(1f, 1f);
        nameRt.pivot = new Vector2(0.5f, 0f);
        nameRt.offsetMin = new Vector2(0f, 8f);
        nameRt.offsetMax = new Vector2(0f, 56f);

        // --- Sayısal can (barın ortasında) ---
        var valueLabel = NewText("Value", barGO.transform, font, "500 / 500", 24, FontStyle.Bold);
        valueLabel.color = Color.white;
        valueLabel.alignment = TextAnchor.MiddleCenter;
        var valueRt = valueLabel.rectTransform;
        valueRt.anchorMin = Vector2.zero;
        valueRt.anchorMax = Vector2.one;
        valueRt.offsetMin = Vector2.zero;
        valueRt.offsetMax = Vector2.zero;

        // --- Boss'u bul, gerekiyorsa Health ekle ---
        var bossFight = Object.FindAnyObjectByType<BossFight>();
        Health bossHealth = null;

        if (bossFight != null)
        {
            bossHealth = bossFight.GetComponent<Health>();
            if (bossHealth == null)
            {
                bossHealth = Undo.AddComponent<Health>(bossFight.gameObject);
                var hso = new SerializedObject(bossHealth);
                hso.FindProperty("maxHealth").floatValue = 500f;
                hso.FindProperty("destroyDelay").floatValue = 6f;
                hso.FindProperty("knockbackMultiplier").floatValue = 0.2f;
                hso.ApplyModifiedProperties();
                Debug.Log("[BossHealthBarSetup] Boss'a Health eklendi (500 can).", bossFight);
            }
        }
        else
        {
            Debug.LogWarning("[BossHealthBarSetup] Sahnede BossFight bulunamadi. " +
                             "Barin 'Boss Health' ve 'Boss Fight' alanlarini elle ata.");
        }

        // --- Bağla ---
        var ui = barGO.AddComponent<BossHealthBarUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("bossHealth").objectReferenceValue = bossHealth;
        so.FindProperty("bossFight").objectReferenceValue = bossFight;
        so.FindProperty("group").objectReferenceValue = barGO.GetComponent<CanvasGroup>();
        so.FindProperty("fill").objectReferenceValue = fill;
        so.FindProperty("chip").objectReferenceValue = chip;
        so.FindProperty("nameLabel").objectReferenceValue = nameLabel;
        so.FindProperty("valueLabel").objectReferenceValue = valueLabel;
        so.ApplyModifiedProperties();

        Undo.RegisterCreatedObjectUndo(createdRoot != null ? createdRoot : barGO, "Create Boss Health Bar");
        Selection.activeGameObject = barGO;
        Debug.Log("[BossHealthBarSetup] Boss can bari olusturuldu ve baglandi.");
    }

    /// <summary>Sahnedeki ekran-uzayı Canvas'ını bulur (HUD Canvas öncelikli).</summary>
    private static Canvas FindHudCanvas()
    {
        Canvas fallback = null;

        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            if (c.name == "HUD Canvas") return c;
            if (fallback == null) fallback = c;
        }

        return fallback;
    }

    /// <summary>Bar dolgusu: kenarlardan içeri girintili, yatay Filled Image.</summary>
    private static Image NewFill(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(5f, 5f);
        rt.offsetMax = new Vector2(-5f, -5f);

        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static Text NewText(string name, Transform parent, Font font, string text, int size, FontStyle style)
    {
        var go = new GameObject(name, typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static Font GetBuiltinFont()
    {
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }
}
