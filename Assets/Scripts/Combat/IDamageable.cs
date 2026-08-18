using UnityEngine;

/// <summary>
/// Tek bir vuruşun tüm bilgisi. Hasar veren taraf doldurur, alan taraf
/// (Health / PlayerHealth) hasarı, knockback'i ve efektleri buna göre uygular.
/// </summary>
public struct DamageInfo
{
    /// <summary>Verilecek ham hasar.</summary>
    public float Amount;
    /// <summary>Vuruşun dünya üzerindeki temas noktası (VFX burada doğar).</summary>
    public Vector3 HitPoint;
    /// <summary>Vuran -> vurulan yönü (normalize, yatay). Knockback bu yöne uygulanır.</summary>
    public Vector3 HitDirection;
    /// <summary>Knockback kuvveti (0 = itme yok).</summary>
    public float KnockbackForce;
    /// <summary>Hasarı veren obje (oyuncu, düşman, tuzak). Dost ateşi filtrelemede kullanılır.</summary>
    public GameObject Source;
    /// <summary>
    /// Sersemletme süresi (saniye). 0 = sersemletme yok. Yalnızca IStunnable uygulayan
    /// hedefler (şu an boss) buna tepki verir; diğerleri sessizce yok sayar.
    /// </summary>
    public float StunDuration;

    public DamageInfo(float amount, Vector3 hitPoint, Vector3 hitDirection, float knockbackForce,
        GameObject source, float stunDuration = 0f)
    {
        Amount = amount;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        KnockbackForce = knockbackForce;
        Source = source;
        StunDuration = stunDuration;
    }
}

/// <summary>
/// Hasar alabilen her şeyin ortak arayüzü (düşman, boss, kırılabilir fıçı, oyuncu).
/// Saldıran taraf karşısındakinin ne olduğunu bilmek zorunda kalmaz.
/// </summary>
public interface IDamageable
{
    bool IsDead { get; }
    void TakeDamage(DamageInfo info);
}

/// <summary>
/// Sersemletilebilen davranışların arayüzü. Health, DamageInfo.StunDuration doluysa
/// aynı objedeki IStunnable'lara Stun() geçer; uygulamayan düşmanlar etkilenmez.
/// </summary>
public interface IStunnable
{
    /// <summary>Verilen süre boyunca sersemlet (hareket/saldırı kesilir). Üst üste gelirse süre uzar.</summary>
    void Stun(float duration);
}
