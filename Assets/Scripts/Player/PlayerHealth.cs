using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Oyuncunun canının tek otoritesi. Tüm hasar buradan geçer:
///   - TakeDamage(amount): normal hasar (düşman, düşen obje). Dodge i-frame'inde yok sayılır.
///   - Kill(): lava gibi ani ölüm; i-frame'i yok sayar.
/// Can 0'a inince Die() çalışır, kısa gecikmeyle sahne 0'dan yeniden yüklenir.
/// UI/ses için HealthChanged ve Died event'leri sunulur.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerHealth : MonoBehaviour
{
    [Header("Can")]
    [SerializeField] private float maxHealth = 100f;

    [Header("Hasar Zamanlaması")]
    [Tooltip("Hasar aldıktan sonra kısa dokunulmazlık; tek çarpışmanın çoklu sayılmasını önler.")]
    [SerializeField] private float damageInvulnDuration = 0.3f;
    [Tooltip("Ölümden sahne yeniden yüklenene kadar geçen süre.")]
    [SerializeField] private float deathReloadDelay = 1.2f;

    // --- Okuma erişimi ---
    public float MaxHealth => maxHealth;
    public float Current { get; private set; }
    public bool IsDead { get; private set; }

    // --- Event'ler (UI/ses buraya bağlanabilir) ---
    public event Action<float, float> HealthChanged; // (current, max)
    public event Action Died;

    private PlayerController _controller;
    private float _lastDamageTime = Mathf.NegativeInfinity;

    private void Awake()
    {
        _controller = GetComponent<PlayerController>();
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

        // Kontrolü kes (fizik/animasyon sürer, girişe tepki vermez).
        if (_controller != null)
        {
            _controller.enabled = false;
            _controller.TakeHit();   // basit ölüm tepkisi (hit animasyonu)
        }

        Died?.Invoke();
        Invoke(nameof(ReloadScene), deathReloadDelay);
    }

    private void ReloadScene()
    {
        Scene s = SceneManager.GetActiveScene();
        SceneManager.LoadScene(s.buildIndex);
    }
}
