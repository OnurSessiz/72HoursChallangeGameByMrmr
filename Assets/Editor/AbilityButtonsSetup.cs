using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekranın alt-ortasına Dash ve Dodge için cooldown ikonları oluşturur ve
/// PlayerController'a bağlar. Radial dönen karartma + geri sayım + hazır nabzı.
/// Sadece yerleşik uGUI; dış asset gerekmez. Menü: Tools/Player/Create Ability Buttons.
/// </summary>
public static class AbilityButtonsSetup
{
    private static Sprite _sprite;
    private static Font _font;

    [MenuItem("Tools/Player/Create Ability Buttons")]
    public static void Create()
    {
        _sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        var player = Object.FindFirstObjectByType<PlayerController>();

        // --- Canvas ---
        var canvasGO = new GameObject("Ability HUD",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var cyan = new Color(0.28f, 0.78f, 1f);
        var amber = new Color(1f, 0.60f, 0.20f);

        BuildIcon(canvasGO.transform, player, AbilityCooldownUI.Ability.Dash, "DASH", "Shift", cyan, -70f);
        BuildIcon(canvasGO.transform, player, AbilityCooldownUI.Ability.Dodge, "DODGE", "Space", amber, 70f);

        if (player == null)
            Debug.LogWarning("Sahnede PlayerController bulunamadi. Ikonlarin 'Player' alanini elle ata.");

        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Ability Buttons");
        Selection.activeGameObject = canvasGO;
        Debug.Log("Ability cooldown ikonlari olusturuldu ve baglandi.");
    }

    private static void BuildIcon(Transform parent, PlayerController player,
        AbilityCooldownUI.Ability ability, string name, string keyHint, Color accent, float xPos)
    {
        const float size = 104f;

        // Kök (alt-orta)
        var root = NewRect(name + " Ability", parent);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(xPos, 90f);
        root.sizeDelta = new Vector2(size, size + 26f);

        // Gövde (punch/nabız burada)
        var body = NewRect("Body", root);
        body.anchorMin = body.anchorMax = body.pivot = new Vector2(0.5f, 0f);
        body.anchoredPosition = new Vector2(0f, 26f);
        body.sizeDelta = new Vector2(size, size);

        // Glow (arkada, hazırken nabız)
        var glow = NewImage("Glow", body, _sprite, Image.Type.Sliced);
        Fit(glow.rectTransform, size + 22f);
        glow.color = new Color(accent.r, accent.g, accent.b, 0f);

        // Accent çerçeve
        var border = NewImage("Border", body, _sprite, Image.Type.Sliced);
        Fit(border.rectTransform, size);
        border.color = accent;

        // Koyu iç zemin (çerçeveden içeri)
        var bg = NewImage("BG", body, _sprite, Image.Type.Sliced);
        Fit(bg.rectTransform, size - 8f);
        bg.color = new Color(0.09f, 0.11f, 0.13f, 0.96f);

        // Glyph (isim)
        var glyph = NewText("Glyph", body, _font, name, 22, FontStyle.Bold);
        glyph.color = accent;
        Fit(glyph.rectTransform, size - 8f);

        // Radial dönen karartma
        var sweep = NewImage("Sweep", body, _sprite, Image.Type.Filled);
        Fit(sweep.rectTransform, size - 8f);
        sweep.color = new Color(0.02f, 0.03f, 0.05f, 0.72f);
        sweep.fillMethod = Image.FillMethod.Radial360;
        sweep.fillOrigin = (int)Image.Origin360.Top;
        sweep.fillClockwise = true;
        sweep.fillAmount = 0f;
        sweep.enabled = false;

        // Geri sayım
        var countdown = NewText("Countdown", body, _font, "", 40, FontStyle.Bold);
        countdown.color = Color.white;
        Fit(countdown.rectTransform, size);
        countdown.enabled = false;

        // Tuş ipucu (gövdenin altında)
        var hint = NewText("KeyHint", root, _font, keyHint, 18, FontStyle.Bold);
        hint.color = new Color(1f, 1f, 1f, 0.7f);
        var hintRt = hint.rectTransform;
        hintRt.anchorMin = hintRt.anchorMax = hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.anchoredPosition = new Vector2(0f, 2f);
        hintRt.sizeDelta = new Vector2(size, 22f);

        // Component + bağlama
        var ui = root.gameObject.AddComponent<AbilityCooldownUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("player").objectReferenceValue = player;
        so.FindProperty("ability").enumValueIndex = (int)ability;
        so.FindProperty("scaleTarget").objectReferenceValue = body;
        so.FindProperty("sweep").objectReferenceValue = sweep;
        so.FindProperty("countdown").objectReferenceValue = countdown;
        so.FindProperty("glyph").objectReferenceValue = glyph;
        so.FindProperty("glow").objectReferenceValue = glow;
        so.ApplyModifiedProperties();
    }

    // --- Yardımcılar ---

    private static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image NewImage(string name, Transform parent, Sprite sprite, Image.Type type)
    {
        var go = new GameObject(name, typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = type;
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
        t.alignment = TextAnchor.MiddleCenter;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    private static void Fit(RectTransform rt, float size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(size, size);
    }
}
