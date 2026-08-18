using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Gökyüzünden düşüp yokuş aşağı yuvarlanan topun yaşam döngüsü.
/// Hasar bu script'te DEĞİL: topun üzerindeki DamageSource oyuncuya çarpınca
/// hasarı kendisi verir (lava/düşen taş ile aynı sözleşme). Burada yalnızca
/// topun sahneyi doldurmadan temizlenmesi ve çarpma efektleri yönetilir.
///
/// Temizlenme koşulları (hangisi önce olursa):
///   - lifetime saniye doldu
///   - despawnBelowY'nin altına düştü (haritadan çıktı)
///   - stopDuration boyunca stopSpeed altında kaldı (bir yere sıkıştı)
///
/// Kurulum: Tools/World/Create Rolling Ball prefab'ı hazır üretir.
/// Elle yapacaksan: Sphere + Rigidbody + SphereCollider + DamageSource + bu script.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public class RollingBall : MonoBehaviour
{
    [Header("Ömür")]
    [Tooltip("Kaç saniye sonra yok olsun. 0 = süresiz (sadece diğer koşullarla silinir).")]
    [SerializeField] private float lifetime = 25f;
    [Tooltip("Bu Y seviyesinin altına düşerse yok olur (denize/boşluğa kaçan toplar).")]
    [SerializeField] private float despawnBelowY = -50f;

    [Header("Sıkışma")]
    [Tooltip("Açıksa uzun süre duran top temizlenir; yolda engel birikmesin.")]
    [SerializeField] private bool despawnWhenStopped = true;
    [Tooltip("Bu hızın altı 'durdu' sayılır (m/s).")]
    [SerializeField] private float stopSpeed = 0.4f;
    [Tooltip("Durmuş sayılması için geçmesi gereken süre.")]
    [SerializeField] private float stopDuration = 4f;

    [Header("Çarpma")]
    [Tooltip("Bu hızın üstündeki çarpmalarda ses/efekt oynar. 0 = her çarpmada.")]
    [SerializeField] private float impactSpeedThreshold = 4f;
    [Tooltip("Çarpma anında doğacak efekt (toz). Opsiyonel.")]
    [SerializeField] private GameObject impactVfxPrefab;
    [SerializeField] private float impactVfxLifetime = 2f;
    [Tooltip("Yok olurken doğacak efekt (dağılma). Opsiyonel.")]
    [SerializeField] private GameObject despawnVfxPrefab;
    [SerializeField] private float despawnVfxLifetime = 2f;

    [Header("Ses")]
    [Tooltip("Boşsa Awake'te otomatik eklenir ve 3D yapılır.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Yere/duvara çarpınca çalar. Opsiyonel.")]
    [SerializeField] private AudioClip impactClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;
    [Tooltip("İki çarpma sesi arasındaki en kısa süre; yuvarlanırken ses makineli tüfeğe dönmesin.")]
    [SerializeField] private float impactSfxCooldown = 0.2f;

    [Header("Olaylar")]
    [Tooltip("Top yok olmadan hemen önce tetiklenir.")]
    public UnityEvent onDespawn;

    private Rigidbody _rb;
    private float _spawnTime;
    private float _stoppedSince = Mathf.NegativeInfinity;
    private float _lastImpactSfxTime = Mathf.NegativeInfinity;
    private bool _despawning;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();

        if (audioSource == null && impactClip != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }
    }

    private void OnEnable()
    {
        // Havuzlanmış/yeniden kullanılan topta sayaçlar sıfırlansın.
        _spawnTime = Time.time;
        _stoppedSince = Mathf.NegativeInfinity;
        _despawning = false;
    }

    /// <summary>Spawner topu doğurduktan sonra çağırır: ilk hız ve dönüş.</summary>
    public void Launch(Vector3 velocity, Vector3 angularVelocity)
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();

        _rb.linearVelocity = velocity;
        _rb.angularVelocity = angularVelocity;
    }

    private void Update()
    {
        if (_despawning) return;

        if (lifetime > 0f && Time.time >= _spawnTime + lifetime)
        {
            Despawn();
            return;
        }

        if (transform.position.y < despawnBelowY)
        {
            Despawn();
            return;
        }

        if (!despawnWhenStopped) return;

        // Yavaşlayan top hemen silinmesin; stopDuration boyunca yavaş kalmalı.
        if (_rb.linearVelocity.sqrMagnitude > stopSpeed * stopSpeed)
        {
            _stoppedSince = Mathf.NegativeInfinity;
            return;
        }

        if (_stoppedSince < 0f) _stoppedSince = Time.time;
        else if (Time.time >= _stoppedSince + stopDuration) Despawn();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (_despawning) return;
        if (collision.relativeVelocity.magnitude < impactSpeedThreshold) return;

        Vector3 point = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;

        if (impactVfxPrefab != null)
        {
            var vfx = Instantiate(impactVfxPrefab, point, Quaternion.identity);
            if (impactVfxLifetime > 0f) Destroy(vfx, impactVfxLifetime);
        }

        if (impactClip != null && Time.time >= _lastImpactSfxTime + impactSfxCooldown)
        {
            _lastImpactSfxTime = Time.time;
            if (audioSource != null) audioSource.PlayOneShot(impactClip, sfxVolume);
            else AudioSource.PlayClipAtPoint(impactClip, point, sfxVolume);
        }
    }

    /// <summary>Topu temizler. Spawner da (dalga bitince) dışarıdan çağırabilir.</summary>
    public void Despawn()
    {
        if (_despawning) return;
        _despawning = true;

        if (despawnVfxPrefab != null)
        {
            var vfx = Instantiate(despawnVfxPrefab, transform.position, Quaternion.identity);
            if (despawnVfxLifetime > 0f) Destroy(vfx, despawnVfxLifetime);
        }

        onDespawn?.Invoke();
        Destroy(gameObject);
    }
}
