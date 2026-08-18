using UnityEditor;
using UnityEngine;

/// <summary>
/// Yuvarlanan top tuzağının kurulumu.
///
/// Menü:
///   Tools/World/Create Rolling Ball      -> Assets/Prefabs/RollingBall.prefab üretir
///   Tools/World/Create Rolling Ball Trap -> sahneye spawner + tetikleyici kurar ve bağlar
///
/// Üretilen top: küre mesh + Rigidbody (sürekli çarpışma algılamalı, hızlı düşerken
/// zeminden geçmesin diye) + DamageSource (temasta hasar) + RollingBall (temizlik).
///
/// Tuzak kurulumu seçili objenin konumunu referans alır: tetik alanı oraya, doğma
/// kutusu SpawnHeight metre yukarısına konur. Seçim yoksa oyuncunun konumu kullanılır.
/// Yerleştirmeyi sahnede elle ayarlaman beklenir; araç yalnızca bağlantıları kurar.
/// </summary>
public static class RollingBallSetup
{
    private const string PrefabFolder = "Assets/Prefabs";
    private const string BallPrefabPath = PrefabFolder + "/RollingBall.prefab";

    /// <summary>Topun çapı (metre). Yokuşta ezici hissettirecek kadar büyük.</summary>
    private const float BallDiameter = 2f;
    private const float BallMass = 60f;
    private const float BallDamage = 25f;

    /// <summary>Doğma kutusunun referans noktasının kaç metre üstüne konacağı.</summary>
    private const float SpawnHeight = 30f;

    [MenuItem("Tools/World/Create Rolling Ball")]
    public static GameObject CreateRollingBall()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(BallPrefabPath);
        if (existing != null)
        {
            EditorGUIUtility.PingObject(existing);
            Debug.Log($"[RollingBallSetup] Top prefab'i zaten var: {BallPrefabPath}", existing);
            return existing;
        }

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        // Bilesenleri eklemenin tek yolu once sahnede kurmak; sonra prefab'a kaydedip siliyoruz.
        GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        instance.name = "RollingBall";
        instance.transform.localScale = Vector3.one * BallDiameter;

        var rb = instance.AddComponent<Rigidbody>();
        rb.mass = BallMass;
        // Gokyuzunden dusen top tek karede zeminin altina gecmesin.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var damage = instance.AddComponent<DamageSource>();
        var damageSo = new SerializedObject(damage);
        damageSo.FindProperty("damage").floatValue = BallDamage;
        damageSo.FindProperty("instantKill").boolValue = false;
        damageSo.FindProperty("destroyOnHit").boolValue = false;
        // Oyuncunun ustunde duran top surekli ezsin; PlayerHealth zaten 0.3 sn debounce uyguluyor.
        damageSo.FindProperty("continuous").boolValue = true;
        damageSo.FindProperty("tickInterval").floatValue = 0.6f;
        damageSo.ApplyModifiedProperties();

        instance.AddComponent<RollingBall>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, BallPrefabPath);
        Object.DestroyImmediate(instance);

        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[RollingBallSetup] Top prefab'i uretildi: {BallPrefabPath}\n" +
                  "Kendi mesh'ini kullanacaksan prefab'in icindeki kureyi degistir; " +
                  "SphereCollider + Rigidbody + DamageSource kalsin.", prefab);
        return prefab;
    }

    [MenuItem("Tools/World/Create Rolling Ball Trap")]
    public static void CreateRollingBallTrap()
    {
        GameObject ballPrefab = CreateRollingBall();
        if (ballPrefab == null) return;

        Vector3 origin = ResolveOrigin();

        // --- Gokyuzundeki dogma kutusu ---
        var spawnerGo = new GameObject("BallSpawner");
        Undo.RegisterCreatedObjectUndo(spawnerGo, "Create Rolling Ball Trap");
        spawnerGo.transform.position = origin + Vector3.up * SpawnHeight;

        var spawnBox = spawnerGo.AddComponent<BoxCollider>();
        spawnBox.isTrigger = true;   // dogan toplar kutunun kendisine carpmasin
        spawnBox.size = new Vector3(20f, 4f, 20f);

        var spawner = spawnerGo.AddComponent<RollingBallSpawner>();
        var spawnerSo = new SerializedObject(spawner);
        spawnerSo.FindProperty("spawnArea").objectReferenceValue = spawnBox;
        spawnerSo.FindProperty("ballPrefab").objectReferenceValue = ballPrefab;
        spawnerSo.FindProperty("spawnOnStart").boolValue = false;   // tetikleyici baslatacak
        spawnerSo.ApplyModifiedProperties();

        // --- Yoldaki tetik alani ---
        var triggerGo = new GameObject("BallSpawnTrigger");
        Undo.RegisterCreatedObjectUndo(triggerGo, "Create Rolling Ball Trap");
        triggerGo.transform.position = origin;

        var triggerBox = triggerGo.AddComponent<BoxCollider>();
        triggerBox.isTrigger = true;
        triggerBox.size = new Vector3(10f, 4f, 2f);

        var trigger = triggerGo.AddComponent<BallSpawnTrigger>();
        var triggerSo = new SerializedObject(trigger);
        var spawnersProp = triggerSo.FindProperty("spawners");
        spawnersProp.arraySize = 1;
        spawnersProp.GetArrayElementAtIndex(0).objectReferenceValue = spawner;
        triggerSo.ApplyModifiedProperties();

        Selection.objects = new Object[] { triggerGo, spawnerGo };
        Debug.Log("[RollingBallSetup] Tuzak kuruldu.\n" +
                  "1) BallSpawner'i yokusun TEPESINE, gokyuzune tasi; kutusunu yokusun genisligine yay.\n" +
                  "2) BallSpawnTrigger'i oyuncunun gececegi yere koy; kutusunu yolu kapatacak kadar genislet.\n" +
                  "3) Interval / Burst Count / Damage degerleriyle zorlugu ayarla.\n" +
                  "Sahneyi kaydetmeyi unutma.", triggerGo);
    }

    /// <summary>Kurulum referansi: secili obje > oyuncu > sahne merkezi.</summary>
    private static Vector3 ResolveOrigin()
    {
        if (Selection.activeGameObject != null)
            return Selection.activeGameObject.transform.position;

        var player = Object.FindAnyObjectByType<PlayerHealth>();
        return player != null ? player.transform.position : Vector3.zero;
    }
}
