using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Oyuncunun canının tek otoritesi. Tüm hasar buradan geçer:
///   - TakeDamage(amount): normal hasar (düşman, düşen obje). Dodge i-frame'inde yok sayılır.
///   - Kill(): lava gibi ani ölüm; i-frame'i yok sayar.
/// Can 0'a inince Die() çalışır: kontrol kesilir, Die animasyonu oynar ve
/// deathReloadDelay sonunda sahne yeniden yüklenir.
/// UI/ses için HealthChanged ve Died event'leri sunulur.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Can")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Hasar Zamanlaması")]
    [Tooltip("Hasar aldıktan sonra kısa dokunulmazlık; tek çarpışmanın çoklu sayılmasını önler.")]
    [SerializeField] private float damageInvulnDuration = 0.3f;
    [Tooltip("Ölümden sahne yeniden yüklenene kadar geçen süre. Ölüm animasyonunun uzunluğuna göre ayarla.")]
    [SerializeField] private float deathReloadDelay = 3f;

    [Header("Ölüm")]
    [Tooltip("Ölünce tetiklenecek Animator trigger'ı. Controller'da yoksa sessizce atlanır.")]
    [SerializeField] private string deathTrigger = "Die";
    [Tooltip("Ölünce ceset kaymasın diye hız sıfırlanır ve rotasyon kilitlenir.")]
    [SerializeField] private bool freezeOnDeath = true;

    // --- Okuma erişimi ---
    public float MaxHealth => maxHealth;
    public float Current { get; private set; }
    public bool IsDead { get; private set; }

    // --- Event'ler (UI/ses buraya bağlanabilir) ---
    public event Action<float, float> HealthChanged; // (current, max)
    public event Action Died;

    private PlayerController _controller;
    private PlayerAttack _attack;
    private Rigidbody _rb;
    private float _lastDamageTime = Mathf.NegativeInfinity;

    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
        _attack = GetComponent<PlayerAttack>();
        _rb = GetComponent<Rigidbody>();
        Current = maxHealth;
    }

    private void Start()
    {
        HealthChanged?.Invoke(Current, maxHealth);
    }

    /// <summary>Normal hasar. Dodge i-frame'inde ve kısa post-hit penceresinde yok sayılır.</summary>
    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f) return;
        if (_controller != null && _controller.IsInvincible) return;      // dodge i-frame
        if (Time.time < _lastDamageTime + damageInvulnDuration) return;   // çoklu çarpışma debounce
        _lastDamageTime = Time.time;

        Current = Mathf.Max(0f, Current - amount);
        HealthChanged?.Invoke(Current, maxHealth);

        if (Current <= 0f)
            Die();
        else if (_controller != null)
            _controller.TakeHit();   // hit animasyonu
    }

    /// <summary>
    /// IDamageable girişi: oyuncu da düşmanlarla aynı hasar sözleşmesini kullanır.
    /// Knockback'i oyuncuda düşman AI'ları kendi uyguladığı için burada yalnızca hasar işlenir.
    /// </summary>
    public void TakeDamage(DamageInfo info) => TakeDamage(info.Amount);

    /// <summary>Ani ölüm (lava vb.); dokunulmazlığı yok sayar.</summary>
    public void Kill()
    {
        if (IsDead) return;
        Current = 0f;
        HealthChanged?.Invoke(Current, maxHealth);
        Die();
    }

    public void Heal(float amount)
    {
        if (IsDead || amount <= 0f) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        HealthChanged?.Invoke(Current, maxHealth);
    }

    private void Die()
    {
        if (IsDead) return;
        IsDead = true;

        // Ölüm animasyonunu başlat. Any State üzerinden geldiği için o an ne oynuyorsa keser.
        if (_controller != null) _controller.SetAnimTrigger(deathTrigger);

        // Kontrolü kes: giriş işlenmez ama fizik ve animasyon sürer.
        if (_controller != null)
        {
            _controller.EndAirAttack();   // uçan tekme ortasında ölürsek hasar penceresi kapansın
            _controller.enabled = false;
        }

        // Saldırı tarafı da sussun; tekme taraması her karede çalışıyordu.
        if (_attack != null) _attack.enabled = false;

        if (freezeOnDeath) FreezeBody();

        Died?.Invoke();
        Invoke(nameof(ReloadScene), deathReloadDelay);
    }

    /// <summary>Cesedi durdurur: yatay kayma ve devrilme olmasın, yerçekimi kalsın.</summary>
    private void FreezeBody()
    {
        if (_rb == null) return;

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void ReloadScene()
    {
        Scene s = SceneManager.GetActiveScene();
        SceneManager.LoadScene(s.buildIndex);
    }
}
