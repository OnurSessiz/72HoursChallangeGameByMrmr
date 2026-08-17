using UnityEngine;

/// <summary>
/// Varsayılan state: kamera-relative hareket + yumuşak rotasyon.
/// Diğer state'lere (Dash/Attack/Dodge/Jump) geçişler burada tetiklenir.
/// Buton olayları Enter'da abone olunur, Exit'te bırakılır (event-driven, bool yığını yok).
/// </summary>
public class LocomotionState : IPlayerState
{
    private readonly PlayerController _ctx;

    public LocomotionState(PlayerController ctx)
    {
        _ctx = ctx;
    }

    public void Enter()
    {
        var input = _ctx.Input;
        input.JumpPressed += OnJump;
        input.DashPressed += OnDash;
        input.AttackPressed += OnAttack;
        input.DodgePressed += OnDodge;
    }

    public void Exit()
    {
        var input = _ctx.Input;
        input.JumpPressed -= OnJump;
        input.DashPressed -= OnDash;
        input.AttackPressed -= OnAttack;
        input.DodgePressed -= OnDodge;
    }

    public void Tick()
    {
        // Anlık geçişler event'lerle yapıldığı için burada iş yok.
    }

    public void FixedTick()
    {
        Vector3 moveDir = _ctx.GetCameraRelativeMoveDirection();

        // Hareket: fizik dostu MovePosition.
        Vector3 targetPos = _ctx.Rb.position + moveDir * (_ctx.MoveSpeed * Time.fixedDeltaTime);
        _ctx.Rb.MovePosition(targetPos);

        // Rotasyon: yalnızca yön varsa, tüm yönleri kapsar (çapraz dahil), Slerp ile yumuşak.
        if (moveDir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir, Vector3.up);
            Quaternion smooth = Quaternion.Slerp(_ctx.Rb.rotation, targetRot, _ctx.RotationSpeed * Time.fixedDeltaTime);
            _ctx.Rb.MoveRotation(smooth);
        }
    }

    // --- Geçiş tetikleyicileri ---

    private void OnJump()
    {
        // Ground check + tek seferlik impulse.
        if (_ctx.IsGrounded())
        {
            _ctx.SetAnimTrigger("Jump");
            _ctx.Rb.AddForce(Vector3.up * _ctx.JumpForce, ForceMode.Impulse);
            // Hemen ardından gelen saldırı "jump + attack" sayılsın (uçan tekme).
            _ctx.MarkJumpUsed();
        }
    }

    private void OnDash()
    {
        if (_ctx.CanDash())
            _ctx.ChangeState(_ctx.Dash);
    }

    private void OnAttack()
    {
        // Havadaysa (ya da daha yeni zıpladıysa) yerdeki combo yerine uçan tekme.
        bool airborne = !_ctx.IsGrounded() || _ctx.JustJumped;

        if (airborne)
        {
            // Tekme hakkı bir hava süresinde bir kez; hakkı yoksa hiçbir şey yapma
            // (yerdeki combo'yu havada oynatmak animasyonu bozar).
            if (_ctx.CanAirAttack)
                _ctx.ChangeState(_ctx.AirAttack);
            return;
        }

        _ctx.ChangeState(_ctx.Attack);
    }

    private void OnDodge()
    {
        if (_ctx.CanDodge())
            _ctx.ChangeState(_ctx.Dodge);
    }
}