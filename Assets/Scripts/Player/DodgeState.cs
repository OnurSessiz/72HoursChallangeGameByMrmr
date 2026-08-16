using UnityEngine;

/// <summary>
/// Yön kilitli takla. Enter'da i-frame (IsInvincible) açılır, Exit'te kapanır.
/// Süre bitince Locomotion'a döner. Cooldown'lı.
/// Yön Enter'da kilitlenir: giriş varsa o yön, yoksa karakterin baktığı yön.
/// </summary>
public class DodgeState : IPlayerState
{
    private readonly PlayerController _ctx;
    private float _endTime;
    private Vector3 _dodgeDir;

    public DodgeState(PlayerController ctx)
    {
        _ctx = ctx;
    }

    public void Enter()
    {
        _ctx.MarkDodgeUsed();
        _ctx.IsInvincible = true;               // i-frame aç
        _endTime = Time.time + _ctx.DodgeDuration;

        Vector3 inputDir = _ctx.GetCameraRelativeMoveDirection();
        _dodgeDir = inputDir.sqrMagnitude > 0.0001f ? inputDir : _ctx.transform.forward;

        // Takla yönüne anında dön.
        _ctx.Rb.MoveRotation(Quaternion.LookRotation(_dodgeDir, Vector3.up));
    }

    public void Tick()
    {
        if (Time.time >= _endTime)
            _ctx.ChangeState(_ctx.Locomotion);
    }

    public void FixedTick()
    {
        // Kilitli yönde itiş; dikey hızı koru.
        Vector3 v = _dodgeDir * _ctx.DodgeSpeed;
        v.y = _ctx.Rb.linearVelocity.y;
        _ctx.Rb.linearVelocity = v;
    }

    public void Exit()
    {
        _ctx.IsInvincible = false;              // i-frame kapat

        Vector3 v = _ctx.Rb.linearVelocity;
        v.x = 0f;
        v.z = 0f;
        _ctx.Rb.linearVelocity = v;
    }
}