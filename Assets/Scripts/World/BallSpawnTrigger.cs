using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Top yağmurunu başlatan tetik alanı. Oyuncu bu objenin trigger collider'ına
/// girince bağlı RollingBallSpawner'lar çalışmaya başlar.
///
/// Tetikleyici ile spawner ayrı objelerdir: tetik yokuşun başında (oyuncunun
/// geçeceği yerde), spawner yokuşun tepesinde gökyüzünde durur.
///
/// Kurulum: yolun üstüne boş bir GameObject koy, BoxCollider ekle, "Is Trigger"
/// işaretle, kutuyu oyuncunun geçeceği genişliğe yay ve spawners listesine
/// gökyüzündeki spawner'ı sürükle. (Tools/World/Create Rolling Ball Trap ikisini
/// birden kurup birbirine bağlar.)
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class BallSpawnTrigger : MonoBehaviour
{
    [Header("Tetikleyici")]
    [Tooltip("Açıksa tuzak yalnızca bir kez kurulur (oyuncu geri gelince tekrar başlamaz).")]
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("Oyuncu girdikten sonra ilk topun düşmesi için beklenecek süre.")]
    [SerializeField] private float delay = 0f;

    [Header("Spawner'lar")]
    [Tooltip("Tetiklenince çalışacak spawner'lar. Birden çok yokuşu aynı anda başlatabilirsin.")]
    [SerializeField] private RollingBallSpawner[] spawners;

    [Header("Çıkış")]
    [Tooltip("Açıksa oyuncu alandan çıkınca doğurma durur (yokuşu geçince top yağmasın).")]
    [SerializeField] private bool stopOnExit = false;
    [Tooltip("stopOnExit açıkken sahnedeki mevcut toplar da temizlensin mi?")]
    [SerializeField] private bool clearBallsOnExit = false;

    [Header("Ses")]
    [Tooltip("Boşsa Awake'te otomatik eklenir ve 3D yapılır.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Tuzak tetiklenince çalar (gürleme, uyarı). Opsiyonel.")]
    [SerializeField] private AudioClip triggerClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar")]
    [Tooltip("Tuzak tetiklendiği anda çalışır (müzik, kamera sarsıntısı, UI).")]
    public UnityEvent onTriggered;

    private bool _hasTriggered;

    /// <summary>Tuzak daha önce tetiklendi mi? (Başka sistemler okuyabilir.)</summary>
    public bool HasTriggered => _hasTriggered;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"{name}: BallSpawnTrigger collider'i 'Is Trigger' olmali.", this);

        if (spawners == null || spawners.Length == 0)
            Debug.LogWarning($"{name}: spawners listesi bos; tetiklenince hicbir sey olmaz.", this);

        if (audioSource == null && triggerClip != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (triggerOnce && _hasTriggered) return;

        // Oyuncuyu DamageSource/BossRoomTurn ile ayni sekilde tani (child collider'lar da calissin).
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead) return;

        _hasTriggered = true;

        if (triggerClip != null && audioSource != null)
            audioSource.PlayOneShot(triggerClip, sfxVolume);

        onTriggered?.Invoke();

        if (delay > 0f) StartCoroutine(BeginAfterDelay());
        else BeginAll();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!stopOnExit) return;

        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null) return;

        StopAll();
    }

    private IEnumerator BeginAfterDelay()
    {
        yield return new WaitForSeconds(delay);
        BeginAll();
    }

    /// <summary>Bagli tum spawner'lari calistirir. UnityEvent'ten de cagrilabilir.</summary>
    public void BeginAll()
    {
        if (spawners == null) return;

        foreach (RollingBallSpawner spawner in spawners)
        {
            if (spawner != null) spawner.BeginSpawning();
        }
    }

    /// <summary>Bagli tum spawner'lari durdurur. UnityEvent'ten de cagrilabilir.</summary>
    public void StopAll()
    {
        if (spawners == null) return;

        foreach (RollingBallSpawner spawner in spawners)
        {
            if (spawner == null) continue;

            if (clearBallsOnExit) spawner.StopAndClear();
            else spawner.StopSpawning();
        }
    }

    private void OnDrawGizmos()
    {
        // Tetik alanini ve spawner'lara giden baglari sahnede goster.
        var col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0f, 0.25f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }

        if (spawners == null) return;

        Gizmos.color = new Color(1f, 0.85f, 0f);
        foreach (RollingBallSpawner spawner in spawners)
        {
            if (spawner != null) Gizmos.DrawLine(transform.position, spawner.transform.position);
        }
    }
}
