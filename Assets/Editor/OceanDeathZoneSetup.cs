using UnityEditor;
using UnityEngine;

/// <summary>
/// Denize değen oyuncuyu anında öldüren tetik hacmini kurar.
///
/// Menü:
///   Tools/World/Create Ocean Death Zone -> seçili (yoksa sahnedeki "Ocean") objenin
///                                          altına "OceanDeathZone" trigger'ı üretir
///
/// Ocean prefab'ında collider yok; su tamamen görsel. Bu araç suyun mesh sınırlarını
/// ölçüp yüzeyi tavan kabul eden, aşağı doğru DeathZoneDepth metre uzanan bir
/// BoxCollider (isTrigger) + instantKill'li DamageSource ekler. Kutu kalın tutulur ki
/// hızlı düşen oyuncu tek karede içinden geçip kaçamasın.
///
/// Araç tekrar çalıştırılabilir: mevcut OceanDeathZone varsa yeniden ölçülüp güncellenir.
/// Kutu adanın altını da kapsıyorsa Inspector'dan BoxCollider size/center'ı daraltman yeterli.
/// </summary>
public static class OceanDeathZoneSetup
{
    private const string ZoneName = "OceanDeathZone";
    private const string OceanName = "Ocean";

    /// <summary>Su yüzeyinden aşağı doğru ölüm hacminin derinliği (metre).</summary>
    private const float DeathZoneDepth = 30f;

    [MenuItem("Tools/World/Create Ocean Death Zone")]
    public static void CreateOceanDeathZone()
    {
        GameObject ocean = FindOcean();
        if (ocean == null)
        {
            EditorUtility.DisplayDialog("Ocean bulunamadı",
                "Sahnede 'Ocean' adlı obje yok. Deniz objesini Hierarchy'de seçip aracı tekrar çalıştır.",
                "Tamam");
            return;
        }

        var renderer = ocean.GetComponent<Renderer>();
        if (renderer == null)
        {
            EditorUtility.DisplayDialog("Mesh bulunamadı",
                $"'{ocean.name}' üzerinde Renderer yok, su yüzeyi ölçülemiyor.\n" +
                "Su mesh'inin bulunduğu objeyi seçip tekrar dene.", "Tamam");
            return;
        }

        Bounds bounds = renderer.bounds;   // dünya uzayında su yüzeyinin sınırları

        // Var olan zone'u koru (elle daraltılmış olabilir), yoksa yenisini üret.
        Transform existing = ocean.transform.Find(ZoneName);
        GameObject zone;
        if (existing != null)
        {
            zone = existing.gameObject;
            Undo.RecordObject(zone.transform, "Update Ocean Death Zone");
        }
        else
        {
            zone = new GameObject(ZoneName);
            Undo.RegisterCreatedObjectUndo(zone, "Create Ocean Death Zone");
            zone.transform.SetParent(ocean.transform, false);
        }

        // Ocean çok küçük ölçekli (0.07x) bir prefab; kutu ölçülerini metre cinsinden
        // yazabilmek için child'ın ölçeğiyle parent'ın ölçeğini sadeleştiriyoruz.
        zone.transform.localScale = InverseScale(ocean.transform.lossyScale);
        zone.transform.rotation = Quaternion.identity;
        zone.transform.position = new Vector3(
            bounds.center.x,
            bounds.max.y - DeathZoneDepth * 0.5f,   // tavan tam su yüzeyinde
            bounds.center.z);

        var box = zone.GetComponent<BoxCollider>();
        if (box == null) box = Undo.AddComponent<BoxCollider>(zone);
        Undo.RecordObject(box, "Configure Ocean Death Zone");
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = new Vector3(bounds.size.x, DeathZoneDepth, bounds.size.z);

        var damage = zone.GetComponent<DamageSource>();
        if (damage == null) damage = Undo.AddComponent<DamageSource>(zone);

        // instantKill private [SerializeField]; SerializedObject üzerinden yazıyoruz.
        var so = new SerializedObject(damage);
        so.FindProperty("instantKill").boolValue = true;
        so.ApplyModifiedProperties();

        Selection.activeGameObject = zone;
        EditorUtility.SetDirty(zone);
        Debug.Log($"[OceanDeathZoneSetup] '{ocean.name}' altına ölüm bölgesi kuruldu " +
                  $"({box.size.x:F1} x {box.size.z:F1} m, {DeathZoneDepth} m derin). " +
                  "Sahneyi kaydetmeyi unutma.", zone);
    }

    /// <summary>Seçili obje varsa onu, yoksa sahnede "Ocean" adlı objeyi kullanır.</summary>
    private static GameObject FindOcean()
    {
        if (Selection.activeGameObject != null) return Selection.activeGameObject;

        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            if (t.name == OceanName) return t.gameObject;

        return null;
    }

    /// <summary>Sıfır ölçekli eksende bölme yapmadan 1/scale döndürür.</summary>
    private static Vector3 InverseScale(Vector3 scale)
    {
        return new Vector3(
            Mathf.Approximately(scale.x, 0f) ? 1f : 1f / scale.x,
            Mathf.Approximately(scale.y, 0f) ? 1f : 1f / scale.y,
            Mathf.Approximately(scale.z, 0f) ? 1f : 1f / scale.z);
    }
}
