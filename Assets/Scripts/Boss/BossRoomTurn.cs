using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Boss odası girişindeki jump scare tetikleyicisi. Oyuncu kapıdaki trigger
/// collider'a girince: (opsiyonel gecikmeden sonra) scare objesi açılır, animasyon
/// tetiklenir, kamera sarsılır, oyuncu kısa süre kilitlenir ve odadaki heykeller
/// aynı anda oyuncuya döner.
///
/// Kurulum: bu script'i odanın girişine koyulan boş bir GameObject'e ekle ve
/// üzerindeki Collider'ı "Is Trigger" yap. Oyuncu, PlayerHealth bileşeni üzerinden
/// tanınır (DamageSource ile aynı mantık).
///
/// Ses: klip slotları ve UnityEvent'ler ön hazırlık olarak burada. Klip atanmazsa
/// hiçbir şey çalmaz, tetikleyici sorunsuz çalışmaya devam eder.
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class BossRoomTurn : MonoBehaviour
{
    [Header("Tetikleyici")]
    [Tooltip("Açıksa sadece bir kez tetiklenir (jump scare tekrar etmesin).")]
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("Oyuncu girdikten sonra scare'in patlaması için beklenecek süre. Küçük gecikme etkiyi artırır.")]
    [SerializeField] private float delayBeforeScare = 0.25f;
    [Tooltip("Scare'in toplam süresi; bu süre sonunda kilit kalkar ve onScareEnd tetiklenir.")]
    [SerializeField] private float scareDuration = 1.5f;

    [Header("Scare objesi")]
    [Tooltip("Scare anında aktifleşecek obje (boss, fırlayan heykel, yüz, gölge vb.).")]
    [SerializeField] private GameObject scareObject;
    [Tooltip("Açıksa scare objesi oyun başında kapatılır. Boss sahnede zaten duruyorsa KAPAT.")]
    [SerializeField] private bool startHidden = true;
    [Tooltip("Açıksa scare bitince obje tekrar kapatılır. Boss savaşa devam edecekse KAPAT.")]
    [SerializeField] private bool hideScareObjectAfter = true;

    [Header("Scare animasyonu")]
    [Tooltip("Oynatılacak Animator (boss'un Animator'ı). Boşsa scareObject üzerinde aranır.")]
    [SerializeField] private Animator scareAnimator;
    [Tooltip("Animator Controller'daki trigger parametresinin adı (birebir aynı yazılmalı).")]
    [SerializeField] private string scareTrigger = "Scare";
    [Tooltip("Animasyonun bulunduğu Animator layer'ı (genelde 0).")]
    [SerializeField] private int scareLayer = 0;
    [Tooltip("Açıksa kamera kilidi scareDuration yerine animasyon bitene kadar sürer.")]
    [SerializeField] private bool holdUntilAnimationEnds = false;
    [Tooltip("Beklenecek state'in Animator'daki adı (ör. BossScream). Boşsa trigger sonrası girilen state kullanılır.")]
    [SerializeField] private string scareStateName = "";
    [Tooltip("Animasyon beklerken güvenlik sınırı; bu süre dolarsa kamera yine de serbest bırakılır.")]
    [SerializeField] private float animationTimeout = 8f;

    [Header("Oyuncu kilidi")]
    [Tooltip("Açıksa scare boyunca oyuncunun kontrolü kapanır (kaçamaz, izlemek zorunda).")]
    [SerializeField] private bool freezePlayer = true;
    [Tooltip("Kontrolün kapalı kalacağı süre. 0 veya daha küçükse scareDuration kullanılır.")]
    [SerializeField] private float freezeDuration = 0f;

    [Header("Kamera")]
    [Tooltip("Açıksa scare boyunca kamera zorla hedefe bakar (PlayerLook geçici kapatılır).")]
    [SerializeField] private bool forceLookAtScare = true;
    [Tooltip("Kameranın kilitleneceği transform. Boşsa scareObject kullanılır. Boss'un kafa/göğüs bone'unu atarsan kamera yüzünü çerçeveler.")]
    [SerializeField] private Transform lookTarget;
    [Tooltip("PlayerLook'un döndürdüğü kamera pivotu. Boşsa PlayerLook'tan otomatik alınır.")]
    [SerializeField] private Transform cameraPivot;
    [Tooltip("Scare boyunca devre dışı bırakılacak PlayerLook. Boşsa sahnede aranır.")]
    [SerializeField] private PlayerLook playerLook;
    [Tooltip("Zorla bakışın dönüş hızı (Slerp katsayısı).")]
    [SerializeField] private float forceLookSpeed = 12f;

    [Header("Kamera sarsıntısı")]
    [Tooltip("Sarsılacak kamera transformu. Boşsa Camera.main kullanılır.")]
    [SerializeField] private Transform shakeTransform;
    [SerializeField] private float shakeDuration = 0.4f;
    [Tooltip("Sarsıntı şiddeti (birim). 0 = sarsıntı yok.")]
    [SerializeField] private float shakeStrength = 0.25f;

    [Header("Dövüş")]
    [Tooltip("Scare bitince başlatılacak boss dövüşü. Boşsa scareObject üzerinde aranır.")]
    [SerializeField] private BossFight bossFight;
    [Tooltip("Açıksa scare biter bitmez dövüş başlar. Kapalıysa dövüşü sen tetiklersin.")]
    [SerializeField] private bool startFightAfterScare = true;

    [Header("Heykeller")]
    [Tooltip("Scare anında hep birlikte oyuncuya dönecek heykeller. Odadaki StatueWatcher'ları buraya sürükle.")]
    [SerializeField] private StatueWatcher[] statuesToAlert;

    [Header("Ses (ön hazırlık, klipler sonra atanacak)")]
    [Tooltip("Boşsa Awake'te otomatik AudioSource eklenir ve 3D olarak ayarlanır.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Oyuncu trigger'a girer girmez çalar (gerilim yükselten build-up).")]
    [SerializeField] private AudioClip buildUpClip;
    [Tooltip("Scare patladığı anda çalar (stinger / çığlık).")]
    [SerializeField] private AudioClip scareClip;
    [Tooltip("Scare bittikten sonra çalar (nefes, kalp atışı, sessizliğe dönüş).")]
    [SerializeField] private AudioClip aftermathClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar (ses/ışık/UI bağlamak için)")]
    [Tooltip("Scare patladığı anda tetiklenir.")]
    public UnityEvent onScareTriggered;
    [Tooltip("Scare bitip kontrol oyuncuya döndüğünde tetiklenir.")]
    public UnityEvent onScareEnd;

    private PlayerController _playerController;
    private Rigidbody _playerRb;
    private Transform _playerTransform;
    // Zorla bakış sırasında PlayerLook kapalı olduğu için pivotun takip ofsetini kendimiz koruruz.
    private Vector3 _pivotFollowOffset;
    private bool _hasTriggered;
    private bool _isPlaying;
    private bool _animationFinished;

    /// <summary>Jump scare daha önce tetiklendi mi? (Boss akışı okuyabilir.)</summary>
    public bool HasTriggered => _hasTriggered;

    private void Awake()
    {
        // Collider trigger değilse jump scare hiç çalışmaz; erken uyar.
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"{name}: BossRoomTurn collider'ı 'Is Trigger' olmalı.", this);

        // Scare objesi başta gizlenecekse kapat (boss sahnede duracaksa startHidden'ı kapat).
        if (startHidden && scareObject != null) scareObject.SetActive(false);

        // Animator elle atanmadıysa scare objesinden al (child'lar dahil).
        if (scareAnimator == null && scareObject != null)
            scareAnimator = scareObject.GetComponentInChildren<Animator>(true);

        // Dövüş script'i elle atanmadıysa scare objesinden al.
        if (bossFight == null && scareObject != null)
            bossFight = scareObject.GetComponentInChildren<BossFight>(true);

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }
    }

    private void Start()
    {
        if (playerLook == null) playerLook = FindAnyObjectByType<PlayerLook>();
        if (cameraPivot == null && playerLook != null) cameraPivot = playerLook.CameraPivot;
        if (shakeTransform == null && Camera.main != null) shakeTransform = Camera.main.transform;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isPlaying) return;
        if (triggerOnce && _hasTriggered) return;

        // Oyuncuyu DamageSource ile aynı şekilde tanı (child collider'lar da çalışsın).
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead) return;

        _hasTriggered = true;
        _playerTransform = health.transform;
        _playerController = health.GetComponent<PlayerController>();
        _playerRb = health.GetComponent<Rigidbody>();

        StartCoroutine(ScareRoutine());
    }

    /// <summary>Jump scare akışı: build-up, patlama, kilit ve toparlanma.</summary>
    private IEnumerator ScareRoutine()
    {
        _isPlaying = true;

        PlaySfx(buildUpClip);
        if (delayBeforeScare > 0f) yield return new WaitForSeconds(delayBeforeScare);

        // --- Patlama anı ---
        // Önce objeyi aç: kapalı bir GameObject'in Animator'ına trigger yazmak işe yaramaz.
        if (scareObject != null) scareObject.SetActive(true);
        if (scareAnimator != null && !string.IsNullOrEmpty(scareTrigger))
            scareAnimator.SetTrigger(scareTrigger);

        // Odadaki tüm heykeller aynı anda oyuncuya dönsün.
        if (statuesToAlert != null)
        {
            foreach (StatueWatcher statue in statuesToAlert)
            {
                if (statue != null) statue.SnapToTarget();
            }
        }

        PlaySfx(scareClip);
        onScareTriggered?.Invoke();

        bool frozen = freezePlayer && LockPlayer(true);
        if (shakeStrength > 0f && shakeTransform != null) StartCoroutine(ShakeRoutine());

        // Kamerayı devral: PlayerLook her LateUpdate'te rotasyonu mutlak yazdığı için
        // zorla bakış ancak o kapalıyken çalışır (oyuncu kilidinden bağımsız).
        bool cameraTaken = forceLookAtScare && cameraPivot != null;
        if (cameraTaken)
        {
            if (playerLook != null) playerLook.enabled = false;
            // PlayerLook kapalıyken pivotun oyuncuyu takibini kendimiz sürdürürüz.
            if (_playerTransform != null)
                _pivotFollowOffset = cameraPivot.position - _playerTransform.position;
        }

        // --- Scare süresi: gerekiyorsa kamerayı zorla hedefe çevir ---
        // Kamera hedefi: elle atanan lookTarget > scare objesi > bu obje.
        Transform focus = lookTarget != null ? lookTarget
                        : (scareObject != null ? scareObject.transform : transform);

        // Animasyonu bekleyeceksek bitişi ayrı bir coroutine belirler.
        bool waitForAnimation = holdUntilAnimationEnds && scareAnimator != null;
        _animationFinished = false;
        if (waitForAnimation) StartCoroutine(WaitForScareAnimation());

        // Animasyon beklenirken kilit süresi de animasyona uyar (freezeDuration verilmedikçe).
        float lockTime = freezeDuration > 0f ? freezeDuration
                       : (waitForAnimation ? Mathf.Infinity : scareDuration);
        float elapsed = 0f;

        while (true)
        {
            if (cameraTaken) ForceLook(focus);

            elapsed += Time.deltaTime;
            // Kilit süresi scare'den kısaysa kontrolü erken geri ver.
            if (frozen && elapsed >= lockTime)
            {
                LockPlayer(false);
                frozen = false;
            }

            bool finished = waitForAnimation ? _animationFinished : elapsed >= scareDuration;
            if (finished) break;

            yield return null;
        }

        // --- Toparlanma ---
        if (frozen) LockPlayer(false);

        if (cameraTaken && playerLook != null)
        {
            // Kamerayı biz çevirdik; PlayerLook açılmadan önce açılarını güncel pivottan
            // okusun, yoksa kontrol geri gelince kamera eski açısına zıplar.
            playerLook.SyncFromPivot();
            playerLook.enabled = true;
        }

        if (hideScareObjectAfter && scareObject != null) scareObject.SetActive(false);

        PlaySfx(aftermathClip);
        onScareEnd?.Invoke();

        // Scare bitti: boss artık oyuncuyu kovalayabilir.
        if (startFightAfterScare && bossFight != null) bossFight.BeginFight();

        _isPlaying = false;
    }

    /// <summary>
    /// Scare animasyonunun state'e girip bitmesini bekler; bitince _animationFinished'i
    /// işaretler. Loop'a giren ya da hiç tetiklenmeyen animasyonlarda animationTimeout
    /// devreye girer, böylece oyuncu kamerada asılı kalmaz.
    /// </summary>
    private IEnumerator WaitForScareAnimation()
    {
        float deadline = Time.time + animationTimeout;
        bool checkName = !string.IsNullOrEmpty(scareStateName);

        // 1) Trigger'dan sonra state'e geçiş birkaç frame sürebilir; girişi bekle.
        while (Time.time < deadline)
        {
            if (!scareAnimator.IsInTransition(scareLayer))
            {
                AnimatorStateInfo info = scareAnimator.GetCurrentAnimatorStateInfo(scareLayer);
                if (!checkName || info.IsName(scareStateName)) break;
            }
            yield return null;
        }

        // 2) State'in sonunu (normalizedTime >= 1) ya da başka state'e çıkışı bekle.
        while (Time.time < deadline)
        {
            AnimatorStateInfo info = scareAnimator.GetCurrentAnimatorStateInfo(scareLayer);
            if (checkName && !info.IsName(scareStateName)) break;
            if (!scareAnimator.IsInTransition(scareLayer) && info.normalizedTime >= 1f) break;
            yield return null;
        }

        _animationFinished = true;
    }

    /// <summary>Oyuncu hareket/saldırı kontrolünü açar-kapatır (kamera ayrı yönetilir).</summary>
    private bool LockPlayer(bool locked)
    {
        if (_playerController != null) _playerController.enabled = !locked;

        // Kilitlenirken yatay hızı sıfırla ki oyuncu kayarak gitmesin.
        if (locked && _playerRb != null)
            _playerRb.linearVelocity = new Vector3(0f, _playerRb.linearVelocity.y, 0f);

        return locked;
    }

    /// <summary>Kamera pivotunu scare hedefine yumuşakça çevirir.</summary>
    private void ForceLook(Transform lookTarget)
    {
        // PlayerLook kapalı olduğu için pivotu oyuncuya biz sabitliyoruz.
        if (_playerTransform != null)
            cameraPivot.position = _playerTransform.position + _pivotFollowOffset;

        Vector3 dir = lookTarget.position - cameraPivot.position;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion desired = Quaternion.LookRotation(dir.normalized, Vector3.up);
        cameraPivot.rotation = Quaternion.Slerp(
            cameraPivot.rotation, desired, forceLookSpeed * Time.deltaTime);
    }

    /// <summary>Kamerayı localPosition offset'iyle sarsar, sonra eski yerine koyar.</summary>
    private IEnumerator ShakeRoutine()
    {
        Vector3 original = shakeTransform.localPosition;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            // Şiddet zamanla sönümlensin.
            float damper = 1f - (elapsed / shakeDuration);
            shakeTransform.localPosition = original + Random.insideUnitSphere * (shakeStrength * damper);

            elapsed += Time.deltaTime;
            yield return null;
        }

        shakeTransform.localPosition = original;
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, sfxVolume);
    }

    /// <summary>Test/akış için scare'i dışarıdan tetikler (ör. boss cutscene'i).</summary>
    public void TriggerManually(Transform player)
    {
        if (_isPlaying || (triggerOnce && _hasTriggered)) return;

        _hasTriggered = true;
        if (player != null)
        {
            _playerTransform = player;
            _playerController = player.GetComponent<PlayerController>();
            _playerRb = player.GetComponent<Rigidbody>();
        }
        StartCoroutine(ScareRoutine());
    }

    private void OnDrawGizmos()
    {
        // Trigger alanını ve scare objesine giden bağı sahnede göster.
        var col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.color = new Color(1f, 0f, 0.3f, 0.25f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }

        if (scareObject != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, scareObject.transform.position);
        }
    }
}
