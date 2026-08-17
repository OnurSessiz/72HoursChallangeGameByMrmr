using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Heykel/statü objelerini "canlıymış gibi" gösterir: oyuncu belirlenen yarıçapa
/// girdiğinde heykel ona döner, menzilden çıkınca (isteğe bağlı) eski duruşuna döner.
///
/// Kullanım senaryoları:
///   - Boss odasına giden koridor: Smooth mod + geniş radius, yavaşça oyuncuyu takip eder.
///   - Boss odası içi: Snap mod, belirli aralıklarla ani "seğirme" ile dönüş (daha gerici).
///   - Weeping Angel: onlyTurnWhenUnobserved açıkken heykel SADECE oyuncu bakmıyorken döner;
///     oyuncu geri baktığında heykel yönünü değiştirmiş olur.
///
/// Ses: klipler ve UnityEvent'ler ön hazırlık olarak burada duruyor. Klip atanmadığında
/// hiçbir şey çalmaz, script sorunsuz çalışmaya devam eder.
/// </summary>
[DisallowMultipleComponent]
public class StatueWatcher : MonoBehaviour
{
    /// <summary>Dönüş stili: yumuşak takip mi, ani seğirme mi.</summary>
    public enum TurnMode
    {
        Smooth, // sürekli, yumuşak takip
        Snap    // snapInterval'da bir anlık dönüş (tedirgin edici)
    }

    [Header("Hedef")]
    [Tooltip("Bakılacak hedef. Boşsa 'Player' tag'li obje otomatik bulunur.")]
    [SerializeField] private Transform target;
    [Tooltip("Asıl döndürülecek transform. Boşsa bu obje döner. Sadece kafa dönsün istersen head bone'unu ata.")]
    [SerializeField] private Transform rotateTransform;

    [Header("Menzil")]
    [Tooltip("Oyuncu bu mesafeye girince heykel ona bakmaya başlar.")]
    [SerializeField] private float detectionRadius = 12f;
    [Tooltip("Çıkış bu kadar fazla mesafede sayılır; sınırda girdi/çıktı titremesini önler.")]
    [SerializeField] private float exitBuffer = 1.5f;

    [Header("Dönüş")]
    [SerializeField] private TurnMode turnMode = TurnMode.Smooth;
    [Tooltip("Smooth modda dönüş hızı (Slerp katsayısı).")]
    [SerializeField] private float rotationSpeed = 3f;
    [Tooltip("Snap modda iki ani dönüş arası süre (saniye). Küçük değer = daha sık seğirme.")]
    [SerializeField] private float snapInterval = 1.2f;
    [Tooltip("Sadece Y ekseninde döner; heykel öne/arkaya yatmaz.")]
    [SerializeField] private bool yawOnly = true;
    [Tooltip("Modelin yüzü +Z yönüne bakmıyorsa derece cinsinden düzeltme (ör. yan duruyorsa 90).")]
    [SerializeField] private float yawOffset = 0f;
    [Tooltip("Oyuncu menzilden çıkınca heykel başlangıç rotasyonuna geri dönsün mü?")]
    [SerializeField] private bool returnToStartRotation = false;

    [Header("Weeping Angel (opsiyonel)")]
    [Tooltip("Açıksa heykel SADECE oyuncu ona bakmıyorken döner. Klasik 'arkamı dönünce yer değiştirmiş' efekti.")]
    [SerializeField] private bool onlyTurnWhenUnobserved = false;
    [Tooltip("Oyuncunun göz/kamera transformu. Boşsa Camera.main kullanılır.")]
    [SerializeField] private Transform playerEye;
    [Tooltip("Bakış toleransı: 1 = tam ekran merkezi, 0.5 yaklaşık 60 derecelik koni. Üstündeyse 'oyuncu bakıyor' sayılır.")]
    [Range(0f, 1f)]
    [SerializeField] private float observeDot = 0.5f;
    [Tooltip("Açıksa arada duvar varken oyuncu 'bakmıyor' sayılır, yani heykel dönebilir.")]
    [SerializeField] private bool checkObstruction = true;
    [Tooltip("Görüşü kesen katmanlar (duvar, zemin vb.).")]
    [SerializeField] private LayerMask obstructionMask = ~0;

    [Header("Ses (ön hazırlık, klipler sonra atanacak)")]
    [Tooltip("Boşsa Awake'te otomatik AudioSource eklenir ve 3D olarak ayarlanır.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Oyuncu menzile ilk girdiğinde çalar (fısıltı, taş gıcırtısı vb.).")]
    [SerializeField] private AudioClip enterRangeClip;
    [Tooltip("Heykel her döndüğünde çalar (taş sürtünme / seğirme sesi).")]
    [SerializeField] private AudioClip turnClip;
    [Tooltip("Oyuncu menzildeyken döngüde çalan ortam sesi (uğultu, nefes vb.).")]
    [SerializeField] private AudioClip ambientLoopClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;
    [Tooltip("İki efekt sesi arası minimum süre; seslerin üst üste binmesini önler.")]
    [SerializeField] private float sfxMinInterval = 0.2f;

    [Header("Olaylar (ses/efekt/titreşim bağlamak için)")]
    [Tooltip("Oyuncu menzile girdiğinde bir kez tetiklenir.")]
    public UnityEvent onPlayerEnterRange;
    [Tooltip("Oyuncu menzilden çıktığında bir kez tetiklenir.")]
    public UnityEvent onPlayerExitRange;
    [Tooltip("Heykel dönmeye başladığında (Snap modda her seğirmede) tetiklenir.")]
    public UnityEvent onTurnStart;

    private Quaternion _startRotation;
    private bool _playerInRange;
    private bool _isTurning;
    private float _nextSnapTime;
    private float _lastSfxTime = Mathf.NegativeInfinity;
    private float _nextTargetSearchTime;

    /// <summary>Oyuncu şu anda menzilde mi? (Boss odası tetikleyicileri okuyabilir.)</summary>
    public bool IsPlayerInRange => _playerInRange;

    /// <summary>Heykelin bakacağı hedefi çalışma anında değiştirir.</summary>
    public void SetTarget(Transform newTarget) => target = newTarget;

    /// <summary>
    /// Heykeli anında hedefe döndürür ve dönüş olayını/sesini tetikler.
    /// Jump scare gibi anlarda dışarıdan (ör. BossRoomTurn) çağrılır.
    /// </summary>
    public void SnapToTarget()
    {
        if (rotateTransform == null) rotateTransform = transform;
        TryFindTarget();
        if (target == null) return;

        rotateTransform.rotation = GetDesiredRotation();
        _nextSnapTime = Time.time + snapInterval;
        NotifyTurnStart();
    }

    private void Reset()
    {
        rotateTransform = transform;
    }

    private void Awake()
    {
        if (rotateTransform == null) rotateTransform = transform;
        _startRotation = rotateTransform.rotation;

        // Ses kaynağı yoksa oluştur: 3D, kendiliğinden başlamayan, döngüye hazır.
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.maxDistance = detectionRadius * 1.5f;
        }
    }

    private void Start()
    {
        TryFindTarget();
        if (playerEye == null && Camera.main != null) playerEye = Camera.main.transform;
    }

    private void Update()
    {
        // Oyuncu sahneye sonradan gelmiş olabilir; saniyede bir tekrar ara.
        if (target == null)
        {
            if (Time.time >= _nextTargetSearchTime)
            {
                _nextTargetSearchTime = Time.time + 1f;
                TryFindTarget();
            }
            return;
        }

        UpdateRangeState();

        if (_playerInRange) TrackTarget();
        else if (returnToStartRotation) ReturnToStart();
    }

    /// <summary>Menzile giriş/çıkışı histerezisle (exitBuffer) belirler ve olayları tetikler.</summary>
    private void UpdateRangeState()
    {
        float distance = Vector3.Distance(rotateTransform.position, target.position);
        // Girişte detectionRadius, çıkışta detectionRadius + exitBuffer: sınırda titreme olmaz.
        float threshold = _playerInRange ? detectionRadius + exitBuffer : detectionRadius;
        bool inRangeNow = distance <= threshold;

        if (inRangeNow == _playerInRange) return;
        _playerInRange = inRangeNow;

        if (_playerInRange)
        {
            _nextSnapTime = Time.time + snapInterval;
            PlaySfx(enterRangeClip);
            StartAmbient();
            onPlayerEnterRange?.Invoke();
        }
        else
        {
            _isTurning = false;
            StopAmbient();
            onPlayerExitRange?.Invoke();
        }
    }

    /// <summary>Menzildeyken hedefe dönüşü seçili moda göre uygular.</summary>
    private void TrackTarget()
    {
        // Weeping Angel: oyuncu bakıyorsa heykel donar.
        if (onlyTurnWhenUnobserved && IsObservedByPlayer())
        {
            _isTurning = false;
            return;
        }

        Quaternion desired = GetDesiredRotation();
        float angle = Quaternion.Angle(rotateTransform.rotation, desired);

        if (turnMode == TurnMode.Snap)
        {
            // Belirli aralıklarla ani dönüş; ihmal edilebilir açılarda seğirmeyi tetikleme.
            if (Time.time < _nextSnapTime || angle < 1f) return;

            _nextSnapTime = Time.time + snapInterval;
            rotateTransform.rotation = desired;
            NotifyTurnStart();
            return;
        }

        // Smooth: yumuşak takip. Kayda değer açı farkı varsa "dönmeye başladı" say.
        if (angle < 0.5f)
        {
            _isTurning = false;
            return;
        }

        if (!_isTurning && angle > 5f) NotifyTurnStart();
        _isTurning = true;

        rotateTransform.rotation = Quaternion.Slerp(
            rotateTransform.rotation, desired, rotationSpeed * Time.deltaTime);
    }

    /// <summary>Menzil dışında başlangıç rotasyonuna yumuşakça döner.</summary>
    private void ReturnToStart()
    {
        if (Quaternion.Angle(rotateTransform.rotation, _startRotation) < 0.5f) return;

        rotateTransform.rotation = Quaternion.Slerp(
            rotateTransform.rotation, _startRotation, rotationSpeed * Time.deltaTime);
    }

    /// <summary>Hedefe bakan rotasyonu hesaplar (yawOnly ve yawOffset dahil).</summary>
    private Quaternion GetDesiredRotation()
    {
        Vector3 toTarget = target.position - rotateTransform.position;
        if (yawOnly) toTarget.y = 0f; // heykel öne/arkaya yatmasın
        if (toTarget.sqrMagnitude < 0.0001f) return rotateTransform.rotation;

        Quaternion look = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        // Modelin yüzü +Z değilse ofsetle düzelt.
        return look * Quaternion.Euler(0f, yawOffset, 0f);
    }

    /// <summary>Oyuncu şu anda heykele bakıyor mu? (bakış konisi + isteğe bağlı duvar kontrolü)</summary>
    private bool IsObservedByPlayer()
    {
        if (playerEye == null) return false;

        Vector3 eyeToStatue = rotateTransform.position - playerEye.position;
        float distance = eyeToStatue.magnitude;
        if (distance < 0.001f) return true;

        Vector3 dir = eyeToStatue / distance;
        // Bakış konisinin dışındaysa görmüyor.
        if (Vector3.Dot(playerEye.forward, dir) < observeDot) return false;

        // Arada duvar varsa görmüyor sayılır, yani heykel dönebilir.
        if (checkObstruction &&
            Physics.Raycast(playerEye.position, dir, out RaycastHit hit, distance, obstructionMask,
                QueryTriggerInteraction.Ignore))
        {
            // Isabet eden şey heykelin kendisi değilse görüş kesilmiştir.
            if (!hit.transform.IsChildOf(transform)) return false;
        }

        return true;
    }

    /// <summary>Dönüş başlangıcı: olay + ses (klip atanmadıysa sessiz geçer).</summary>
    private void NotifyTurnStart()
    {
        PlaySfx(turnClip);
        onTurnStart?.Invoke();
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        if (Time.time < _lastSfxTime + sfxMinInterval) return;

        _lastSfxTime = Time.time;
        audioSource.PlayOneShot(clip, sfxVolume);
    }

    private void StartAmbient()
    {
        if (ambientLoopClip == null || audioSource == null) return;

        audioSource.clip = ambientLoopClip;
        audioSource.loop = true;
        audioSource.volume = sfxVolume;
        audioSource.Play();
    }

    private void StopAmbient()
    {
        if (ambientLoopClip == null || audioSource == null) return;
        if (audioSource.clip == ambientLoopClip) audioSource.Stop();
    }

    private void TryFindTarget()
    {
        if (target != null) return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) target = player.transform;
    }

    private void OnDrawGizmosSelected()
    {
        Transform pivot = rotateTransform != null ? rotateTransform : transform;

        // Algılama ve çıkış yarıçapları.
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(pivot.position, detectionRadius);
        Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
        Gizmos.DrawWireSphere(pivot.position, detectionRadius + exitBuffer);

        // Heykelin baktığı yön (yawOffset dahil).
        Gizmos.color = Color.magenta;
        Vector3 facing = pivot.rotation * Quaternion.Euler(0f, -yawOffset, 0f) * Vector3.forward;
        Gizmos.DrawRay(pivot.position, facing * 2f);

        // Hedefe çizgi.
        if (target != null)
        {
            Gizmos.color = _playerInRange ? Color.red : Color.gray;
            Gizmos.DrawLine(pivot.position, target.position);
        }
    }
}
