using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// boss.fbx içindeki clip'lerden tek tıkla boss Animator Controller'ı üretir.
/// Menü: Tools/Boss/Create Boss Animator. Idempotent (tekrar çalıştırınca bozmaz).
///
/// Kurulan akış:
///   Wait (boş, varsayılan)  -> Any State -(Scare)->  Scare  -(exit time)->  Idle
///   Idle &lt;-(Speed)-&gt; Walk
///   Any State -(Attack1/Attack2)-> Attack state -(exit time)-> Idle
///
/// Clip'ler ada göre eşleştirilir (scare/roar, walk/yuru, attack1, attack2, idle).
/// Eşleşmeyen olursa Console'a uyarı düşer; controller'da o state'e clip'i elle sürükle.
/// </summary>
public static class BossAnimatorSetup
{
    // Clip kaynakları öncelik sırasıyla: önce boss'un kendi FBX'i, bulunamazsa
    // oyuncunun FBX'i (aynı metarig'den geldikleri için clip'ler boss'a da uyar).
    private static readonly string[] SourceFbxPaths =
    {
        "Assets/Models/boss.fbx",
        "Assets/Models/temelkarakter14.fbx",
    };

    private const string ControllerPath = "Assets/Animations/BossFight.controller";

    private const string SpeedParam = "Speed";
    private const string ScareParam = "Scare";
    private const string Attack1Param = "Attack1";
    private const string Attack2Param = "Attack2";

    [MenuItem("Tools/Boss/Create Boss Animator")]
    public static void Create()
    {
        // Tüm kaynak FBX'lerin clip'lerini öncelik sırasıyla topla (preview clip'leri hariç).
        var clips = new List<AnimationClip>();
        foreach (string path in SourceFbxPaths)
        {
            var found = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__"))
                .ToArray();

            if (found.Length > 0)
                Debug.Log($"{path} clip'leri: {string.Join(", ", found.Select(c => c.name))}");

            clips.AddRange(found);
        }

        if (clips.Count == 0)
        {
            Debug.LogError("Hicbir kaynak FBX'te AnimationClip bulunamadi. FBX'i sec, Inspector > " +
                           "Animation sekmesinde 'Import Animation' acik mi kontrol et.");
            return;
        }

        // Rolleri ada göre eşleştir. Oyuncu klipleri metarig|CatAttackOne / CatWalk gibi
        // adlandirildigi icin hem "attack1" hem "attackone" varyantlari aranir.
        AnimationClip scareClip = Match(clips, "scare", "roar", "scream", "wake");
        AnimationClip idleClip = Match(clips, "idle", "bekle", "breath");
        AnimationClip walkClip = Match(clips, "walk", "yuru", "yürü", "run", "move");
        AnimationClip attack1Clip = Match(clips, "attack1", "attackone", "attack_1", "atak1", "punch");
        AnimationClip attack2Clip = Match(clips, "attack2", "attacktwo", "attack_2", "atak2", "slash");

        // Attack'lar numarasız tek isimle geldiyse (ör. "Attack"), ilk saldırıya onu ver.
        if (attack1Clip == null) attack1Clip = Match(clips, "attack", "atak", "hit");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParam(controller, SpeedParam, AnimatorControllerParameterType.Float);
        EnsureParam(controller, ScareParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, Attack1Param, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, Attack2Param, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // --- State'ler ---
        // Wait: boş varsayılan state. Boss, scare tetiklenene kadar import pozunda bekler.
        AnimatorState wait = EnsureState(sm, "Wait", null, new Vector3(-60f, 0f, 0f));
        sm.defaultState = wait;

        AnimatorState idle = EnsureState(sm, "Idle", idleClip, new Vector3(260f, 0f, 0f));
        AnimatorState walk = EnsureState(sm, "Walk", walkClip, new Vector3(260f, 90f, 0f));
        AnimatorState scare = EnsureState(sm, "Scare", scareClip, new Vector3(60f, -110f, 0f));
        AnimatorState attack1 = EnsureState(sm, "Attack1", attack1Clip, new Vector3(520f, -40f, 0f));
        AnimatorState attack2 = EnsureState(sm, "Attack2", attack2Clip, new Vector3(520f, 50f, 0f));

        // --- Geçişler ---
        // Scare: her yerden tetiklenir, animasyon bitince dövüş duruşuna (Idle) geçer.
        EnsureAnyState(sm, scare, ScareParam);
        EnsureTrans(scare, idle, true, 0.95f);

        // Idle <-> Walk: BossFight'ın yazdığı Speed parametresiyle.
        EnsureTrans(idle, walk, false, 0f, (SpeedParam, AnimatorConditionMode.Greater, 0.1f));
        EnsureTrans(walk, idle, false, 0f, (SpeedParam, AnimatorConditionMode.Less, 0.1f));

        // Saldırılar: her yerden kesip oynar, bitince Idle'a döner.
        EnsureAnyState(sm, attack1, Attack1Param);
        EnsureTrans(attack1, idle, true, 0.85f);
        EnsureAnyState(sm, attack2, Attack2Param);
        EnsureTrans(attack2, idle, true, 0.85f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Hangi role hangi clip bağlandı, tek bakışta görünsün.
        Debug.Log("Boss animasyon eslesmesi:\n" +
                  RoleLine("Scare", scareClip) +
                  RoleLine("Idle", idleClip) +
                  RoleLine("Walk", walkClip) +
                  RoleLine("Attack1", attack1Clip) +
                  RoleLine("Attack2", attack2Clip));

        WarnMissing("Scare", scareClip);
        WarnMissing("Idle", idleClip);
        WarnMissing("Walk", walkClip);
        WarnMissing("Attack1", attack1Clip);
        WarnMissing("Attack2", attack2Clip);

        Debug.Log($"Hazir: {ControllerPath}\nBoss'un Animator'ina bu controller'i ata; " +
                  "BossFight ve BossRoomTurn parametre adlarini zaten bunlara gore kullaniyor.", controller);

        Selection.activeObject = controller;
    }

    /// <summary>Rol -> clip eşleşmesini (ve clip'in geldiği dosyayı) tek satırda yazar.</summary>
    private static string RoleLine(string role, AnimationClip clip)
    {
        if (clip == null) return $"  {role,-8}: -\n";

        string source = System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(clip));
        return $"  {role,-8}: {clip.name}  ({source})\n";
    }

    private static void WarnMissing(string role, AnimationClip clip)
    {
        if (clip == null)
            Debug.LogWarning($"'{role}' icin clip eslesmedi. {ControllerPath} icinde {role} " +
                             "state'ini sec ve Motion alanina dogru clip'i surukle.");
    }

    /// <summary>Adında anahtar kelimelerden biri geçen ilk clip'i döndürür.</summary>
    private static AnimationClip Match(IEnumerable<AnimationClip> clips, params string[] keywords)
    {
        foreach (AnimationClip clip in clips)
        {
            string name = clip.name.ToLowerInvariant();
            if (keywords.Any(k => name.Contains(k))) return clip;
        }
        return null;
    }

    private static void EnsureParam(AnimatorController controller, string name,
        AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(p => p.name == name)) return;
        controller.AddParameter(name, type);
    }

    /// <summary>State'i bulur ya da oluşturur; clip verilmişse Motion'ı günceller.</summary>
    private static AnimatorState EnsureState(AnimatorStateMachine sm, string name,
        AnimationClip clip, Vector3 position)
    {
        AnimatorState state = null;
        foreach (var child in sm.states)
        {
            if (child.state.name != name) continue;
            state = child.state;
            break;
        }

        if (state == null) state = sm.AddState(name, position);
        // Eşleşen clip yoksa elle atanmış motion'ı ezme.
        if (clip != null) state.motion = clip;

        return state;
    }

    /// <summary>Any State -> hedef geçişini bir kez kurar (exit time yok, kısa blend).</summary>
    private static void EnsureAnyState(AnimatorStateMachine sm, AnimatorState target, string trigger)
    {
        if (sm.anyStateTransitions.Any(t => t.destinationState == target)) return;

        var transition = sm.AddAnyStateTransition(target);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

    /// <summary>İki state arası geçişi bir kez kurar (exit time ya da parametre koşuluyla).</summary>
    private static void EnsureTrans(AnimatorState from, AnimatorState to, bool hasExitTime,
        float exitTime, params (string param, AnimatorConditionMode mode, float threshold)[] conditions)
    {
        if (from == null || to == null) return;
        if (from.transitions.Any(t => t.destinationState == to)) return;

        var transition = from.AddTransition(to);
        transition.hasExitTime = hasExitTime;
        transition.exitTime = exitTime;
        transition.duration = 0.1f;

        foreach (var c in conditions)
            transition.AddCondition(c.mode, c.threshold, c.param);
    }
}
