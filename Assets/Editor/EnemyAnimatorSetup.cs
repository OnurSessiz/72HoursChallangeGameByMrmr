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
///   Any State -(Attack1)-> Attack1 -> Idle   (EnemyFollow.attackTrigger)
///   Any State -(Whistle)-> Whistle -> Idle   (HirtRoomAmbush; klip sonra atanabilir)
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
    /// <summary>Hırt modellerinin klasörü. Klipler önce burada aranır ki oyuncunun
    /// animasyonları (ve içlerindeki AttackHit event'leri) moba sızmasın.</summary>
    private const string EnemyModelsFolder = "Assets/Models/enemy";

    private const string SpeedParam = "Speed";
    private const string HitParam = "Hit";
    private const string DieParam = "Die";
    private const string AttackParam = "Attack1";
    private const string WhistleParam = "Whistle";

    [MenuItem("Tools/Enemy/Create Enemy Animator")]
    public static void Create()
    {
        // Önce hırt FBX'leri: aynı isimli klip oyuncunun FBX'inde de varsa mobunki kazanır.
        string[] preferred = EnemyFbxPaths();

        AnimationClip idleClip = CharacterClips.Find(preferred, "idle", "bekle");
        AnimationClip walkClip = CharacterClips.Find(preferred, "catwalk", "realwalk", "walk", "yuru", "run");
        AnimationClip hitClip = CharacterClips.Find(preferred, "cathit", "hit", "flinch");
        AnimationClip dieClip = CharacterClips.FindDeath(preferred);
        AnimationClip attackClip = CharacterClips.Find(preferred, "attack1", "attackone", "atak1", "punch", "attack");
        // Islık klibi daha hazır olmayabilir; bulunamazsa state boş (slot olarak) kurulur.
        AnimationClip whistleClip = CharacterClips.Find(preferred, "whistle", "islik", "ıslık", "sinyal", "call");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        EnsureParam(controller, SpeedParam, AnimatorControllerParameterType.Float);
        EnsureParam(controller, HitParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, DieParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, AttackParam, AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, WhistleParam, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        AnimatorState idle = EnsureState(sm, "Idle", idleClip, new Vector3(260f, 0f, 0f));
        AnimatorState walk = EnsureState(sm, "Walk", walkClip, new Vector3(260f, 90f, 0f));
        AnimatorState hit = EnsureState(sm, "Hit", hitClip, new Vector3(520f, -40f, 0f));
        AnimatorState die = EnsureState(sm, "Die", dieClip, new Vector3(520f, 60f, 0f));
        AnimatorState attack = EnsureState(sm, "Attack1", attackClip, new Vector3(520f, 150f, 0f));
        // Islık: hırt odası pususunda ilk hırtın çaldığı animasyon. Klip sonra atanabilir.
        AnimatorState whistle = EnsureState(sm, "Whistle", whistleClip, new Vector3(520f, 240f, 0f));

        sm.defaultState = idle;

        // Idle <-> Walk: EnemyFollow'un yazdığı Speed ile sürülür.
        EnsureTrans(idle, walk, false, 0f, (SpeedParam, AnimatorConditionMode.Greater, 0.1f));
        EnsureTrans(walk, idle, false, 0f, (SpeedParam, AnimatorConditionMode.Less, 0.1f));

        // Hasar tepkisi: her yerden kesip oynar, bitince Idle'a döner.
        EnsureAnyState(sm, hit, HitParam);
        EnsureTrans(hit, idle, true, 0.8f);

        // Saldırı: her yerden kesip oynar, bitince Idle'a döner (EnemyFollow tetikler).
        EnsureAnyState(sm, attack, AttackParam);
        EnsureTrans(attack, idle, true, 0.85f);

        // Islık: HirtRoomAmbush tetikler; bitince Idle'a döner, sonra saldırı başlar.
        EnsureAnyState(sm, whistle, WhistleParam);
        EnsureTrans(whistle, idle, true, 0.9f);

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
                  RoleLine("Attack1", attackClip) +
                  RoleLine("Whistle", whistleClip) +
                  "Hazir: " + ControllerPath + "\nMobun Animator'ina bu controller'i ata " +
                  "(ya da Tools/Enemy/Setup Selected Enemies kullan).", controller);

        WarnMissing("Idle", idleClip);
        WarnMissing("Walk", walkClip);
        WarnMissing("Hit", hitClip);
        WarnMissing("Die", dieClip);
        WarnMissing("Attack1", attackClip);
        // Islık klibi henüz hazır olmayabilir; eksikse uyarma, sadece bilgi ver.
        if (whistleClip == null)
            Debug.Log("Islik klibi bulunamadi. Enemy.controller icindeki bos Whistle state'ine " +
                      "animasyonu hazir olunca surukle (ya da adinda 'Whistle' gecen klibi projeye " +
                      "atip bu araci tekrar calistir).");

        Selection.activeObject = controller;
    }

    /// <summary>
    /// Seçili mobları dövüşe hazırlar: Health, EnemyFollow ve Animator ekler; Animator'ın
    /// controller'ı boşsa Enemy.controller'ı atar. Var olan ayarları ezmez.
    /// </summary>
    [MenuItem("Tools/Enemy/Setup Selected Enemies")]
    public static void SetupSelected() => Setup(false);

    /// <summary>
    /// Pusu hırtları: aynı kurulum + "Activate On Start" kapatılır, böylece ıslık
    /// çalınana kadar beklerler (HirtRoomAmbush uyandırır).
    /// </summary>
    [MenuItem("Tools/Enemy/Setup Selected Ambush Enemies")]
    public static void SetupSelectedAmbush() => Setup(true);

    [MenuItem("Tools/Enemy/Setup Selected Ambush Enemies", true)]
    private static bool SetupSelectedAmbushValidate() => Selection.gameObjects.Length > 0;

    private static void Setup(bool waitForWhistle)
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

            // Collider olmadan mob HASAR ALMAZ: PlayerAttack vuruşu OverlapSphere ile
            // tarıyor, collider bulamazsa Health'e hiç ulaşamaz. FBX modellerinde
            // collider gelmediği için modelin sınırlarına göre bir kapsül ekliyoruz.
            if (go.GetComponentInChildren<Collider>(true) == null) AddCharacterCapsule(go);

            var follow = go.GetComponent<EnemyFollow>();
            if (follow == null) follow = Undo.AddComponent<EnemyFollow>(go);

            // activateOnStart private olduğu için SerializedObject üzerinden yazılır.
            if (waitForWhistle)
            {
                var so = new SerializedObject(follow);
                so.FindProperty("activateOnStart").boolValue = false;
                so.ApplyModifiedProperties();
            }

            // Animator zaten bir child'da olabilir (skinned mesh kurulumlarında sık).
            var animator = go.GetComponentInChildren<Animator>(true);
            if (animator == null) animator = Undo.AddComponent<Animator>(go);

            // Animation Event köprüsü Animator ile AYNI objede olmalı; saldırı klipleri
            // oyuncunun FBX'inden geldiğinde "AttackHit has no receiver" uyarısını keser.
            if (animator.GetComponent<EnemyAnimationEvents>() == null)
                Undo.AddComponent<EnemyAnimationEvents>(animator.gameObject);

            if (animator.runtimeAnimatorController == null)
            {
                Undo.RecordObject(animator, "Assign Enemy Controller");
                animator.runtimeAnimatorController = controller;
            }

            touched++;
        }

        Debug.Log("[EnemyAnimatorSetup] " + touched + " mob hazirlandi (Health + Collider + EnemyFollow + Animator)" +
                  (waitForWhistle ? " - islik bekliyorlar." : "."));
    }

    [MenuItem("Tools/Enemy/Setup Selected Enemies", true)]
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

        // Görünür mesh yoksa Unity'nin varsayılan kapsülü kalsın.
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

    /// <summary>Assets/Models/enemy altındaki tüm model dosyaları (öncelikli klip kaynağı).</summary>
    private static string[] EnemyFbxPaths()
    {
        if (!AssetDatabase.IsValidFolder(EnemyModelsFolder)) return new string[0];

        return AssetDatabase.FindAssets("t:Model", new[] { EnemyModelsFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .ToArray();
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
