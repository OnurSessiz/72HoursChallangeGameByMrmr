using UnityEngine;

/// <summary>
/// Animator ile AYNI GameObject'e konur. Animation Event'ler yalnızca Animator'ın
/// bulunduğu GameObject'teki MonoBehaviour metotlarını çağırabildiği için (mobun
/// Animator'ı çoğu zaman bir child'da olur), event'ler bu köprü üzerinden
/// EnemyFollow'a iletilir.
///
/// Neden gerekli: hırtların saldırı klipleri oyuncunun FBX'inden gelebiliyor ve
/// içlerinde oyuncu için eklenmiş AttackHit / AttackStepEnd event'leri var. Alıcı
/// olmayınca Unity "AttackHit has no receiver" uyarısı basar.
///
/// EnemyFollow'daki "Use Animation Event Hit" kapalıysa event'ler sadece yutulur
/// (hasar zamanlaması hitDelay ile sürer); açıksa vuruş tam bu frame'de uygulanır.
/// </summary>
[DisallowMultipleComponent]
public class EnemyAnimationEvents : MonoBehaviour
{
    [Tooltip("Boşsa parent'lardan otomatik bulunur.")]
    [SerializeField] private EnemyFollow enemy;

    private void Awake()
    {
        if (enemy == null) enemy = GetComponentInParent<EnemyFollow>();
    }

    /// <summary>Animation Event: saldırının vuruş (hasar) frame'i.</summary>
    public void AttackHit()
    {
        if (enemy != null) enemy.NotifyAttackHit();
    }

    /// <summary>Animation Event: saldırı animasyonunun bittiği frame.</summary>
    public void AttackStepEnd()
    {
        if (enemy != null) enemy.NotifyAttackStepEnd();
    }
}
