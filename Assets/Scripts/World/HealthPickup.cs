using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Yerden toplanan can eşyası (portakal). Oyuncu değince canını yeniler ve yok olur.
/// Oyuncu, DamageSource ile aynı şekilde tanınır: çarpan collider'ın parent'larında
/// PlayerHealth aranır, böylece child collider'lar da çalışır.
///
/// Üç ayrı efekt slotu var:
///   idleVfxPrefab   : yerde beklerken sürekli oynar (parıltı/ışık sütunu). Portakalın
///                     child'ı olarak doğar; süzülmeyi takip eder, dönmesi opsiyoneldir.
///   pickupVfxPrefab : toplanma anında PORTAKALIN yerinde patlar.
///   healVfxPrefab   : toplanma anında OYUNCUNUN üzerinde oynar (iyileşme efekti);
///                     istenirse oyuncuya bağlanır ve onunla birlikte hareket eder.
/// Hepsi opsiyonel: atanmayan slot sessizce atlanır.
///
/// Kurulum: modelin üzerine bir Collider koy ve "Is Trigger" işaretle
/// (Tools/World/Create Orange Pickup bunu ve efekt slotlarını senin yerine doldurur).
///
/// Canı zaten doluysa varsayılan olarak toplanmaz (portakal yerde bekler);
/// consumeWhenFull açılırsa dokunulduğu an harcanır.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class HealthPickup : MonoBehaviour
{
    [Header("İyileştirme")]
    [Tooltip("Toplanınca yenilenecek can.")]
    [SerializeField] private float healAmount = 25f;
    [Tooltip("Açıksa can doluyken de toplanır (boşa gider). Kapalıysa yerde bekler.")]
    [SerializeField] private bool consumeWhenFull = false;

    [Header("Görünüm")]
    [Tooltip("Saniyedeki dönüş hızı (derece). 0 = dönmez.")]
    [SerializeField] private float spinSpeed = 90f;
    [Tooltip("Aşağı-yukarı süzülme yüksekliği (birim). 0 = sabit durur.")]
    [SerializeField] private float bobHeight = 0.15f;
    [Tooltip("Süzülmenin hızı.")]
    [SerializeField] private float bobSpeed = 2f;

    [Header("Efekt: yerde beklerken")]
    [Tooltip("Portakal yerde dururken sürekli oynayacak efekt (ışık sütunu, parıltı). Opsiyonel.")]
    [SerializeField] private GameObject idleVfxPrefab;
    [Tooltip("Efektin portakala göre konumu (ışık sütununu yere indirmek için Y'yi düşür).")]
    [SerializeField] private Vector3 idleVfxOffset = Vector3.zero;
    [Tooltip("Açıksa portakal dönerken efekt dönmez (ışık sütunu sabit kalır).")]
    [SerializeField] private bool keepIdleVfxUpright = true;

    [Header("Efekt: toplanınca")]
    [Tooltip("Portakalın yerinde patlayacak efekt (toplama parıltısı). Opsiyonel.")]
    [SerializeField] private GameObject pickupVfxPrefab;
    [SerializeField] private float vfxLifetime = 2f;
    [Tooltip("Oyuncunun üzerinde oynayacak iyileşme efekti. Opsiyonel.")]
    [SerializeField] private GameObject healVfxPrefab;
    [Tooltip("İyileşme efektinin oyuncunun ayak hizasından yüksekliği.")]
    [SerializeField] private float healVfxHeight = 1f;
    [Tooltip("Açıksa efekt oyuncuya bağlanır ve onunla hareket eder; kapalıysa yerinde kalır.")]
    [SerializeField] private bool attachHealVfxToPlayer = true;
    [SerializeField] private float healVfxLifetime = 2f;

    [Header("Ömür")]
    [Tooltip("Bu süre sonunda kendiliğinden yok olur. 0 = süresiz bekler.")]
    [SerializeField] private float lifetime = 0f;

    [Header("Ses")]
    [Tooltip("Toplanma sesi. Obje yok olduğu için kendi AudioSource'u yerine konumda çalınır.")]
    [SerializeField] private AudioClip pickupClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar")]
    [Tooltip("Toplandığı anda tetiklenir (UI, ses, sayaç).")]
    public UnityEvent onPickedUp;

    private Vector3 _startPosition;
    private GameObject _idleVfx;
    private bool _consumed;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"{name}: HealthPickup collider'ı 'Is Trigger' olmalı.", this);

        _startPosition = transform.position;

        SpawnIdleVfx();

        if (lifetime > 0f) Destroy(gameObject, lifetime);
    }

    /// <summary>Bekleme efektini portakalın child'ı olarak doğurur (süzülmeyi takip etsin).</summary>
    private void SpawnIdleVfx()
    {
        if (idleVfxPrefab == null) return;

        _idleVfx = Instantiate(idleVfxPrefab, transform.position + idleVfxOffset,
                               idleVfxPrefab.transform.rotation, transform);
    }

    private void Update()
    {
        if (spinSpeed != 0f)
            transform.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);

        // Süzülme: düştüğü yerin etrafında yumuşak salınım.
        if (bobHeight > 0f)
        {
            float offset = Mathf.Sin(Time.time * bobSpeed) * bobHeight;
            transform.position = _startPosition + Vector3.up * offset;
        }

        // Portakal dönerken child efekt de dönüyor; istenirse dünya rotasyonunda sabitlenir.
        if (_idleVfx != null && keepIdleVfxUpright)
            _idleVfx.transform.rotation = idleVfxPrefab.transform.rotation;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_consumed) return;

        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead) return;

        // Can doluysa portakalı harcama; oyuncu lazım olunca dönsün.
        if (!consumeWhenFull && health.Current >= health.MaxHealth) return;

        _consumed = true;
        health.Heal(healAmount);

        ReleaseIdleVfx();
        SpawnPickupVfx();
        SpawnHealVfx(health.transform);

        if (pickupClip != null)
            AudioSource.PlayClipAtPoint(pickupClip, transform.position, sfxVolume);

        onPickedUp?.Invoke();
        Destroy(gameObject);
    }

    /// <summary>
    /// Bekleme efektini portakaldan ayırır: portakal yok olunca child efekt de anında
    /// silinip parçacıkları kesilmesin, havada sönerek bitsin.
    /// </summary>
    private void ReleaseIdleVfx()
    {
        if (_idleVfx == null) return;

        _idleVfx.transform.SetParent(null, true);

        // Yeni parçacık üretmeyi durdur; mevcutlar ömrünü tamamlasın.
        foreach (var ps in _idleVfx.GetComponentsInChildren<ParticleSystem>(true))
            ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        Destroy(_idleVfx, Mathf.Max(0.1f, vfxLifetime));
    }

    private void SpawnPickupVfx()
    {
        if (pickupVfxPrefab == null) return;

        var vfx = Instantiate(pickupVfxPrefab, transform.position, pickupVfxPrefab.transform.rotation);
        if (vfxLifetime > 0f) Destroy(vfx, vfxLifetime);
    }

    /// <summary>İyileşme efektini oyuncunun üzerinde oynatır (istenirse ona bağlı).</summary>
    private void SpawnHealVfx(Transform player)
    {
        if (healVfxPrefab == null || player == null) return;

        Vector3 position = player.position + Vector3.up * healVfxHeight;
        var vfx = Instantiate(healVfxPrefab, position, healVfxPrefab.transform.rotation);

        if (attachHealVfxToPlayer) vfx.transform.SetParent(player, true);
        if (healVfxLifetime > 0f) Destroy(vfx, healVfxLifetime);
    }

    /// <summary>Düştüğü yeri günceller (süzülme merkezi). Loot düşerken çağrılır.</summary>
    public void ResetBobOrigin()
    {
        _startPosition = transform.position;
    }
}
