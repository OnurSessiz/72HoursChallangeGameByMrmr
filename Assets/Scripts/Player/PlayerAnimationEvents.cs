using UnityEngine;

/// <summary>
/// Animator ile AYNI GameObject'e konur. Animation Event'ler yalnızca Animator'ın
/// bulunduğu GameObject'teki MonoBehaviour metotlarını çağırabildiği için, saldırı
/// animasyonlarındaki event'ler bu köprü üzerinden PlayerController'a iletilir.
///
/// Kurulum: her saldırı animasyonuna
///   - vuruş frame'inde   -> AttackHit
///   - bitiş frame'inde    -> AttackStepEnd
/// Animation Event'lerini ekle (fonksiyon adları birebir bu metotlarla aynı olmalı).
/// </summary>
public class PlayerAnimationEvents : MonoBehaviour
{
    [Tooltip("Boşsa parent'lardan otomatik bulunur.")]
    [SerializeField] private PlayerController controller;

    private void Awake()
    {
        if (controller == null) controller = GetComponentInParent<PlayerController>();
    }

    /// <summary>Animation Event: saldırının vuruş (hasar) frame'i.</summary>
    public void AttackHit()
    {
        if (controller != null) controller.NotifyAttackHit();
    }

    /// <summary>Animation Event: saldırı animasyonunun bittiği frame; combo burada ilerler.</summary>
    public void AttackStepEnd()
    {
        if (controller != null) controller.NotifyAttackStepEnd();
    }
}
