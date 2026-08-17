using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Oyuncu dışındaki her şeyin (düşman, boss, kırılabilir obje) can otoritesi.
/// Tüm hasar TakeDamage(DamageInfo) üzerinden geçer; can 0'a inince Die() çalışır.
///
/// Ölümde sırasıyla: AI davranışları kapanır -> Die animasyonu oynar ->
/// collider'lar kapanır -> destroyDelay sonunda obje yok edilir (0 ise kalır).
///
/// Animator sözleşmesi (opsiyonel): controller'da varsa "Hit" ve "Die" trigger'ları
/// tetiklenir; parametre yoksa sessizce atlanır, hata vermez.
/// </summary>
[DisallowMultipleComponent]
public class Health : MonoBehaviour, IDamageable
{
    [Header("Can")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Hasar")]
    [Tooltip("Aynı vuruşun birden çok collider yüzünden çoklu saymasını önleyen kısa dokunulmazlık.")]
    [SerializeField] private float hitInvulnDuration = 0.1f;
    [Tooltip("Gelen knockback bu çarpanla uygulanır (0 = bu düşman hiç itilmez).")]
    [SerializeField] private float knockbackMultiplier = 1f;
    [Tooltip("Rigidbody yoksa (transform ile hareket eden düşman) kuvvet başına kaç metre geri kayacağı.")]
    [SerializeField] private float transformKnockbackPerForce = 0.06f;
    [Tooltip("Transform knockback'inin süresi (saniye).")]
    [SerializeField] private float transformKnockbackDuration = 0.12f;

    [Header("Animasyon")]
    [Tooltip("Boşsa bu objede ve child'larında aranır.")]
    [SerializeField] private Animator animator;
    [Tooltip("Hasar alınca tetiklenecek trigger. Boş bırakılırsa tetiklenmez.")]
    [SerializeField] private string hitTrigger = "Hit";
    [Tooltip("Ölünce tetiklenecek trigger. Boş bırakılırsa tetiklenmez.")]
    [SerializeField] private string dieTrigger = "Die";

    [Header("Ölüm")]
    [Tooltip("Ölünce kapatılacak davranışlar. Boşsa EnemyFollow/BossFight otomatik durdurulur.")]
    [SerializeField] private Behaviour[] disableOnDeath;
    [Tooltip("Ölünce collider'lar kapansın mı? (ceset oyuncuya engel olmasın).")]
    [SerializeField] private bool disableCollidersOnDeath = true;
    [Tooltip("Ölüm animasyonu için beklenip obje yok edilir. 0 = sahnede kalsın.")]
    [SerializeField] private float destroyDelay = 4f;

    [Header("Efekt")]
    [Tooltip("Vuruş noktasında doğacak efekt (kan, kıvılcım). Opsiyonel.")]
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private float hitVfxLifetime = 2f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hitClip;
    [SerializeField] private AudioClip deathClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar")]
    [Tooltip("Öldüğü anda tetiklenir (kapı açma, loot, boss müziği kapatma vb.).")]
    public UnityEvent onDeath;

    // --- Okuma erişimi (UI / AI için) ---
    public float MaxHealth => maxHealth;
    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public float Normalized => maxHealth > 0f ? Current / maxHealth : 0f;

    /// <summary>(current, max) — can barı gibi sistemler buna abone olur.</summary>
    public event Action<float, float> HealthChanged;
    public event Action Died;

    private Rigidbody _rb;
    private float _lastHitTime = Mathf.NegativeInfinity;
    private Coroutine _knockbackRoutine;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        _rb = GetComponent<Rigidbody>();
        Current = maxHealth;
    }

    private void Start()
    {
        HealthChanged?.Invoke(Current, maxHealth);
    }

    /// <summary>Ana hasar girişi. Ölüyse, hasar 0 ise veya i-frame içindeyse yok sayılır.</summary>
    public void TakeDamage(DamageInfo info)
    {
        if (IsDead || info.Amount <= 0f) return;
        if (Time.time < _lastHitTime + hitInvulnDuration) return;
        _lastHitTime = Time.time;

        Current = Mathf.Max(0f, Current - info.Amount);
        HealthChanged?.Invoke(Current, maxHealth);

        SpawnHitVfx(info.HitPoint);
        PlaySfx(hitClip);

        if (Current <= 0f)
        {
            Die(info);
            return;
        }

        ApplyKnockback(info);
        SetTrigger(hitTrigger);
    }

    /// <summary>Kısayol: yön/knockback olmadan sade hasar (tuzak, zehir vb.).</summary>
    public void TakeDamage(float amount)
    {
        TakeDamage(new DamageInfo(amount, transform.position, Vector3.zero, 0f, null));
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        HealthChanged?.Invoke(Current, maxHealth);
    }

    /// <summary>Ani ölüm (uçurum, cutscene).</summary>
    public void Kill()
    {
        if (IsDead) return;
        Current = 0f;
        HealthChanged?.Invoke(Current, maxHealth);
        Die(new DamageInfo(0f, transform.position, Vector3.zero, 0f, null));
    }

    private void Die(DamageInfo info)
    {
        if (IsDead) return;
        IsDead = true;

        DisableBehaviours();
        SetTrigger(dieTrigger);
        PlaySfx(deathClip);

        // Ölüm anındaki son itme; ceset vuruş yönünde savrulsun.
        ApplyKnockback(info);

        if (disableCollidersOnDeath)
        {
            foreach (var col in GetComponentsInChildren<Collider>())
                col.enabled = false;
        }

        Died?.Invoke();
        onDeath?.Invoke();

        if (destroyDelay > 0f) Destroy(gameObject, destroyDelay);
    }

    /// <summary>Ölümde AI'yı susturur: liste doluysa onu, boşsa bilinen davranışları kapatır.</summary>
    private void DisableBehaviours()
    {
        if (disableOnDeath != null && disableOnDeath.Length > 0)
        {
            foreach (var b in disableOnDeath)
                if (b != null) b.enabled = false;
            return;
        }

        // Liste boşsa varsayılan davranış: bu objedeki AI'ları kendimiz durduralım.
        var boss = GetComponent<BossFight>();
        if (boss != null)
        {
            boss.StopFight();
            boss.enabled = false;
        }

        var follow = GetComponent<EnemyFollow>();
        if (follow != null) follow.enabled = false;
    }

    private void ApplyKnockback(DamageInfo info)
    {
        if (info.KnockbackForce <= 0f || knockbackMultiplier <= 0f) return;
        if (info.HitDirection.sqrMagnitude < 0.0001f) return;

        Vector3 dir = info.HitDirection.normalized;
        float force = info.KnockbackForce * knockbackMultiplier;

        // Fiziksel düşman: gerçek impuls.
        if (_rb != null && !_rb.isKinematic)
        {
            _rb.AddForce(dir * force, ForceMode.Impulse);
            return;
        }

        // Transform ile hareket eden düşman: kısa, yumuşak geri kayma.
        if (transformKnockbackPerForce <= 0f || transformKnockbackDuration <= 0f) return;
        if (_knockbackRoutine != null) StopCoroutine(_knockbackRoutine);
        _knockbackRoutine = StartCoroutine(TransformKnockback(dir, force * transformKnockbackPerForce));
    }

    /// <summary>Rigidbody'siz düşmanı verilen yönde kısa süre geriye kaydırır.</summary>
    private IEnumerator TransformKnockback(Vector3 dir, float distance)
    {
        float elapsed = 0f;
        while (elapsed < transformKnockbackDuration)
        {
            float step = distance * (Time.deltaTime / transformKnockbackDuration);
            transform.position += dir * step;
            elapsed += Time.deltaTime;
            yield return null;
        }

        _knockbackRoutine = null;
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

    /// <summary>Trigger'ı yalnızca controller'da gerçekten varsa tetikler (uyarı üretmemek için).</summary>
    private void SetTrigger(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName)) return;

        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName)
            {
                animator.SetTrigger(triggerName);
                return;
            }
        }
    }
}
