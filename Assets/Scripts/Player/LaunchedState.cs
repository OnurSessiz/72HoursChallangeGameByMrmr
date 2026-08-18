using UnityEngine;

/// <summary>
/// Savrulma (knockback) state'i: oyuncu ağır bir vuruşla havaya fırlatıldığında girilir.
/// Uçuş boyunca kontrol tamamen fizikte kalır; input'a tepki verilmez.
///
/// Neden ayrı bir state gerekiyor: LocomotionState her FixedUpdate'te Rb.MovePosition
/// çağırıyor. Girdi yokken hedef konum = mevcut konum olduğu için MovePosition oyuncuyu
/// yerine sabitler ve AddForce ile verilen itme bir sonraki fizik karesinde silinir.
/// Yani savrulma ancak Locomotion devre dışıyken görünür.
///
/// Bitiş (hangisi önce olursa): yere iniş (LaunchMinAirTime sonrası) ya da
/// LaunchMaxDuration güvenlik zaman aşımı.
/// </summary>
public class LaunchedState : IPlayerState
{
    private readonly PlayerController _ctx;

    private Vector3 _velocity;
    private float _startTime;
    private float _endTime;
    private bool _finished;

    public LaunchedState(PlayerController ctx)
    {
        _ctx = ctx;
    }

    /// <summary>Savrulma hızını ayarlar. PlayerController.Launch() state'e girmeden önce çağırır.</summary>
    public void SetVelocity(Vector3 velocity)
    {
        _velocity = velocity;
    }

    public void Enter()
    {
        _finished = false;
        _startTime = Time.time;
        _endTime = Time.time + _ctx.LaunchMaxDuration;

        // Hız doğrudan yazılır: AddForce yerine mutlak atama, kütleden bağımsız
        // öngörülebilir mesafe verir (BlackSwordsman metre cinsinden hesaplıyor).
        _ctx.Rb.linearVelocity = _velocity;

        // Savrulurken tekme hakkı geri gelmesin; yere inince Locomotion zaten yeniler.
        _ctx.EndAirAttack();
        _ctx.SetAnimTrigger("Hit");
    }

    public void Tick()
    {
        if (_finished) return;

        // Güvenlik zaman aşımı: bir yere sıkışırsa oyuncu kilitli kalmasın.
        if (Time.time >= _endTime)
        {
            Finish();
            return;
        }

        // İlk karelerde ayak hâlâ yere yakın olduğu için minimum hava süresi bekle.
        if (Time.time >= _startTime + _ctx.LaunchMinAirTime && _ctx.IsGrounded())
            Finish();
    }

    public void FixedTick()
    {
        // Hiçbir şey yapma: uçuşu tamamen yerçekimi ve çarpışmalar belirlesin.
        // Locomotion'ın MovePosition'ı burada çalışmadığı için savrulma korunur.
    }

    public void Exit()
    {
        // İniş sonrası kaymayı önle (Dash ve uçan tekme ile aynı davranış).
        Vector3 v = _ctx.Rb.linearVelocity;
        v.x = 0f;
        v.z = 0f;
        _ctx.Rb.linearVelocity = v;
    }

    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        _ctx.ChangeState(_ctx.Locomotion);
    }
}
