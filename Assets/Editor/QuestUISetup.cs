using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekranın sağ altına görev panelini kurar: koyu bir kutu, solunda kehribar şerit,
/// üstte "GÖREV" başlığı, altında gidilecek oda ve yapılacak iş, en altta kalan
/// düşman sayacı.
///
/// Sadece Unity'nin yerleşik uGUI'ı kullanılır; dış asset gerekmez (projedeki
/// diğer HUD araçlarıyla aynı yaklaşım).
///
/// Menü: Tools/UI/Create Quest HUD
///
/// Dört adımın metinleri Türkçe varsayılanlarla doldurulur; sonradan QuestUI'ın
/// entries listesinden değiştirebilirsin. Panel zaten varsa yeniden üretilmez.
/// </summary>
public static class QuestUISetup
{
    private static readonly Color Amber = new Color(1f, 0.60f, 0.20f);
    private static readonly Color PanelBg = new Color(0.08f, 0.09f, 0.11f, 0.82f);
    private static readonly Color TaskGray = new Color(0.85f, 0.87f, 0.90f);

    private const string CanvasName = "Quest HUD";
    private const float PanelWidth = 460f;
    private const float PanelHeight = 158f;
    private const float Padding = 18f;

    private static Sprite _sprite;
    private static Font _font;

    [MenuItem("Tools/UI/Create Quest HUD")]
    public static void Create()
    {
        var existing = Object.FindAnyObjectByType<QuestUI>();
        if (existing != null)
        {
            // Panel zaten kurulu: yeniden uretmek yerine gorev metinlerini tazeliyoruz,
            // boylece metin degisikligi icin Inspector'da ugrasmak gerekmiyor.
            Undo.RecordObject(existing, "Refresh Quest Texts");
            var existingSo = new SerializedObject(existing);
            WriteEntries(existingSo.FindProperty("entries"));
            existingSo.ApplyModifiedProperties();

            Selection.activeGameObject = existing.gameObject;
            Debug.Log("[QuestUISetup] Panel zaten vardi; gorev metinleri guncellendi.\n" +
                      "Sahneyi kaydetmeyi unutma.", existing);
            return;
        }

        _sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // --- Canvas ---
        var canvasGO = new GameObject(CanvasName,
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // --- Panel (sag alt) ---
        var panelGO = new GameObject("QuestPanel", typeof(Image));
        panelGO.transform.SetParent(canvasGO.transform, false);
        var panelRt = panelGO.GetComponent<RectTransform>();
        panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(1f, 0f);
        panelRt.anchoredPosition = new Vector2(-40f, 40f);
        panelRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);

        var bg = panelGO.GetComponent<Image>();
        bg.sprite = _sprite;
        bg.type = Image.Type.Sliced;
        bg.color = PanelBg;
        bg.raycastTarget = false;

        // --- Sol kenardaki kehribar serit ---
        var stripGO = new GameObject("Accent", typeof(Image));
        stripGO.transform.SetParent(panelGO.transform, false);
        var stripRt = stripGO.GetComponent<RectTransform>();
        stripRt.anchorMin = new Vector2(0f, 0f);
        stripRt.anchorMax = new Vector2(0f, 1f);
        stripRt.pivot = new Vector2(0f, 0.5f);
        stripRt.anchoredPosition = new Vector2(6f, 0f);
        stripRt.sizeDelta = new Vector2(5f, -16f);
        var strip = stripGO.GetComponent<Image>();
        strip.sprite = _sprite;
        strip.type = Image.Type.Sliced;
        strip.color = Amber;
        strip.raycastTarget = false;

        // --- Yazilar (ustten asagi) ---
        NewLabel("Header", panelGO.transform, "GÖREV", 16, FontStyle.Bold, Amber, -14f, 20f);

        Text room = NewLabel("Room", panelGO.transform, "Hırt Odası", 26, FontStyle.Bold,
                             Color.white, -38f, 32f);
        Text task = NewLabel("Task", panelGO.transform, "Odadaki bütün hırtları temizle", 19,
                             FontStyle.Normal, TaskGray, -74f, 26f);
        Text counter = NewLabel("Counter", panelGO.transform, "Kalan: 0 / 0", 18,
                                FontStyle.Bold, Amber, -108f, 24f);

        // --- Bagla ---
        var ui = panelGO.AddComponent<QuestUI>();
        var so = new SerializedObject(ui);
        so.FindProperty("panel").objectReferenceValue = panelGO;
        so.FindProperty("roomLabel").objectReferenceValue = room;
        so.FindProperty("taskLabel").objectReferenceValue = task;
        so.FindProperty("counterLabel").objectReferenceValue = counter;
        WriteEntries(so.FindProperty("entries"));
        so.ApplyModifiedProperties();

        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Quest HUD");
        Selection.activeGameObject = panelGO;

        if (Object.FindAnyObjectByType<GameProgress>() == null)
            Debug.LogWarning("[QuestUISetup] Sahnede GameProgress yok. " +
                             "Tools/Progression/Setup Game Flow calistir, yoksa panel bos kalir.");

        Debug.Log("[QuestUISetup] Gorev paneli olusturuldu ve dort adimin metinleri dolduruldu.\n" +
                  "Metinleri QuestPanel > QuestUI > Entries listesinden degistirebilirsin.\n" +
                  "Sahneyi kaydetmeyi unutma.", panelGO);
    }

    /// <summary>Dört adımın oda/görev metinlerini yazar.</summary>
    private static void WriteEntries(SerializedProperty entries)
    {
        (GameStage stage, string room, string task)[] defaults =
        {
            (GameStage.HirtRoom,  "Hırt Odası",    "Odadaki bütün hırtları temizle"),
            (GameStage.Swordsman, "Okyanus Odası", "Kara Kılıç Ustası'nı alt et - DİKKAT: su ölümcüldür!"),
            (GameStage.Boss,      "Boss Odası",    "Boss'u yen"),
            (GameStage.RockTrap,  "Kaya Tuzağı",   "Yokuşu tırman, kayalardan sağ çık")
        };

        entries.arraySize = defaults.Length;

        for (int i = 0; i < defaults.Length; i++)
        {
            SerializedProperty element = entries.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("stage").enumValueIndex = (int)defaults[i].stage;
            element.FindPropertyRelative("room").stringValue = defaults[i].room;
            element.FindPropertyRelative("task").stringValue = defaults[i].task;
        }
    }

    /// <summary>Panelin içinde, üstten verilen mesafede, sola hizalı bir yazı üretir.</summary>
    private static Text NewLabel(string name, Transform parent, string content, int size,
                                 FontStyle style, Color color, float topOffset, float height)
    {
        var go = new GameObject(name, typeof(Text));
        go.transform.SetParent(parent, false);

        // Yatayda panele yaslanir, dikeyde ustten topOffset kadar asagida ve height yuksekliginde
        // durur. Rect'i tamamen offset'lerle tanimliyoruz; anchoredPosition ile karistirmiyoruz.
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        // Sol kenardaki serit icin fazladan bosluk birakiyoruz.
        rt.offsetMin = new Vector2(Padding + 8f, topOffset - height);
        rt.offsetMax = new Vector2(-Padding, topOffset);

        var text = go.GetComponent<Text>();
        text.font = _font;
        text.text = content;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}
