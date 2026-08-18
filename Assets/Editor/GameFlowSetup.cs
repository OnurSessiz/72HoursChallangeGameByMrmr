using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Oyun akışının (4 adımlık ilerleme) kurulumu.
///
/// Menü:
///   Tools/Progression/Setup Game Flow   -> GameProgress + hedefler + kapılar
///   Tools/Progression/Create Gates Only -> yalnızca üç kapıyı kurar (hedeflere dokunmaz)
///
/// Sahnedeki mevcut sistemleri kendisi bulur ve bağlar:
///   1. adım (HirtRoom)  -> HirtRoomAmbush'ın hırt listesindeki Health'ler
///   2. adım (Swordsman) -> BlackSwordsman'ın Health'i
///   3. adım (Boss)      -> BossFight'ın Health'i
///   4. adım (RockTrap)  -> tırmanışın tepesine konan ReachPointObjective
///
/// Kapılar (StageGate) kaba konumlarla üretilir; DOĞRU YERE SEN TAŞIRSIN. Araç
/// yalnızca bileşenleri ve bağlantıları kurar, level design yapmaz.
///
/// Tekrar çalıştırılabilir: var olan objeler yeniden üretilmez, yalnızca eksik
/// bağlantılar tamamlanır. Bir adım hata verirse yalnızca o adım atlanır; hata
/// Console'a adıyla yazılır ve kalan adımlar çalışmaya devam eder.
/// </summary>
public static class GameFlowSetup
{
    private const string ProgressName = "GameProgress";
    private const string ObjectivesRoot = "Objectives";
    private const string GatesRoot = "Gates";

    [MenuItem("Tools/Progression/Setup Game Flow")]
    public static void SetupGameFlow()
    {
        var report = new List<string>();

        GameProgress progress = null;
        Run("GameProgress", report, () => progress = EnsureGameProgress(report));

        Transform objectives = EnsureRoot(ObjectivesRoot);
        Run("1. adim (HirtRoom)", report, () => SetupHirtRoomObjective(objectives, report));
        Run("2. adim (Swordsman)", report, () => SetupSwordsmanObjective(objectives, report));
        Run("3. adim (Boss)", report, () => SetupBossObjective(objectives, report));
        Run("4. adim (RockTrap)", report, () => SetupRockTrapObjective(objectives, report));

        CreateGates(report);
        Run("Kapanis sekansi", report, () => SetupEndingSequence(report));

        if (progress != null) Selection.activeGameObject = progress.gameObject;

        Debug.Log("[GameFlowSetup] Kurulum bitti.\n" + string.Join("\n", report) +
                  "\n\nSIRADAKI IS: Gates altindaki uc kapiyi ilgili oda girislerine tasi ve " +
                  "kati BoxCollider'i gecidi tam kapatacak boyda yap.\nSahneyi kaydetmeyi unutma.");
    }

    [MenuItem("Tools/Progression/Create Gates Only")]
    public static void CreateGatesOnly()
    {
        var report = new List<string>();
        CreateGates(report);
        Debug.Log("[GameFlowSetup] Kapilar:\n" + string.Join("\n", report));
    }

    private static void CreateGates(List<string> report)
    {
        Transform gates = EnsureRoot(GatesRoot);

        Run("Gate_OceanRoom", report, () =>
            SetupGate(gates, "Gate_OceanRoom", GameStage.HirtRoom, OceanGatePosition(), report));
        Run("Gate_BossRoom", report, () =>
            SetupGate(gates, "Gate_BossRoom", GameStage.Swordsman, BossGatePosition(), report));
        Run("Gate_RockTrap", report, () =>
            SetupGate(gates, "Gate_RockTrap", GameStage.Boss, RockTrapGatePosition(), report));
    }

    /// <summary>
    /// Bir kurulum adımını çalıştırır. Adım hata verirse yalnızca o adım atlanır:
    /// hata Console'a adıyla yazılır, kalan adımlar çalışmaya devam eder.
    /// </summary>
    private static void Run(string stepName, List<string> report, Action step)
    {
        try
        {
            step();
        }
        catch (Exception e)
        {
            report.Add($"- {stepName} HATA VERDI: {e.GetType().Name} — {e.Message}");
            Debug.LogError($"[GameFlowSetup] '{stepName}' kurulamadi.\n{e}");
        }
    }

    // --- Kurulum adimlari ---

    private static GameProgress EnsureGameProgress(List<string> report)
    {
        var existing = UnityEngine.Object.FindAnyObjectByType<GameProgress>();
        if (existing != null)
        {
            report.Add("- GameProgress zaten vardi, korundu.");
            return existing;
        }

        var go = new GameObject(ProgressName);
        Undo.RegisterCreatedObjectUndo(go, "Setup Game Flow");
        var progress = go.AddComponent<GameProgress>();
        report.Add("- GameProgress olusturuldu.");
        return progress;
    }

    private static void SetupHirtRoomObjective(Transform parent, List<string> report)
    {
        var ambush = UnityEngine.Object.FindAnyObjectByType<HirtRoomAmbush>();
        if (ambush == null)
        {
            report.Add("- 1. adim ATLANDI: sahnede HirtRoomAmbush bulunamadi.");
            return;
        }

        // Hirtlari pusunun kendi listesinden okuyoruz ki ikisi ayni dusmanlari hedeflesin.
        List<Health> enemies = ReadAmbushEnemies(ambush);
        if (enemies.Count == 0)
        {
            report.Add($"- 1. adim UYARI: {ambush.name} uzerinde hirt bulunamadi; " +
                       "Objective_HirtRoom'un enemies listesini elle doldur.");
        }

        var objective = EnsureObjective<EnemyClearObjective>(parent, "Objective_HirtRoom",
                                                            ambush.transform.position);
        var so = new SerializedObject(objective);
        SetEnum(so, "stage", GameStage.HirtRoom);
        WriteObjectArray(so.FindProperty("enemies"), enemies);
        so.ApplyModifiedProperties();

        report.Add($"- 1. adim (HirtRoom): {enemies.Count} hirt baglandi.");
    }

    private static void SetupSwordsmanObjective(Transform parent, List<string> report)
    {
        var swordsman = UnityEngine.Object.FindAnyObjectByType<BlackSwordsman>();
        if (swordsman == null)
        {
            report.Add("- 2. adim ATLANDI: sahnede BlackSwordsman bulunamadi.");
            return;
        }

        var health = swordsman.GetComponent<Health>();
        if (health == null)
        {
            report.Add($"- 2. adim UYARI: {swordsman.name} uzerinde Health yok; " +
                       "olumu yakalanamaz (Tools/Combat/Add Health To Selection).");
            return;
        }

        var objective = EnsureObjective<EnemyClearObjective>(parent, "Objective_Swordsman",
                                                            swordsman.transform.position);
        var so = new SerializedObject(objective);
        SetEnum(so, "stage", GameStage.Swordsman);
        WriteObjectArray(so.FindProperty("enemies"), new List<Health> { health });
        so.ApplyModifiedProperties();

        report.Add($"- 2. adim (Swordsman): {swordsman.name} baglandi.");
    }

    private static void SetupBossObjective(Transform parent, List<string> report)
    {
        var boss = UnityEngine.Object.FindAnyObjectByType<BossFight>();
        if (boss == null)
        {
            report.Add("- 3. adim ATLANDI: sahnede BossFight bulunamadi.");
            return;
        }

        var health = boss.GetComponent<Health>();
        if (health == null)
        {
            report.Add($"- 3. adim UYARI: {boss.name} uzerinde Health yok; olumu yakalanamaz.");
            return;
        }

        var objective = EnsureObjective<EnemyClearObjective>(parent, "Objective_Boss",
                                                            boss.transform.position);
        var so = new SerializedObject(objective);
        SetEnum(so, "stage", GameStage.Boss);
        WriteObjectArray(so.FindProperty("enemies"), new List<Health> { health });
        so.ApplyModifiedProperties();

        report.Add($"- 3. adim (Boss): {boss.name} baglandi.");
    }

    private static void SetupRockTrapObjective(Transform parent, List<string> report)
    {
        // Tirmanisin tepesi icin en iyi tahmin: toplarin dogdugu noktanin altindaki zemin.
        var spawner = UnityEngine.Object.FindAnyObjectByType<RollingBallSpawner>();
        Vector3 position = spawner != null
            ? spawner.transform.position + Vector3.down * 25f
            : Vector3.zero;

        GameObject go = EnsureChildObject(parent, "Objective_RockTrapTop", position);

        // Trigger kutusu bilesenden ONCE eklenir: ReachPointObjective'in RequireComponent
        // kurali collider bekliyor, hazir degilse AddComponent null donebiliyor.
        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(go);

        Undo.RecordObject(box, "Setup Game Flow");
        box.isTrigger = true;
        box.size = new Vector3(10f, 5f, 3f);

        var objective = EnsureComponent<ReachPointObjective>(go);

        var so = new SerializedObject(objective);
        SetEnum(so, "stage", GameStage.RockTrap);
        so.ApplyModifiedProperties();

        report.Add("- 4. adim (RockTrap): varis alani kuruldu (trigger 10x5x3). " +
                   "Objective_RockTrapTop'u tirmanisin BITIS noktasina tasi.");
    }

    [MenuItem("Tools/Progression/Create Ending Sequence")]
    public static void CreateEndingSequenceOnly()
    {
        var report = new List<string>();
        Run("Kapanis sekansi", report, () => SetupEndingSequence(report));
        Debug.Log("[GameFlowSetup] Kapanis sekansi:\n" + string.Join("\n", report));
    }

    /// <summary>
    /// Kapanış sekansını kurar ve çekim kameralarını adlarına göre bağlar.
    /// Ad birebir aranır (buyuk/kucuk harf onemsiz); bulunamayan slot bos birakilir.
    /// </summary>
    private static void SetupEndingSequence(List<string> report)
    {
        var existing = UnityEngine.Object.FindAnyObjectByType<EndingSequence>();
        EndingSequence sequence;

        if (existing != null)
        {
            sequence = existing;
            report.Add("- EndingSequence zaten vardi, kameralari tazelendi.");
        }
        else
        {
            var go = new GameObject("EndingSequence");
            Undo.RegisterCreatedObjectUndo(go, "Setup Game Flow");
            sequence = EnsureComponent<EndingSequence>(go);
            report.Add("- EndingSequence olusturuldu.");
        }

        // Kullanicinin istedigi sira: hirt odasi -> final -> boss.
        string[] shotNames = { "HirtRoomCam", "FinalCam", "BossCam" };

        var so = new SerializedObject(sequence);
        SetEnum(so, "triggerStage", GameStage.RockTrap);

        SerializedProperty shots = so.FindProperty("shots");
        if (shots == null) throw new InvalidOperationException("'shots' alani EndingSequence uzerinde bulunamadi.");

        shots.arraySize = shotNames.Length;
        var missing = new List<string>();

        for (int i = 0; i < shotNames.Length; i++)
        {
            SerializedProperty element = shots.GetArrayElementAtIndex(i);
            Camera cam = FindCameraByName(shotNames[i]);

            // Var olan sekansta elle ayarlanmis sureyi/secimi ezmeyelim; yalnizca
            // kamera slotu bossa doldururuz.
            SerializedProperty cameraProp = element.FindPropertyRelative("camera");
            if (cameraProp.objectReferenceValue == null) cameraProp.objectReferenceValue = cam;

            SerializedProperty durationProp = element.FindPropertyRelative("duration");
            if (durationProp.floatValue <= 0f) durationProp.floatValue = 2.5f;

            if (cam == null) missing.Add(shotNames[i]);
        }

        so.ApplyModifiedProperties();

        if (missing.Count > 0)
            report.Add($"- Kapanis UYARI: su kameralar sahnede bulunamadi: {string.Join(", ", missing)}. " +
                       "Sahnede olusturup EndingSequence'in shots listesine sirayla surukle.");
        else
            report.Add("- Kapanis: HirtRoomCam -> FinalCam -> BossCam baglandi.");
    }

    /// <summary>Sahnedeki (kapalı olanlar dahil) kameraları adına göre arar.</summary>
    private static Camera FindCameraByName(string cameraName)
    {
        foreach (Camera cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            if (string.Equals(cam.name, cameraName, StringComparison.OrdinalIgnoreCase)) return cam;
        }

        return null;
    }

    private static void SetupGate(Transform parent, string gateName, GameStage requiredStage,
                                  Vector3 position, List<string> report)
    {
        Transform existing = parent.Find(gateName);
        if (existing != null)
        {
            report.Add($"- {gateName} zaten vardi, korundu.");
            return;
        }

        var go = new GameObject(gateName);
        Undo.RegisterCreatedObjectUndo(go, "Setup Game Flow");
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        // Kati engel: oyuncuyu durduran duvar.
        var blocker = go.AddComponent<BoxCollider>();
        blocker.isTrigger = false;
        blocker.size = new Vector3(6f, 5f, 1f);

        // Uyari alani: kilitliyken oyuncu yaklasinca onBlocked tetiklenir.
        var warnZone = go.AddComponent<BoxCollider>();
        warnZone.isTrigger = true;
        warnZone.size = new Vector3(8f, 5f, 4f);

        // StageGate en son eklenir; collider'lar hazir oldugu icin kendi engelini bulabilir.
        var gate = go.AddComponent<StageGate>();
        var so = new SerializedObject(gate);
        SetEnum(so, "requiredStage", requiredStage);
        WriteObjectArray(so.FindProperty("blockers"), new List<Collider> { blocker });
        so.ApplyModifiedProperties();

        report.Add($"- {gateName} olusturuldu (acilma sarti: {requiredStage} tamamlanmasi).");
    }

    // --- Konum tahminleri (sahnede elle duzeltilecek) ---

    private static Vector3 OceanGatePosition()
    {
        var swordsman = UnityEngine.Object.FindAnyObjectByType<BlackSwordsman>();
        return swordsman != null ? swordsman.transform.position : Vector3.zero;
    }

    private static Vector3 BossGatePosition()
    {
        // BossRoomTurn zaten boss odasinin girisinde duruyor; kapinin dogal yeri orasi.
        var bossEnter = UnityEngine.Object.FindAnyObjectByType<BossRoomTurn>();
        return bossEnter != null ? bossEnter.transform.position : Vector3.zero;
    }

    private static Vector3 RockTrapGatePosition()
    {
        var trigger = UnityEngine.Object.FindAnyObjectByType<BallSpawnTrigger>();
        return trigger != null ? trigger.transform.position : Vector3.zero;
    }

    // --- Yardimcilar ---

    private static Transform EnsureRoot(string rootName)
    {
        GameObject existing = GameObject.Find(rootName);
        if (existing != null) return existing.transform;

        var go = new GameObject(rootName);
        Undo.RegisterCreatedObjectUndo(go, "Setup Game Flow");
        return go.transform;
    }

    /// <summary>Adı verilen child objeyi bulur; yoksa oluşturur. Bileşen eklemez.</summary>
    private static GameObject EnsureChildObject(Transform parent, string objectName, Vector3 position)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null) return existing.gameObject;

        var go = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(go, "Setup Game Flow");
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        return go;
    }

    /// <summary>
    /// Bileşeni ekler (zaten varsa onu döndürür). AddComponent null dönerse — ki
    /// RequireComponent'i karşılanamayan script'lerde olur — sessizce devam etmek
    /// yerine anlaşılır bir hata verir.
    /// </summary>
    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var found = go.GetComponent<T>();
        if (found != null) return found;

        var added = Undo.AddComponent<T>(go);
        if (added == null)
            throw new InvalidOperationException(
                $"'{go.name}' uzerine {typeof(T).Name} eklenemedi. Script'in RequireComponent " +
                "kurali karsilanamamis olabilir (gerekli Collider'i once ekle).");

        return added;
    }

    /// <summary>Objeyi bulup/oluşturup üzerine bileşeni ekler.</summary>
    private static T EnsureObjective<T>(Transform parent, string objectName, Vector3 position)
        where T : Component
    {
        return EnsureComponent<T>(EnsureChildObject(parent, objectName, position));
    }

    /// <summary>HirtRoomAmbush'ın kendi hırt listesini (gerekirse enemiesParent'tan) Health olarak okur.</summary>
    private static List<Health> ReadAmbushEnemies(HirtRoomAmbush ambush)
    {
        var result = new List<Health>();
        var so = new SerializedObject(ambush);

        SerializedProperty enemiesProp = so.FindProperty("enemies");
        if (enemiesProp != null && enemiesProp.isArray)
        {
            for (int i = 0; i < enemiesProp.arraySize; i++)
            {
                var follow = enemiesProp.GetArrayElementAtIndex(i).objectReferenceValue as EnemyFollow;
                if (follow == null) continue;

                var health = follow.GetComponent<Health>();
                if (health != null && !result.Contains(health)) result.Add(health);
            }
        }

        if (result.Count > 0) return result;

        // Liste bossa pusu da child'lardan topluyor; ayni yerden okuyalim.
        SerializedProperty parentProp = so.FindProperty("enemiesParent");
        Transform root = parentProp != null ? parentProp.objectReferenceValue as Transform : null;
        if (root == null) root = ambush.transform;

        foreach (EnemyFollow follow in root.GetComponentsInChildren<EnemyFollow>(true))
        {
            var health = follow.GetComponent<Health>();
            if (health != null && !result.Contains(health)) result.Add(health);
        }

        return result;
    }

    /// <summary>Enum alanını adına göre yazar; alan bulunamazsa anlaşılır hata verir.</summary>
    private static void SetEnum(SerializedObject so, string propertyName, Enum value)
    {
        SerializedProperty prop = so.FindProperty(propertyName);
        if (prop == null)
            throw new InvalidOperationException(
                $"'{propertyName}' alani {so.targetObject.GetType().Name} uzerinde bulunamadi " +
                "(script degismis olabilir).");

        prop.enumValueIndex = Convert.ToInt32(value);
    }

    private static void WriteObjectArray<T>(SerializedProperty arrayProp, List<T> values)
        where T : UnityEngine.Object
    {
        if (arrayProp == null) return;

        arrayProp.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            arrayProp.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
