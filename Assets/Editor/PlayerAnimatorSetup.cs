using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// PlayerLocomotion.controller'a hareket + hava + hit animasyonlarını tek tıkla bağlar:
/// eksik parametreleri, state'leri ve geçişleri ekler. Idempotent (var olanı tekrar eklemez).
/// Menü: Tools/Player/Bind Movement Animations.
///
/// Kurulan akış:
///   Dash / Dodge        : Any State -(trigger)-> state -> Idle (exit time)
///   Jump / Fall / Land  : Any State -(Jump)-> JumpStart -(VerticalSpeed<0)-> Falling
///                         Falling -(IsGrounded)-> Land -> Idle;  Idle/Walk -(!IsGrounded)-> Falling
///   Hit                 : Any State -(Hit)-> Hit -> Idle (exit time)
///   Kick1 (ucan tekme)  : Any State -(Kick1)-> Kick1 -> Idle (exit time)
/// </summary>
public static class PlayerAnimatorSetup
{
    private const string ControllerPath = "Assets/Animations/PlayerLocomotion.controller";
    private const string FbxPath = "Assets/Models/temelkarakter14.fbx";
    // Uçan tekme klibi ayrı bir FBX'te (metarig|Kick1).
    private const string KickFbxPath = "Assets/Models/ıwinL.fbx";

    [MenuItem("Tools/Player/Bind Movement Animations")]
    public static void Bind()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) { Debug.LogError($"Controller bulunamadi: {ControllerPath}"); return; }

        var sm = controller.layers[0].stateMachine;
        var idle = FindState(sm, "Idle");
        var walk = FindState(sm, "Walk");
        if (idle == null) { Debug.LogError("Idle state bulunamadi."); return; }

        // Parametreler
        EnsureParam(controller, "Dash", AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, "Dodge", AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, "Jump", AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, "Hit", AnimatorControllerParameterType.Trigger);
        EnsureParam(controller, "IsGrounded", AnimatorControllerParameterType.Bool);
        EnsureParam(controller, "VerticalSpeed", AnimatorControllerParameterType.Float);

        // --- Dash / Dodge: basit tek-atış, bitince Idle'a ---
        OneShotToIdle(sm, idle, "Dash", "Dash", "metarig|CatDash", 0.85f);
        OneShotToIdle(sm, idle, "Dodge", "DodgeRoll", "metarig|CatDodgeRoll", 0.85f);

        // --- Jump -> Fall -> Land ---
        var jumpStart = EnsureState(sm, "JumpStart", "metarig|CatJumpStart");
        var falling = EnsureState(sm, "Falling", "metarig|CatFalling");
        var land = EnsureState(sm, "Land", "metarig|CatLand");

        if (jumpStart != null)
            EnsureAnyState(sm, jumpStart, ("Jump", AnimatorConditionMode.If, 0f));

        // Eski kurulumdan kalma JumpStart->Idle dönüşünü kaldır (artık Falling'e zincirleniyor).
        if (jumpStart != null && idle != null)
            RemoveTrans(jumpStart, idle);

        if (jumpStart != null && falling != null)
            EnsureTrans(jumpStart, falling, false, 0f, ("VerticalSpeed", AnimatorConditionMode.Less, 0f));

        if (falling != null && land != null)
            EnsureTrans(falling, land, false, 0f, ("IsGrounded", AnimatorConditionMode.If, 0f));

        if (land != null)
            EnsureTrans(land, idle, true, 0.9f);

        // Zeminden ayrılınca (ledge'den düşme) Idle/Walk -> Falling
        if (falling != null)
        {
            EnsureTrans(idle, falling, false, 0f, ("IsGrounded", AnimatorConditionMode.IfNot, 0f));
            if (walk != null)
                EnsureTrans(walk, falling, false, 0f, ("IsGrounded", AnimatorConditionMode.IfNot, 0f));
        }

        // --- Kick1 (uçan tekme): havada saldırıda her yerden kesip oynar, bitince Idle'a ---
        EnsureParam(controller, "Kick1", AnimatorControllerParameterType.Trigger);
        var kick = EnsureState(sm, "Kick1", "metarig|Kick1", KickFbxPath);
        if (kick != null)
        {
            EnsureAnyState(sm, kick, ("Kick1", AnimatorConditionMode.If, 0f));
            EnsureTrans(kick, idle, true, 0.85f);
        }

        // --- Hit: her yerden kesip oynatır, bitince Idle'a ---
        var hit = EnsureState(sm, "Hit", "metarig|CatHit");
        if (hit != null)
        {
            EnsureAnyState(sm, hit, ("Hit", AnimatorConditionMode.If, 0f));
            EnsureTrans(hit, idle, true, 0.8f);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("Player animasyonlari baglandi (Dash/Dodge/Jump/Fall/Land/Hit/Kick1).");
    }

    // --- Yardimcilar ---

    private static void OneShotToIdle(AnimatorStateMachine sm, AnimatorState idle,
        string trigger, string stateName, string clipName, float backExitTime)
    {
        var state = EnsureState(sm, stateName, clipName);
        if (state == null) return;
        EnsureAnyState(sm, state, (trigger, AnimatorConditionMode.If, 0f));
        EnsureTrans(state, idle, true, backExitTime);
    }

    private static void EnsureParam(AnimatorController c, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in c.parameters)
            if (p.name == name) return;
        c.AddParameter(name, type);
    }

    private static AnimatorState EnsureState(AnimatorStateMachine sm, string name, string clipName,
        string fbxPath = FbxPath)
    {
        var existing = FindState(sm, name);
        if (existing != null) return existing;

        var clip = LoadClip(clipName, fbxPath);
        if (clip == null) { Debug.LogWarning($"Klip bulunamadi: {clipName} ({fbxPath})"); return null; }

        var state = sm.AddState(name);
        state.motion = clip;
        state.writeDefaultValues = true;
        return state;
    }

    private static void EnsureAnyState(AnimatorStateMachine sm, AnimatorState to,
        (string param, AnimatorConditionMode mode, float threshold) cond)
    {
        foreach (var t in sm.anyStateTransitions)
            if (t.destinationState == to) return;

        var tr = sm.AddAnyStateTransition(to);
        tr.hasExitTime = false;
        tr.duration = 0.1f;
        tr.canTransitionToSelf = false;
        tr.AddCondition(cond.mode, cond.threshold, cond.param);
    }

    private static void EnsureTrans(AnimatorState from, AnimatorState to,
        bool hasExitTime, float exitTime,
        (string param, AnimatorConditionMode mode, float threshold)? cond = null)
    {
        foreach (var t in from.transitions)
            if (t.destinationState == to) return;

        var tr = from.AddTransition(to);
        tr.hasExitTime = hasExitTime;
        tr.exitTime = exitTime;
        tr.duration = 0.1f;
        if (cond.HasValue)
            tr.AddCondition(cond.Value.mode, cond.Value.threshold, cond.Value.param);
    }

    private static void RemoveTrans(AnimatorState from, AnimatorState to)
    {
        foreach (var t in from.transitions)
            if (t.destinationState == to) { from.RemoveTransition(t); return; }
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (var cs in sm.states)
            if (cs.state.name == name) return cs.state;
        return null;
    }

    private static AnimationClip LoadClip(string clipName, string fbxPath = FbxPath)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && c.name == clipName) return c;
        return null;
    }
}
