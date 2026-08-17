using UnityEngine;

/// <summary>
/// Kısa süreli yüksek hızlı itiş. Süre bitince Locomotion'a döner. Cooldown'lı.
/// Yön: hareket girişi varsa o yön, yoksa karakterin baktığı yön.
/// </summary>
public class DashState : IPlayerState
{
    private readonly PlayerController _ctx;
    private float _endTime;
    private Vector3 _dashDir;

    public DashState(PlayerController ctx)
    {
        _ctx = ctx;
    }

    public void Enter()
    {
        _ctx.MarkDashUsed();
        _ctx.SetAnimTrigger("Dash");
        _endTime = Time.time + _ctx.DashDuration;

        Vector3 inputDir = _ctx.GetCameraRelativeMoveDirection();
        _dashDir = inputDir.sqrMagnitude > 0.0001f ? inputDir : _ctx.transform.forward;
    }

    public void Tick()
    {
        if (Time.time >= _endTime)
            _ctx.ChangeState(_ctx.Locomotion);
    }

    public void FixedTick()
    {
        // Yatay hızı dash yönünde sabitle, dikey hızı (yerçekimi) koru.
        Vector3 v = _dashDir * _ctx.DashSpeed;
        v.y = _ctx.Rb.linearVelocity.y;
        _ctx.Rb.linearVelocity = v;
    }

    public void Exit()
    {
        // Dash sonrası kaymayı önlemek için yatay hızı sıfırla.
        Vector3 v = _ctx.Rb.linearVelocity;
        v.x = 0f;
        v.z = 0f;
        _ctx.Rb.linearVelocity = v;
    }
}