using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Oda girişini kapatan kilit. Belirtilen adım (requiredStage) tamamlanana kadar
/// katı collider'lar açık kalır ve oyuncu geçemez; adım bitince collider'lar kapanır,
/// oda açılır.
///
/// Collider kullanımı:
///   - KATI (Is Trigger kapalı) collider'lar  -> engelin kendisi. blockers listesi
///     boşsa bu objedeki ve child'larındaki katı collider'lar otomatik kullanılır.
///   - TRIGGER (Is Trigger açık) collider     -> "burası kilitli" uyarısı. Kilitliyken
///     oyuncu bu alana girince onBlocked tetiklenir (UI ipucu, kilit sesi).
///
/// Kurulum: kapının/geçidin önüne boş bir GameObject koy, kapıyı kapatacak boyda bir
/// BoxCollider ekle (Is Trigger KAPALI), istersen bir tane daha ekleyip Is Trigger'ı
/// aç (uyarı alanı), requiredStage'i seç. Görsel bir engel (parmaklık, taş, enerji
/// duvarı) varsa onu showWhileLocked'a sürükle.
///
/// Tools/Progression/Setup Game Flow üç kapıyı hazır kurar; sen yalnızca yerlerini
/// ve boyutlarını sahnede ayarlarsın.
/// </summary>
[DisallowMultipleComponent]
public class StageGate : MonoBehaviour
{
    [Header("Kilit")]
    [Tooltip("Bu adım TAMAMLANINCA kapı açılır. Ör: okyanus odasının kapısı -> HirtRoom.")]
    [SerializeField] private GameStage requiredStage = GameStage.HirtRoom;
    [Tooltip("Engeli oluşturan katı collider'lar. Boşsa bu objedeki ve child'larındaki " +
             "trigger OLMAYAN collider'lar kullanılır.")]
    [SerializeField] private Collider[] blockers;
    [Tooltip("Kapı açılmadan önce beklenecek süre (ölüm animasyonu bitsin, kapı açılma sesi otursun).")]
    [SerializeField] private float unlockDelay = 0.5f;

    [Header("Görsel")]
    [Tooltip("Kilitliyken açık, kapı açılınca kapanacak objeler (parmaklık, taş yığını, efekt).")]
    [SerializeField] private GameObject[] showWhileLocked;
    [Tooltip("Kapı açılınca açılacak objeler (açık kapı modeli, ışık, yön oku).")]
    [SerializeField] private GameObject[] showWhenUnlocked;

    [Header("Uyarı")]
    [Tooltip("Kilitliyken oyuncu trigger alanına girince uyarı tetiklensin mi?")]
    [SerializeField] private bool warnOnBlocked = true;
    [Tooltip("İki uyarı arasındaki en kısa süre; oyuncu kapıya yaslanınca uyarı spam'lemesin.")]
    [SerializeField] private float warnCooldown = 2f;

    [Header("Ses")]
    [Tooltip("Boşsa gerekince otomatik eklenir ve 3D yapılır.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Kilitliyken kapıya gelince çalar (kilit sesi). Opsiyonel.")]
    [SerializeField] private AudioClip lockedClip;
    [Tooltip("Kapı açılırken çalar. Opsiyonel.")]
    [SerializeField] private AudioClip unlockClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar")]
    [Tooltip("Kapı kilitlendiğinde/kilitli başladığında tetiklenir.")]
    public UnityEvent onLocked;
    [Tooltip("Kapı açıldığında tetiklenir (animasyon, kamera, UI).")]
    public UnityEvent onUnlocked;
    [Tooltip("Kilitliyken oyuncu geçmeye çalışınca tetiklenir. UI ipucu bağlamak için: " +
             "\"Önce hırt odasını temizle\".")]
    public UnityEvent onBlocked;

    private GameProgress _progress;
    private float _lastWarnTime = Mathf.NegativeInfinity;
    private bool _unlocked;

    /// <summary>Kapı açık mı? (UI/başka sistemler okuyabilir.)</summary>
    public bool IsUnlocked => _unlocked;

    /// <summary>Bu kapının beklediği adım.</summary>
    public GameStage RequiredStage => requiredStage;

    private void Awake()
    {
        if (blockers == null || blockers.Length == 0) CollectBlockers();
    }

    private void Start()
    {
        _progress = GameProgress.Instance;
        _progress.StageCompleted += OnStageCompleted;

        // Ilerleme korunarak sahne yeniden yuklendiyse kapi zaten acik olabilir.
        if (_progress.IsStageComplete(requiredStage)) Unlock(instant: true);
        else Lock();
    }

    private void OnDestroy()
    {
        if (_progress != null) _progress.StageCompleted -= OnStageCompleted;
    }

    private void OnStageCompleted(GameStage stage)
    {
        if (stage != requiredStage || _unlocked) return;

        if (unlockDelay > 0f) StartCoroutine(UnlockAfterDelay());
        else Unlock(instant: false);
    }

    private IEnumerator UnlockAfterDelay()
    {
        yield return new WaitForSeconds(unlockDelay);
        Unlock(instant: false);
    }

    /// <summary>Kapıyı açar. UnityEvent'ten veya cutscene'den elle de çağrılabilir.</summary>
    public void Unlock(bool instant)
    {
        if (_unlocked) return;
        _unlocked = true;

        SetBlockersActive(false);
        SetObjectsActive(showWhileLocked, false);
        SetObjectsActive(showWhenUnlocked, true);

        // Sahne basinda zaten acik olan kapi ses calmasin.
        if (!instant) PlaySfx(unlockClip);

        onUnlocked?.Invoke();
    }

    /// <summary>Kapıyı yeniden kilitler (oyuncuyu odada tutmak, ikinci tur).</summary>
    public void Lock()
    {
        _unlocked = false;

        SetBlockersActive(true);
        SetObjectsActive(showWhileLocked, true);
        SetObjectsActive(showWhenUnlocked, false);

        onLocked?.Invoke();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!warnOnBlocked || _unlocked) return;
        if (Time.time < _lastWarnTime + warnCooldown) return;

        // Oyuncuyu DamageSource/BossRoomTurn ile ayni sekilde tani.
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead) return;

        _lastWarnTime = Time.time;
        PlaySfx(lockedClip);
        onBlocked?.Invoke();
    }

    /// <summary>Engeli oluşturan katı collider'ları kendi hiyerarşisinden toplar.</summary>
    private void CollectBlockers()
    {
        var found = new System.Collections.Generic.List<Collider>();

        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            // Trigger'lar uyari alanidir, engel degil.
            if (!col.isTrigger) found.Add(col);
        }

        blockers = found.ToArray();

        if (blockers.Length == 0)
            Debug.LogWarning($"{name}: Kapida kati (Is Trigger kapali) collider yok; " +
                             "oyuncuyu hicbir sey durdurmaz. Bir BoxCollider ekle.", this);
    }

    private void SetBlockersActive(bool active)
    {
        if (blockers == null) return;

        foreach (Collider col in blockers)
            if (col != null) col.enabled = active;
    }

    private static void SetObjectsActive(GameObject[] objects, bool active)
    {
        if (objects == null) return;

        foreach (GameObject go in objects)
            if (go != null) go.SetActive(active);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null) return;

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }

        audioSource.PlayOneShot(clip, sfxVolume);
    }

    private void OnDrawGizmos()
    {
        // Kilitli kapiyi kirmizi, acilmis kapiyi yesil goster.
        Gizmos.color = Application.isPlaying && _unlocked
            ? new Color(0.2f, 1f, 0.3f, 0.25f)
            : new Color(1f, 0.15f, 0.15f, 0.3f);

        foreach (Collider col in GetComponentsInChildren<Collider>(true))
        {
            if (col.isTrigger) continue;
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }
    }
}
