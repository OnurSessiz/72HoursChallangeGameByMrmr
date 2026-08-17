using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekrana sabit, "tatlı" bir can barı oluşturur: sol üstte yeşil ♥ kalp, yanında
/// yeşil dolgulu bar ve barın ortasında sayısal can (ör. "100 / 100").
/// Sadece Unity'nin yerleşik UI'ını kullanır; dış asset gerekmez.
/// Menü: Tools/Player/Create Health Bar.
/// </summary>
public static class HealthBarSetup
{
    private static readonly Color GreenFill = new Color(0.30f, 0.82f, 0.38f, 1f);

    [MenuItem("Tools/Player/Create Health Bar")]
    public static void Create()
    {
        var uiSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var font = GetBuiltinFont();

        // --- Canvas ---
        var canvasGO = new GameObject("HUD Canvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // --- Kalp (♥) sol üstte ---
        var heart = NewText("Heart", canvasGO.transform, font, "♥", 40, FontStyle.Bold);
        heart.color = GreenFill;
        heart.alignment = TextAnchor.MiddleCenter;
        var heartRt = heart.rectTransform;
        heartRt.anchorMin = heartRt.anchorMax = heartRt.pivot = new Vector2(0f, 1f);
        heartRt.anchoredPosition = new Vector2(40f, -36f);
        heartRt.sizeDelta = new Vector2(48f, 48f);

        // --- Bar arka planı (kalbin sağında) ---
        var barGO = new GameObject("HealthBar", typeof(Image));
        barGO.transform.SetParent(canvasGO.transform, false);
        var barRt = barGO.GetComponent<RectTransform>();
        barRt.anchorMin = barRt.anchorMax = barRt.pivot = new Vector2(0f, 1f);
        barRt.anchoredPosition = new Vector2(96f, -40f);
        barRt.sizeDelta = new Vector2(360f, 40f);
        var bg = barGO.GetComponent<Image>();
        bg.sprite = uiSprite;
        bg.type = Image.Type.Sliced;
        bg.color = new Color(0.08f, 0.10f, 0.09f, 0.75f);

        // --- Yeşil dolgu ---
        var fillGO = new GameObject("Fill", typeof(Image));
        fillGO.transform.SetParent(barGO.transform, false);
        var fillRt = fillGO.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(4f, 4f);
        fillRt.offsetMax = new Vector2(-4f, -4f);
        var fill = fillGO.GetComponent<Image>();
        fill.sprite = uiSprite;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        fill.color = GreenFill;

        // --- Ortadaki sayı (dolgunun üstünde) ---
        var label = NewText("Label", barGO.transform, font, "100 / 100", 22, FontStyle.Bold);
        label.color = Color.white;
        label.alignment = TextAnchor.MiddleCenter;
        var labelRt = label.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = Vector2.zero;
        labelRt.offsetMax = Vector2.zero;

        // --- Bağla ---
        var ui = barGO.AddComponent<HealthBarUI>();
        var health = Object.FindFirstObjectByType<PlayerHealth>();
        var so = new SerializedObject(ui);
        so.FindProperty("health").objectReferenceValue = health;
        so.FindProperty("fill").objectReferenceValue = fill;
        so.FindProperty("label").objectReferenceValue = label;
        so.ApplyModifiedProperties();

        if (health == null)
            Debug.LogWarning("Sahnede PlayerHealth bulunamadi. Bar'in 'Health' alanini elle ata.");

        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Health Bar");
        Selection.activeGameObject = canvasGO;
        Debug.Log("Can bari olusturuldu ve baglandi.");
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
