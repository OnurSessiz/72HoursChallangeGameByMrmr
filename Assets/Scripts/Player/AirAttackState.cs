using UnityEngine;

/// <summary>
/// Uçan tekme (havada saldırı). Zıpladıktan sonra saldırıya basılınca girilir:
/// karakter baktığı/gittiği yöne doğru ileri fırlar, Kick1 animasyonu oynar ve
/// uçuş boyunca önündeki düşmanlara tekme hasarı uygular.
///
/// Yerdeki combo'dan (AttackState) farkı:
///   - Yatay hız FRENLENMEZ, tam tersine korunur; tekme ileri taşır.
///   - Combo yoktur; tek bir güçlü vuruştur.
///   - Hasar penceresi uçuş boyu açıktır (PlayerAttack her hedefe bir kez vurur),
///     yani düşmana ne zaman ulaşırsan o an isabet eder.
///
/// Bitiş koşulları (hangisi önce olursa): yere iniş, animasyon bitiş event'i
/// (AttackStepEnded) veya KickMaxDuration güvenlik zaman aşımı.
/// </summary>
public class AirAttackState : IPlayerState
{
    private readonly PlayerController _ctx;

    private Vector3 _kickDir;
    private float _startTime;
    private float _endTime;
    private bool _finished;

    public AirAttackState(PlayerController ctx)
    {
        _ctx = ctx;
    }

    public void Enter()
    {
        _finished = false;
        _startTime = Time.time;
        _endTime = Time.time + _ctx.KickMaxDuration;

        // Jump başına tek tekme: havada süresiz asılı kalmayı önler.
        _ctx.MarkAirAttackUsed();

        // Yön: hareket girişi varsa o yön, yoksa karakterin baktığı yön.
        Vector3 inputDir = _ctx.GetCameraRelativeMoveDirection();
        _kickDir = inputDir.sqrMagnitude > 0.0001f ? inputDir : _ctx.transform.forward;

        // Havada yumuşak rotasyona vakit yok; tekme yönüne anında dön.
        _ctx.Rb.MoveRotation(Quaternion.LookRotation(_kickDir, Vector3.up));

        // İleri fırlat + hafif yukarı itiş (uçan tekme hissi).
        Vector3 v = _kickDir * _ctx.KickForwardSpeed;
        v.y = Mathf.Max(_ctx.Rb.linearVelocity.y, 0f) + _ctx.KickUpwardBoost;
        _ctx.Rb.linearVelocity = v;

        // Animasyon + hasar penceresi (PlayerAttack bu event'le tekme ayarına geçer).
        _ctx.TriggerAirAttack();

        _ctx.AttackStepEnded += OnStepEnded;
    }

    public void Tick()
    {
        if (_finished) return;

        // Güvenlik zaman aşımı (animasyon event'i kurulmamış olabilir).
        if (Time.time >= _endTime)
        {
            Finish();
            return;
        }

        // Yere indiyse tekme biter. İlk karelerde ayak hâlâ yere yakın olabildiği için
        // KickMinAirTime dolmadan zemin kontrolüne bakma.
        if (Time.time >= _startTime + _ctx.KickMinAirTime && _ctx.IsGrounded())
            Finish();
    }

    public void FixedTick()
    {
        // Uçuş boyunca yatay hızı koru; sona doğru yumuşakça yavaşlat.
        float t = Mathf.Clamp01((Time.time - _startTime) / Mathf.Max(0.01f, _ctx.KickMaxDuration));
        float speed = _ctx.KickForwardSpeed * Mathf.Lerp(1f, _ctx.KickSpeedFalloff, t);

        Vector3 v = _kickDir * speed;
        v.y = _ctx.Rb.linearVelocity.y;   // dikeyi yerçekimine bırak
        _ctx.Rb.linearVelocity = v;
    }

    public void Exit()
    {
        _ctx.AttackStepEnded -= OnStepEnded;
        _ctx.EndAirAttack();

        // İniş sonrası kaymayı önlemek için yatay hızı sıfırla (Dash ile aynı davranış).
        Vector3 v = _ctx.Rb.linearVelocity;
        v.x = 0f;
        v.z = 0f;
        _ctx.Rb.linearVelocity = v;
    }

    private void OnStepEnded() => Finish();

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _ctx.ChangeState(_ctx.Locomotion);
    }
}
