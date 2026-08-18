using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Oyuncu, boss ve moblar aynı iskeleti (metarig) paylaştığı için animasyon klipleri
/// üçünde de kullanılabilir. Bu yardımcı projedeki tüm karakter kliplerini tarar ve
/// ada göre klip bulur; böylece yeni bir animasyonu (ör. ölüm) hangi FBX'e koyduğun
/// fark etmez, setup araçları (Player/Boss/Enemy Animator Setup) onu bulur.
///
/// Arama sırası: önce çağıranın verdiği "tercih edilen" dosyalar, sonra bilinen karakter
/// FBX'leri, sonra Assets altındaki diğer tüm klipler (3rdPartyAssets hariç).
/// </summary>
public static class CharacterClips
{
    /// <summary>Bilinen karakter FBX'leri; aynı klip birden çok yerdeyse buradaki sıra kazanır.</summary>
    public static readonly string[] KnownCharacterFbx =
    {
        "Assets/Models/boss.fbx",
        "Assets/Models/temelkarakter14.fbx",
        "Assets/Models/ıwinL.fbx",
        "Assets/Scripts/Boss/stunanimmedboss.fbx",
    };

    /// <summary>Ölüm animasyonu için aranan ad parçaları (öncelik sırasıyla).</summary>
    public static readonly string[] DeathKeywords =
    {
        "die", "death", "dead", "dying", "olum", "ölüm", "olme", "ölme", "devril",
    };

    private static readonly string[] IgnoredPathParts = { "/3rdPartyAssets/", "/Packages/" };
    private static readonly string[] ClipExtensions = { ".fbx", ".anim", ".blend", ".dae" };

    /// <summary>Adında anahtar kelimelerden biri geçen ilk klibi verir (bulamazsa null).</summary>
    public static AnimationClip Find(params string[] keywords) => Find(null, keywords);

    /// <summary>
    /// Adında anahtar kelimelerden biri geçen ilk klibi verir. preferredPaths verilirse
    /// önce o dosyalara bakılır (ör. oyuncu için önce oyuncunun FBX'i).
    /// </summary>
    public static AnimationClip Find(string[] preferredPaths, params string[] keywords)
    {
        List<AnimationClip> clips = All(preferredPaths);

        // Dış döngü anahtar kelime: "die" eşleşmesi "devril" eşleşmesine tercih edilir.
        foreach (string keyword in keywords)
        {
            string key = keyword.ToLowerInvariant();
            foreach (AnimationClip clip in clips)
                if (clip.name.ToLowerInvariant().Contains(key)) return clip;
        }

        return null;
    }

    /// <summary>Tam adı verilen klibi bulur (ör. "metarig|CatHit").</summary>
    public static AnimationClip FindExact(string clipName, string[] preferredPaths = null)
    {
        foreach (AnimationClip clip in All(preferredPaths))
            if (clip.name == clipName) return clip;

        return null;
    }

    /// <summary>Taranan tüm karakter klipleri (öncelik sırasında, tekrarsız).</summary>
    public static List<AnimationClip> All(string[] preferredPaths = null)
    {
        var clips = new List<AnimationClip>();
        var seen = new HashSet<AnimationClip>();

        foreach (string path in CollectPaths(preferredPaths))
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(asset is AnimationClip clip)) continue;
                if (clip.name.StartsWith("__preview__")) continue;
                if (seen.Add(clip)) clips.Add(clip);
            }
        }

        return clips;
    }

    /// <summary>Klip barındırabilecek dosya yolları: tercih edilenler -> bilinenler -> proje geneli.</summary>
    private static IEnumerable<string> CollectPaths(string[] preferredPaths)
    {
        var paths = new List<string>();

        if (preferredPaths != null) paths.AddRange(preferredPaths);
        paths.AddRange(KnownCharacterFbx);

        foreach (string filter in new[] { "t:AnimationClip", "t:Model" })
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { "Assets" }))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

        var seen = new HashSet<string>();

        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
            if (IgnoredPathParts.Any(part => path.Contains(part))) continue;
            if (!ClipExtensions.Any(ext => path.ToLowerInvariant().EndsWith(ext))) continue;

            yield return path;
        }
    }

    /// <summary>Ölüm klibini bulur; bulamazsa Console'a ne yapılacağını yazar.</summary>
    public static AnimationClip FindDeath(string[] preferredPaths = null)
    {
        AnimationClip clip = Find(preferredPaths, DeathKeywords);

        if (clip == null)
        {
            Debug.LogWarning("Olum klibi bulunamadi. Adinda 'Die' / 'Death' gecen bir klip iceren " +
                             "FBX'i Assets/Models altina birak ve arac tekrar calistir. Klip adini " +
                             "degistiremiyorsan controller'daki Die state'ine klibi elle surukle.");
            return null;
        }

        Debug.Log($"Olum klibi: {clip.name}  ({System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(clip))})", clip);
        return clip;
    }
}
