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

    public DamageInfo(float amount, Vector3 hitPoint, Vector3 hitDirection, float knockbackForce, GameObject source)
    {
        Amount = amount;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        KnockbackForce = knockbackForce;
        Source = source;
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
