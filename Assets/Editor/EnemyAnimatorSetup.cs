using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Moblar için ortak Animator Controller üretir (Assets/Animations/Enemy.controller).
/// Oyuncu, boss ve moblar aynı iskeleti (metarig) paylaştığı için klipler ortaktır;
/// hangi FBX'te olduklarını CharacterClips bulur.
///
/// Kurulan akış:
///   Idle &lt;-(Speed)-&gt; Walk           (EnemyFollow Speed parametresini yazar)
///   Any State -(Hit)-> Hit -> Idle     (Health.hitTrigger)
///   Any State -(Die)-> Die             (Health.dieTrigger; dönüş yok, mob ölü kalır)
///
/// Menü:
///   Tools/Enemy/Create Enemy Animator   -> controller'ı üretir/günceller
///   Tools/Enemy/Setup Selected Enemies  -> seçili moblara Health + Animator ekler
/// İkisi de idempotent: tekrar çalıştırınca var olanı bozmaz.
/// </summary>
public static class EnemyAnimatorSetup
{
    private const string ControllerPath = "Assets/Animations/Enemy.controller";

    private const string SpeedParam = "Speed";
    private const string HitParam = "Hit";
    private const string DieParam = "Die";

    [MenuItem("Tools/Enemy/Create Enemy Animator")]
    public static void Create()
    {
        AnimationClip idleClip = CharacterClips.Find("idle", "bekle");
        AnimationClip walkClip = CharacterClips.Find("catwalk", "realwalk", "walk", "yuru", "run");
        AnimationClip hitClip = CharacterClips.Find("cathit", "hit", "flinch");
        AnimationClip dieClip = CharacterClips.FindDeath();

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParam(controller, SpeedParam, AnimatorControllerParameterType.Float);
        EnsureParam(controller, HitParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, DieParam, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        AnimatorState idle = EnsureState(sm, "Idle", idleClip, new Vector3(260f, 0f, 0f));
        AnimatorState walk = EnsureState(sm, "Walk", walkClip, new Vector3(260f, 90f, 0f));
        AnimatorState hit = EnsureState(sm, "Hit", hitClip, new Vector3(520f, -40f, 0f));
        AnimatorState die = EnsureState(sm, "Die", dieClip, new Vector3(520f, 60f, 0f));

        sm.defaultState = idle;

        // Idle <-> Walk: EnemyFollow'un yazdığı Speed ile sürülür.
        EnsureTrans(idle, walk, false, 0f, (SpeedParam, AnimatorConditionMode.Greater, 0.1f));
        EnsureTrans(walk, idle, false, 0f, (SpeedParam, AnimatorConditionMode.Less, 0.1f));

        // Hasar tepkisi: her yerden kesip oynar, bitince Idle'a döner.
        EnsureAnyState(sm, hit, HitParam);
        EnsureTrans(hit, idle, true, 0.8f);

        // Ölüm: her şeyi keser, çıkışı yoktur (obje Health.destroyDelay ile yok edilir).
        EnsureAnyState(sm, die, DieParam);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("Mob animasyon eslesmesi:\n" +
                  RoleLine("Idle", idleClip) +
                  RoleLine("Walk", walkClip) +
                  RoleLine("Hit", hitClip) +
                  RoleLine("Die", dieClip) +
                  "Hazir: " + ControllerPath + "\nMobun Animator'ina bu controller'i ata " +
                  "(ya da Tools/Enemy/Setup Selected Enemies kullan).", controller);

        WarnMissing("Idle", idleClip);
        WarnMissing("Walk", walkClip);
        WarnMissing("Hit", hitClip);
        WarnMissing("Die", dieClip);

        Selection.activeObject = controller;
    }

    /// <summary>
    /// Seçili mobları dövüşe hazırlar: Health (hasar/ölüm otoritesi) ve Animator ekler,
    /// Animator'ın controller'ı boşsa Enemy.controller'ı atar. Var olan ayarları ezmez.
    /// </summary>
    [MenuItem("Tools/Enemy/Setup Selected Enemies")]
    public static void SetupSelected()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogWarning(ControllerPath + " yok. Once Tools/Enemy/Create Enemy Animator calistir.");
            return;
        }

        int touched = 0;

        foreach (GameObject go in Selection.gameObjects)
        {
            if (go.GetComponent<Health>() == null) Undo.AddComponent<Health>(go);

            // Animator zaten bir child'da olabilir (skinned mesh kurulumlarında sık).
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = Undo.AddComponent<Animator>(go);

            if (animator.runtimeAnimatorController == null)
            {
                Undo.RecordObject(animator, "Assign Enemy Controller");
                animator.runtimeAnimatorController = controller;
            }

            touched++;
        }

        Debug.Log("[EnemyAnimatorSetup] " + touched + " mob hazirlandi (Health + Animator).");
    }

    [MenuItem("Tools/Enemy/Setup Selected Enemies", true)]
    private static bool SetupSelectedValidate() => Selection.gameObjects.Length > 0;

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

    /// <summary>State'i bulur ya da oluşturur; klip verilmişse Motion'ı günceller.</summary>
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
