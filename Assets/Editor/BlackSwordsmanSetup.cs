using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Çift kılıçlı nöbetçi için Animator Controller üretir
/// (Assets/Animations/BlackSwordsman.controller).
///
/// Kurulan akış:
///   Idle (varsayılan)
///   Any State -(SwordsReady)-> SwordsReady -> Idle   (kılıçları çekme, bir kez)
///   Any State -(DonenYaraks)-> DonenYaraks -> Idle   (360 derece dönen saldırı)
///   Any State -(Hit)-> Hit -> Idle                   (Health.hitTrigger)
///   Any State -(Die)-> Die                           (Health.dieTrigger; dönüş yok)
///
/// Klipler blackswordsman.fbx'ten ada göre eşleştirilir (metarig|Idle,
/// metarig|SwordsReady, metarig|donenyaraks, metarig|CatHit, metarig|Death).
///
/// Menü:
///   Tools/Enemy/Create Black Swordsman Animator  -> controller'ı üretir/günceller
///   Tools/Enemy/Setup Selected Black Swordsman   -> seçili objeye Health + Collider +
///                                                   BlackSwordsman + Animator kurar
/// İkisi de idempotent.
/// </summary>
public static class BlackSwordsmanSetup
{
    private const string ControllerPath = "Assets/Animations/BlackSwordsman.controller";
    private const string FbxPath = "Assets/Models/enemy/blackswordsman.fbx";

    private const string ReadyParam = "SwordsReady";
    private const string AttackParam = "DonenYaraks";
    private const string HitParam = "Hit";
    private const string DieParam = "Die";

    [MenuItem("Tools/Enemy/Create Black Swordsman Animator")]
    public static void Create()
    {
        string[] preferred = { FbxPath };

        // Kendi FBX'i öncelikli: aynı adlı klipler diğer karakterlerde de var.
        AnimationClip idleClip = CharacterClips.FindExact("metarig|Idle", preferred)
                                 ?? CharacterClips.Find(preferred, "idle");
        AnimationClip readyClip = CharacterClips.Find(preferred, "swordsready", "swords", "ready", "unsheath");
        AnimationClip attackClip = CharacterClips.Find(preferred, "donenyaraks", "donen", "spin", "whirl");
        AnimationClip hitClip = CharacterClips.Find(preferred, "cathit", "hit", "flinch");
        AnimationClip dieClip = CharacterClips.FindDeath(preferred);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParam(controller, ReadyParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, AttackParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, HitParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, DieParam, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        AnimatorState idle = EnsureState(sm, "Idle", idleClip, new Vector3(260f, 0f, 0f));
        AnimatorState ready = EnsureState(sm, "SwordsReady", readyClip, new Vector3(520f, -90f, 0f));
        AnimatorState attack = EnsureState(sm, "DonenYaraks", attackClip, new Vector3(520f, 0f, 0f));
        AnimatorState hit = EnsureState(sm, "Hit", hitClip, new Vector3(520f, 90f, 0f));
        AnimatorState die = EnsureState(sm, "Die", dieClip, new Vector3(520f, 180f, 0f));

        sm.defaultState = idle;

        // Kılıçları çekme: bir kez oynar, sonra dövüş duruşuna (Idle) döner.
        EnsureAnyState(sm, ready, ReadyParam);
        EnsureTrans(ready, idle, true, 0.9f);

        // Dönen saldırı: 360 derece dönüş animasyonun içinde, kod rotasyona karışmıyor.
        EnsureAnyState(sm, attack, AttackParam);
        EnsureTrans(attack, idle, true, 0.9f);

        EnsureAnyState(sm, hit, HitParam);
        EnsureTrans(hit, idle, true, 0.8f);

        // Ölüm: çıkışı yoktur, son karede kalır.
        EnsureAnyState(sm, die, DieParam);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("BlackSwordsMan animasyon eslesmesi:\n" +
                  RoleLine("Idle", idleClip) +
                  RoleLine("SwordsReady", readyClip) +
                  RoleLine("DonenYaraks", attackClip) +
                  RoleLine("Hit", hitClip) +
                  RoleLine("Die", dieClip) +
                  "Hazir: " + ControllerPath, controller);

        WarnMissing("Idle", idleClip);
        WarnMissing("SwordsReady", readyClip);
        WarnMissing("DonenYaraks", attackClip);
        WarnMissing("Hit", hitClip);
        WarnMissing("Die", dieClip);

        Selection.activeObject = controller;
    }

    /// <summary>
    /// Seçili nöbetçiyi kurar: Health (hasar/ölüm), collider (yoksa kapsül),
    /// BlackSwordsman AI'si ve Animator + controller. Var olan ayarları ezmez.
    /// </summary>
    [MenuItem("Tools/Enemy/Setup Selected Black Swordsman")]
    public static void SetupSelected()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning(ControllerPath + " yok. Once Tools/Enemy/Create Black Swordsman Animator calistir.");
            return;
        }

        int touched = 0;

        foreach (GameObject go in Selection.gameObjects)
        {
            var health = go.GetComponent<Health>();
            if (health == null) health = Undo.AddComponent<Health>(go);

            // Nobetci itilmez: BlackSwordsman bunu Awake'te de zorluyor, ama Inspector'da
            // 0 gorunsun ki calisma anindaki davranisla sahne verisi celismesin.
            if (health.KnockbackMultiplier > 0f)
            {
                Undo.RecordObject(health, "Disable Knockback");
                health.KnockbackMultiplier = 0f;
                EditorUtility.SetDirty(health);
            }

            // Collider olmadan hasar ALMAZ: PlayerAttack kure taramasi collider arar.
            if (go.GetComponentInChildren<Collider>(true) == null) AddCharacterCapsule(go);

            if (go.GetComponent<BlackSwordsman>() == null) Undo.AddComponent<BlackSwordsman>(go);

            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = Undo.AddComponent<Animator>(go);

            if (animator.runtimeAnimatorController == null)
            {
                Undo.RecordObject(animator, "Assign Black Swordsman Controller");
                animator.runtimeAnimatorController = controller;
            }

            touched++;
        }

        Debug.Log("[BlackSwordsmanSetup] " + touched + " nobetci hazirlandi " +
                  "(Health + Collider + BlackSwordsman + Animator).");
    }

    [MenuItem("Tools/Enemy/Setup Selected Black Swordsman", true)]
    private static bool SetupSelectedValidate() => Selection.gameObjects.Length > 0;

    /// <summary>
    /// Modelin görünür sınırlarına oturan bir CapsuleCollider ekler. Yarıçap, T-poz
    /// kollarından şişmesin diye boyun dörtte biriyle sınırlanır.
    /// </summary>
    private static void AddCharacterCapsule(GameObject go)
    {
        var capsule = Undo.AddComponent<CapsuleCollider>(go);
        capsule.direction = 1;   // Y ekseni (ayakta duran karakter)

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

        if (!bounds.HasValue) return;

        Vector3 scale = go.transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        float sy = Mathf.Max(0.0001f, Mathf.Abs(scale.y));
        float sz = Mathf.Max(0.0001f, Mathf.Abs(scale.z));

        Bounds b = bounds.Value;
        float height = b.size.y / sy;
        float radius = Mathf.Max(b.size.x / sx, b.size.z / sz) * 0.5f;

        capsule.height = height;
        capsule.radius = Mathf.Min(radius, height * 0.25f);
        capsule.center = go.transform.InverseTransformPoint(b.center);
    }

    // --- Yardimcilar (BossAnimatorSetup ile ayni sozlesme) ---

    private static string RoleLine(string role, AnimationClip clip)
    {
        if (clip == null) return "  " + role + ": -\n";

        string source = System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(clip));
        return "  " + role + ": " + clip.name + "  (" + source + ")\n";
    }

    private static void WarnMissing(string role, AnimationClip clip)
    {
        if (clip == null)
            Debug.LogWarning("'" + role + "' icin klip eslesmedi. " + ControllerPath + " icinde " +
                             role + " state'ini sec ve Motion alanina dogru klibi surukle.");
    }

    private static void EnsureParam(AnimatorController controller, string name,
        AnimatorControllerParameterType type)
    {
        if (controller.parameters.Any(p => p.name == name)) return;
        controller.AddParameter(name, type);
    }

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
        // Eşleşen klip yoksa elle atanmış motion'ı ezme.
        if (clip != null) state.motion = clip;

        return state;
    }

    private static void EnsureAnyState(AnimatorStateMachine sm, AnimatorState target, string trigger)
    {
        if (target == null) return;
        if (sm.anyStateTransitions.Any(t => t.destinationState == target)) return;

        var transition = sm.AddAnyStateTransition(target);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
    }

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
