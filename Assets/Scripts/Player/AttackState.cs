using UnityEngine;

/// <summary>
/// Saldırı state'i: süre boyunca hareketi kısıtlar, süre bitince Locomotion'a döner.
/// Combo için genişletilebilir: attackDuration içinde tekrar Attack basılırsa
/// bir sonraki combo adımı kuyruğa alınır ve mevcut adım bitince o adıma geçilir.
/// </summary>
public class AttackState : IPlayerState
{
    private readonly PlayerController _ctx;
    private float _endTime;
    private bool _comboQueued;

    public AttackState(PlayerController ctx)
    {
        _ctx = ctx;
    }

    public void Enter()
    {
        _endTime = Time.time + _ctx.AttackDuration;
        _comboQueued = false;

        // Combo penceresi için saldırı girişini dinle.
        _ctx.Input.AttackPressed += OnAttack;

        // Buraya animasyon/hasar tetikleme eklenebilir (combo adımına göre).
    }

    public void Tick()
    {
        if (Time.time < _endTime) return;

        if (_comboQueued)
        {
            // Sonraki combo adımı: state'i yeniden başlat (basit genişletilebilir yapı).
            Enter();
        }
        else
        {
            _ctx.ChangeState(_ctx.Locomotion);
        }
    }

    public void FixedTick()
    {
        // Saldırı sırasında hareket kısıtlı: yatay hızı frenle (yerçekimini koru).
        Vector3 v = _ctx.Rb.linearVelocity;
        v.x = 0f;
        v.z = 0f;
        _ctx.Rb.linearVelocity = v;
    }

    public void Exit()
    {
        _ctx.Input.AttackPressed -= OnAttack;
    }

    private void OnAttack()
    {
        // Süre dolmadan basılan saldırı bir sonraki combo adımını kuyruğa alır.
        _comboQueued = true;
        Debug.Log("Attack calisiyor!");
    }
}