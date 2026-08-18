using UnityEditor;
using UnityEngine;

/// <summary>
/// Portakal (can eşyası) kurulumu.
///
/// Menü:
///   Tools/World/Create Orange Pickup         -> Assets/Prefabs/OrangePickup.prefab üretir
///                                               (prefab varsa BOŞ efekt slotlarını doldurur)
///   Tools/World/Add Orange Drop To Selection -> seçili hırtlara LootDrop ekler
///
/// Üretilen prefab: PolyOne portakal modeli + modelin boyuna göre ölçülmüş trigger
/// SphereCollider + HealthPickup (25 can) + geçici efektler.
///
/// Efektler yalnızca BOŞ slotlara yazılır: kendi efektini atadıktan sonra aracı tekrar
/// çalıştırırsan seçimin korunur. Varsayılanlar projede zaten duran Eric VFX paketinden
/// seçilmiş yer tutuculardır.
/// </summary>
public static class PickupSetup
{
    private const string OrangeModelPath =
        "Assets/3rdPartyAssets/PolyOne/Free Fruits/Prefabs/SM_Fruits_Orange_1.prefab";
    private const string PrefabFolder = "Assets/Prefabs";
    private const string PickupPrefabPath = PrefabFolder + "/OrangePickup.prefab";

    private const string VfxFolder = "Assets/3rdPartyAssets/Eric VFX Studio/Free Game VFX/Prefab/";
    // Yer tutucu efektler: yerde beklerken loot ışığı, toplanınca yeşil patlama,
    // iyileşirken oyuncunun üzerinde daralan yeşil ışık.
    private const string IdleVfxPath = VfxFolder + "FX_LootDrop_Blue.prefab";
    private const string PickupVfxPath = VfxFolder + "FX_Green_Hit.prefab";
    private const string HealVfxPath = VfxFolder + "FX_Greenlight_shrink.prefab";

    private const float HealAmount = 25f;
    /// <summary>Toplama alanı modelden biraz geniş olsun ki oyuncu değdiğini hissetsin.</summary>
    private const float TriggerPadding = 0.35f;

    [MenuItem("Tools/World/Create Orange Pickup")]
    public static void CreateOrangePickup()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath);
        if (existing != null)
        {
            UpdateExistingPrefab();
            return;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(OrangeModelPath);
        if (model == null)
        {
            Debug.LogError($"Portakal modeli bulunamadi: {OrangeModelPath}\n" +
                           "PolyOne klasoru tasindiysa modeli sahneye surukleyip uzerine " +
                           "trigger Collider + HealthPickup ekle, sonra prefab yap.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        // Modeli sahnede kurup prefab'a kaydediyoruz (bileşenleri eklemenin tek yolu).
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        instance.name = "OrangePickup";
        // 3rdParty prefab'ina bagli kalmasin; kendi basina bir prefab olsun.
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

        var trigger = instance.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = MeasureRadius(instance) + TriggerPadding;
        trigger.center = MeasureCenter(instance);

        var pickup = instance.AddComponent<HealthPickup>();
        var so = new SerializedObject(pickup);
        so.FindProperty("healAmount").floatValue = HealAmount;
        FillEffectSlots(so);
        so.ApplyModifiedProperties();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, PickupPrefabPath);
        Object.DestroyImmediate(instance);

        Debug.Log($"[PickupSetup] Hazir: {PickupPrefabPath}\n" +
                  $"Trigger yaricapi {trigger.radius:0.00}, iyilestirme {HealAmount} can.\n" +
                  "Hirtlara Tools/World/Add Orange Drop To Selection ile bagla.", prefab);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    /// <summary>Var olan prefab'ın boş efekt slotlarını doldurur; dolu olanlara dokunmaz.</summary>
    private static void UpdateExistingPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PickupPrefabPath);

        try
        {
            var pickup = root.GetComponent<HealthPickup>();
            if (pickup == null) pickup = root.AddComponent<HealthPickup>();

            var so = new SerializedObject(pickup);
            int filled = FillEffectSlots(so);
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);

            Debug.Log($"[PickupSetup] {PickupPrefabPath} zaten vardi; {filled} bos efekt slotu dolduruldu " +
                      "(dolu slotlara dokunulmadi).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    /// <summary>Boş efekt slotlarına yer tutucu VFX'leri yazar. Dönüş: doldurulan slot sayısı.</summary>
    private static int FillEffectSlots(SerializedObject so)
    {
        int filled = 0;

        if (AssignIfEmpty(so, "idleVfxPrefab", IdleVfxPath)) filled++;
        if (AssignIfEmpty(so, "pickupVfxPrefab", PickupVfxPath)) filled++;
        if (AssignIfEmpty(so, "healVfxPrefab", HealVfxPath)) filled++;

        return filled;
    }

    private static bool AssignIfEmpty(SerializedObject so, string propertyName, string assetPath)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue != null) return false;

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null)
        {
            Debug.LogWarning($"Efekt bulunamadi: {assetPath}\n'{propertyName}' bos birakildi; " +
                             "kendi efektini Inspector'dan surukleyebilirsin.");
            return false;
        }

        property.objectReferenceValue = asset;
        return true;
    }

    /// <summary>
    /// Seçili hırtlara LootDrop ekler ve portakal prefab'ını bağlar. Yalnızca sopalı
    /// hırtları seçmen yeterli; var olan LootDrop ayarları ezilmez.
    /// </summary>
    [MenuItem("Tools/World/Add Orange Drop To Selection")]
    public static void AddOrangeDropToSelection()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"{PickupPrefabPath} yok. Once Tools/World/Create Orange Pickup calistir.");
            return;
        }

        int touched = 0;

        foreach (GameObject go in Selection.gameObjects)
        {
            if (go.GetComponent<Health>() == null)
            {
                Debug.LogWarning($"{go.name}: Health yok, olum yakalanamaz; esya dusmez. " +
                                 "Once Tools/Enemy/Setup Selected Enemies calistir.", go);
                continue;
            }

            var loot = go.GetComponent<LootDrop>();
            if (loot == null) loot = Undo.AddComponent<LootDrop>(go);

            // dropPrefab private oldugu icin SerializedObject uzerinden yazilir.
            var so = new SerializedObject(loot);
            SerializedProperty dropProp = so.FindProperty("dropPrefab");
            if (dropProp.objectReferenceValue == null)
            {
                dropProp.objectReferenceValue = prefab;
                so.ApplyModifiedProperties();
            }

            touched++;
        }

        Debug.Log($"[PickupSetup] {touched} hirda portakal dusurme eklendi.");
    }

    [MenuItem("Tools/World/Add Orange Drop To Selection", true)]
    private static bool AddOrangeDropValidate() => Selection.gameObjects.Length > 0;

    /// <summary>Modelin yatay yarıçapı (görünür sınırlardan, dünya ölçeğinde).</summary>
    private static float MeasureRadius(GameObject go)
    {
        Bounds? bounds = CombinedBounds(go);
        if (!bounds.HasValue) return 0.25f;

        Vector3 size = bounds.Value.size;
        return Mathf.Max(size.x, size.y, size.z) * 0.5f;
    }

    /// <summary>Modelin merkezi (local uzayda; pivot ayağının altındaysa da doğru oturur).</summary>
    private static Vector3 MeasureCenter(GameObject go)
    {
        Bounds? bounds = CombinedBounds(go);
        if (!bounds.HasValue) return Vector3.zero;

        return go.transform.InverseTransformPoint(bounds.Value.center);
    }

    private static Bounds? CombinedBounds(GameObject go)
    {
        Bounds? bounds = null;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            if (bounds.HasValue)
            {
                Bounds merged = bounds.Value;
                merged.Encapsulate(r.bounds);
                bounds = merged;
            }
            else
            {
                bounds = r.bounds;
            }
        }

        return bounds;
    }
}
