using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Oyuncunun hasar veren tarafı. PlayerController'ın Animation Event köprüsünü dinler:
///   - AttackStepStarted -> yeni combo adımı: vurulanlar listesi sıfırlanır
///   - AttackHit         -> vuruş frame'i: oyuncunun önünde küre taraması yapılır
///
/// Taramada bulunan her IDamageable (Health / PlayerHealth) bir adımda YALNIZCA BİR KEZ
/// hasar alır; birden çok collider'ı olan düşmanlar çoklu hasar yemez.
///
/// Combo adımları ayrı ayrı ayarlanır (comboSteps): son vuruş daha sert ve daha geniş
/// olsun istenirse yalnızca o adımın değerleri büyütülür.
///
/// Animation Event kurulmamışsa sistem sessizce ölmez: autoHitWithoutEvent açıkken
/// adım başlangıcından fallbackHitDelay saniye sonra vuruş kendiliğinden uygulanır.
/// </summary>
[RequireComponent(typeof(PlayerController))]
[DisallowMultipleComponent]
public class PlayerAttack : MonoBehaviour
{
    /// <summary>Tek bir combo adımının vuruş ayarları.</summary>
    [Serializable]
    public class ComboStep
    {
        [Tooltip("Bu adımın hasarı.")]
        public float damage = 20f;
        [Tooltip("Vuruş küresinin yarıçapı (silahın erişimi).")]
        public float radius = 1.1f;
        [Tooltip("Kürenin oyuncunun ne kadar önünde doğacağı.")]
        public float forwardOffset = 1.1f;
        [Tooltip("Hedefe uygulanacak itme kuvveti.")]
        public float knockbackForce = 5f;
        [Tooltip("Hedefi sersemletme süresi (saniye). 0 = yok. Sadece IStunnable olan hedefler (boss) etkilenir.")]
        public float stunDuration = 0f;
        [Tooltip("Vuruş konisinin toplam açısı (derece). 360 = her yön.")]
        [Range(0f, 360f)] public float angle = 160f;
        [Tooltip("Bu vuruş isabet edince kısa zaman donması (hit-stop) uygulansın mı?")]
        public bool hitStop = true;
    }

    [Header("Vuruş Geometrisi")]
    [Tooltip("Vuruş küresinin merkezi (ör. silahın ucundaki boş obje). Boşsa oyuncunun önü hesaplanır.")]
    [SerializeField] private Transform hitOrigin;
    [Tooltip("hitOrigin boşken kürenin ayak hizasından yüksekliği.")]
    [SerializeField] private float hitHeight = 1f;
    [Tooltip("Hangi layer'lar taransın. Varsayılan: her şey (IDamageable olmayanlar zaten elenir).")]
    [SerializeField] private LayerMask targetLayers = ~0;
    [Tooltip("Tek vuruşta değerlendirilecek maksimum collider sayısı.")]
    [SerializeField] private int maxCollidersPerHit = 16;

    [Header("Combo Adımları")]
    [Tooltip("Sıra = combo adımı (PlayerController.attackTriggers ile aynı sırada). Dizi kısa kalırsa son adım tekrar kullanılır.")]
    [SerializeField]
    private ComboStep[] comboSteps =
    {
        new ComboStep { damage = 20f, radius = 1.1f, forwardOffset = 1.1f, knockbackForce = 4f },
        new ComboStep { damage = 25f, radius = 1.1f, forwardOffset = 1.2f, knockbackForce = 5f },
        new ComboStep { damage = 40f, radius = 1.4f, forwardOffset = 1.3f, knockbackForce = 9f },
    };

    [Header("Uçan Tekme (havada saldırı)")]
    [Tooltip("Kick1 vuruşunun ayarları. Yerdeki combo'dan bağımsızdır; hasar penceresi uçuş boyu açıktır. Ağır knockback + boss'a sersemletme uygular.")]
    [SerializeField]
    private ComboStep airKick = new ComboStep
    {
        damage = 35f,
        radius = 1.3f,
        forwardOffset = 1.2f,
        knockbackForce = 60f,   // yerdeki combo'nun ~5 katı: tekme düşmanı savursun
        stunDuration = 2f,      // boss 2 saniye sersemler (diğer düşmanlar etkilenmez)
        angle = 200f,
        hitStop = true,
    };

    [Header("Animation Event Yedeği")]
    [Tooltip("Animasyonlara AttackHit event'i eklenmemişse vuruşu zamanla uygula.")]
    [SerializeField] private bool autoHitWithoutEvent = true;
    [Tooltip("Adım başlangıcından kaç saniye sonra yedek vuruş uygulanacak.")]
    [SerializeField] private float fallbackHitDelay = 0.2f;

    [Header("Hit-Stop (vuruş hissi)")]
    [Tooltip("İsabet anında zamanın yavaşlayacağı süre (gerçek saniye). 0 = kapalı.")]
    [SerializeField] private float hitStopDuration = 0.05f;
    [Tooltip("Hit-stop sırasındaki zaman ölçeği.")]
    [Range(0.01f, 1f)]
    [SerializeField] private float hitStopTimeScale = 0.15f;

    [Header("Efekt")]
    [Tooltip("İsabet noktasında doğacak efekt (kıvılcım/darbe). Opsiyonel.")]
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private float hitVfxLifetime = 2f;
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Her vuruşta çalar (ıskalasa da): kılıç savurma sesi.")]
    [SerializeField] private AudioClip swingClip;
    [Tooltip("İsabet ettiğinde çalar.")]
    [SerializeField] private AudioClip hitClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Debug")]
    [Tooltip("Vuruş kürelerini Scene view'da göster.")]
    [SerializeField] private bool drawGizmos = true;
    [Tooltip("Konsola isabet loglarını yaz.")]
    [SerializeField] private bool logHits = false;

    /// <summary>İsabet eden her hedef için tetiklenir (hit marker UI, kombo sayacı vb.).</summary>
    public event Action<IDamageable, DamageInfo> DamageDealt;

    private PlayerController _controller;
    private Collider[] _buffer;

    // Bir combo adımı içinde zaten vurulan hedefler (çoklu collider'a karşı).
    private readonly HashSet<IDamageable> _hitThisStep = new HashSet<IDamageable>();

    private int _currentStep = 1;
    private bool _hitAppliedThisStep;   // Bu adımda vuruş uygulandı mı? (event veya yedek)
    private bool _fallbackArmed;        // Yedek zamanlayıcı bekliyor mu?
    private float _fallbackTime;
    private Coroutine _hitStopRoutine;

    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
        _buffer = new Collider[Mathf.Max(1, maxCollidersPerHit)];
    }

    private void OnEnable()
    {
        _controller.AttackStepStarted += OnAttackStepStarted;
        _controller.AttackHit += OnAttackHit;
    }

    private void OnDisable()
    {
        _controller.AttackStepStarted -= OnAttackStepStarted;
        _controller.AttackHit -= OnAttackHit;
        _fallbackArmed = false;
    }

    private void Update()
    {
        // Uçan tekme: hasar penceresi uçuş boyunca açık kalır. Her hedefe adım başına
        // yalnızca bir kez vurulduğu için her kare taramak güvenlidir; düşmana hangi
        // anda ulaşırsan o an isabet eder.
        if (_controller.IsAirAttack)
        {
            _fallbackArmed = false;
            PerformHit();
            return;
        }

        // Animation Event gelmediyse yedek vuruşu uygula (kurulum hatasına karşı emniyet).
        if (!_fallbackArmed || _hitAppliedThisStep) return;
        if (Time.time < _fallbackTime) return;

        _fallbackArmed = false;
        PerformHit();
    }

    /// <summary>Yeni combo adımı başladı: hedef listesini ve vuruş bayraklarını sıfırla.</summary>
    private void OnAttackStepStarted(int step)
    {
        _currentStep = step;
        _hitThisStep.Clear();
        _hitAppliedThisStep = false;
        // Uçan tekmede (step 0) yedek zamanlayıcıya gerek yok; tarama zaten süreklidir.
        _fallbackArmed = autoHitWithoutEvent && step > 0;
        _fallbackTime = Time.time + fallbackHitDelay;

        PlaySfx(swingClip);
    }

    /// <summary>Animation Event: vuruş (hasar) frame'i geldi.</summary>
    private void OnAttackHit()
    {
        _fallbackArmed = false;
        PerformHit();
    }

    /// <summary>
    /// Oyuncunun önünde küre taraması yapar; koni içindeki her IDamageable'a
    /// adımın hasarını ve knockback'ini uygular. Dışarıdan da çağrılabilir
    /// (ör. silahın kendi Animation Event'i).
    /// </summary>
    public void PerformHit()
    {
        _hitAppliedThisStep = true;

        ComboStep data = GetStep(_currentStep);
        if (data == null) return;

        Vector3 origin = GetHitOrigin(data);
        int count = Physics.OverlapSphereNonAlloc(
            origin, data.radius, _buffer, targetLayers, QueryTriggerInteraction.Collide);

        bool anyHit = false;

        for (int i = 0; i < count; i++)
        {
            Collider col = _buffer[i];
            if (col == null) continue;

            var target = col.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead) continue;

            // Kendimize (ve aynı hiyerarşideki her şeye) vurmayalım.
            var targetComponent = target as Component;
            if (targetComponent == null || targetComponent.transform.root == transform.root) continue;

            // Aynı adımda aynı hedefe ikinci kez vurma.
            if (!_hitThisStep.Add(target)) continue;

            // Arkamızdaki hedefleri ele: yatay koni kontrolü.
            Vector3 toTarget = targetComponent.transform.position - transform.position;
            toTarget.y = 0f;
            if (data.angle < 360f && toTarget.sqrMagnitude > 0.0001f &&
                Vector3.Angle(transform.forward, toTarget.normalized) > data.angle * 0.5f)
                continue;

            Vector3 hitPoint = col.ClosestPoint(origin);
            Vector3 hitDir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;

            var info = new DamageInfo(data.damage, hitPoint, hitDir, data.knockbackForce, gameObject,
                                      data.stunDuration);
            target.TakeDamage(info);

            SpawnHitVfx(hitPoint);
            DamageDealt?.Invoke(target, info);
            anyHit = true;

            if (logHits)
                Debug.Log("[PlayerAttack] Adim " + _currentStep + " -> " + targetComponent.name +
                          " (" + data.damage + " hasar)", targetComponent);
        }

        if (!anyHit) return;

        PlaySfx(hitClip);
        if (data.hitStop) TriggerHitStop();
    }

    /// <summary>Adım ayarını verir: 0 = uçan tekme, 1+ = yerdeki combo (dizi kısaysa son adım).</summary>
    private ComboStep GetStep(int step)
    {
        if (step <= 0) return airKick;
        if (comboSteps == null || comboSteps.Length == 0) return null;
        int index = Mathf.Clamp(step - 1, 0, comboSteps.Length - 1);
        return comboSteps[index];
    }

    private Vector3 GetHitOrigin(ComboStep data)
    {
        if (hitOrigin != null) return hitOrigin.position;
        return transform.position + Vector3.up * hitHeight + transform.forward * data.forwardOffset;
    }

    /// <summary>Kısa zaman donması; darbenin "ağırlığını" hissettirir.</summary>
    private void TriggerHitStop()
    {
        if (hitStopDuration <= 0f) return;
        if (_hitStopRoutine != null) StopCoroutine(_hitStopRoutine);
        _hitStopRoutine = StartCoroutine(HitStopRoutine());
    }

    private IEnumerator HitStopRoutine()
    {
        Time.timeScale = hitStopTimeScale;
        yield return new WaitForSecondsRealtime(hitStopDuration);
        Time.timeScale = 1f;
        _hitStopRoutine = null;
    }

    private void OnDestroy()
    {
        // Hit-stop sırasında sahne değişirse oyun ağır çekimde kalmasın.
        if (_hitStopRoutine != null) Time.timeScale = 1f;
    }

    private void SpawnHitVfx(Vector3 point)
    {
        if (hitVfxPrefab == null) return;
        var vfx = Instantiate(hitVfxPrefab, point, Quaternion.identity);
        if (hitVfxLifetime > 0f) Destroy(vfx, hitVfxLifetime);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null) return;
        if (audioSource != null) audioSource.PlayOneShot(clip, sfxVolume);
        else AudioSource.PlayClipAtPoint(clip, transform.position, sfxVolume);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        // Uçan tekmenin erişimi (mavi).
        if (airKick != null)
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireSphere(GetHitOrigin(airKick), airKick.radius);
        }

        if (comboSteps == null) return;

        // Her combo adımının erişimini farklı tonda göster (tuning için).
        for (int i = 0; i < comboSteps.Length; i++)
        {
            ComboStep s = comboSteps[i];
            if (s == null) continue;

            float t = comboSteps.Length > 1 ? i / (float)(comboSteps.Length - 1) : 0f;
            Gizmos.color = new Color(1f, 0.4f + 0.6f * (1f - t), 0f, 0.5f);

            Vector3 origin = hitOrigin != null
                ? hitOrigin.position
                : transform.position + Vector3.up * hitHeight + transform.forward * s.forwardOffset;
            Gizmos.DrawWireSphere(origin, s.radius);
        }
    }
}
