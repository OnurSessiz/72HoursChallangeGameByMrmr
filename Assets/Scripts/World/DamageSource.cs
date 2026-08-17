using UnityEngine;

/// <summary>
/// Oyuncuya hasar veren tehlike. Tek script'le birden çok senaryo:
///   - Düşen obje: damage + destroyOnHit (temas edince yok olur).
///   - Lava/uçurum: instantKill (ani ölüm) + isTrigger volume.
///   - Sürekli hasar alanı: continuous + tickInterval.
///
/// Hem Collision (katı temas) hem Trigger (isTrigger collider) ile çalışır.
/// Oyuncuyu, çarpan collider'ın parent'larında PlayerHealth arayarak bulur.
/// </summary>
public class DamageSource : MonoBehaviour
{
    [Header("Hasar")]
    [Tooltip("Verilecek hasar (instantKill açıksa göz ardı edilir).")]
    [SerializeField] private float damage = 20f;
    [Tooltip("Açıksa temas anında oyuncuyu öldürür (lava vb.).")]
    [SerializeField] private bool instantKill = false;

    [Header("Davranış")]
    [Tooltip("Vurunca bu obje yok olsun mu? (düşen taş gibi tek kullanımlık).")]
    [SerializeField] private bool destroyOnHit = false;
    [Tooltip("Açıksa alan içinde kaldıkça tickInterval'da bir tekrar hasar verir.")]
    [SerializeField] private bool continuous = false;
    [Tooltip("continuous açıkken iki hasar arası süre (saniye).")]
    [SerializeField] private float tickInterval = 0.5f;

    private float _lastTickTime = Mathf.NegativeInfinity;

    private void OnCollisionEnter(Collision collision) => TryHit(collision.collider);
    private void OnTriggerEnter(Collider other) => TryHit(other);

    private void OnTriggerStay(Collider other)
    {
        if (continuous) TryHit(other);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (continuous) TryHit(collision.collider);
    }

    private void TryHit(Collider col)
    {
        var health = col.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead) return;

        // Sürekli hasarda tick aralığına uy.
        if (continuous && Time.time < _lastTickTime + tickInterval) return;
        _lastTickTime = Time.time;

        if (instantKill) health.Kill();
        else health.TakeDamage(damage);

        if (destroyOnHit) Destroy(gameObject);
    }
}
