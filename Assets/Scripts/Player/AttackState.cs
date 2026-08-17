using UnityEngine;

/// <summary>
/// Saldırı state'i: ard arda basıldığında çok adımlı combo çalıştırır.
/// Her adım farklı bir animasyon trigger'ı ateşler (PlayerController.attackTriggers).
///
/// Adım geçişi Animation Event ile sürülür: saldırı animasyonunun bitiş frame'inde
/// PlayerController.AttackStepEnded tetiklenir. O ana kadar tekrar Attack basılmışsa
/// sonraki combo adımına geçilir; basılmamışsa ya da son adıma gelinmişse Locomotion'a döner.
///
/// Güvenlik: Animation Event kurulmamış/atlanmış olursa oyuncu sonsuza dek saldırıda
/// takılı kalmasın diye AttackDuration bir üst-sınır zaman aşımı olarak da adımı bitirir.
/// </summary>
public class AttackState : IPlayerState
{
    private readonly PlayerController _ctx;

    private int _comboStep;         // Aktif combo adımı (1..MaxComboStep).
    private bool _comboQueued;      // Bu adım sırasında bir sonraki saldırı istendi mi?
    private float _safetyEndTime;   // Event gelmezse adımı bitiren üst-sınır zaman.

    public AttackState(PlayerController ctx)
    {
        _ctx = ctx;
    }

    public void Enter()
    {
        // Combo penceresi boyunca girişi ve animasyon bitiş event'ini dinle (TEK abonelik).
        _ctx.Input.AttackPressed += OnAttack;
        _ctx.AttackStepEnded += OnStepEnded;

        // İlk adımdan başla.
        _comboStep = 0;
        StartNextStep();
    }

    public void Tick()
    {
        // Yalnızca güvenlik zaman aşımı: Animation Event gelmediyse adımı zorla bitir.
        if (Time.time >= _safetyEndTime)
            OnStepEnded();
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
        _ctx.AttackStepEnded -= OnStepEnded;
    }

    /// <summary>Sonraki combo adımını başlatır: animasyonu ateşler, güvenlik timer'ını ve kuyruğu sıfırlar.</summary>
    private void StartNextStep()
    {
        _comboStep++;
        _comboQueued = false;
        // AttackDuration'ı gerçek animasyon süresinden biraz uzun tut ki normalde
        // Animation Event tetiklensin, bu yalnızca ağ/kurulum hatalarına karşı emniyet olsun.
        _safetyEndTime = Time.time + _ctx.AttackDuration;

        // Bu adıma özel saldırı animasyonunu tetikle.
        _ctx.TriggerAttack(_comboStep);
    }

    /// <summary>Adım bittiğinde (event veya güvenlik): combo'yu ilerlet ya da çık.</summary>
    private void OnStepEnded()
    {
        if (_comboQueued && _comboStep < _ctx.MaxComboStep)
            StartNextStep();
        else
            _ctx.ChangeState(_ctx.Locomotion);
    }

    private void OnAttack()
    {
        // Adım bitmeden basılan saldırı bir sonraki combo adımını kuyruğa alır.
        _comboQueued = true;
    }
}
