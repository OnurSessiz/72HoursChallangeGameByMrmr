using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Hırt odası pususu. Oyuncu odanın girişindeki trigger'a girince:
///   1) Sinematik kameraya kesilir (opsiyonel), oyuncu kısa süre kilitlenir
///   2) Islıkçı hırt ıslık animasyonunu oynatır + ıslık sesi çalar
///   3) Islık bitince odadaki TÜM hırtlar oyuncuya döner ve saldırmaya başlar
///
/// Kurulum: odanın girişine boş bir GameObject koy, üzerine bir Collider ekle ve
/// "Is Trigger" işaretle. Hırtları enemies listesine sürükle (ya da enemiesParent'a
/// hepsinin parent'ını ver, liste boşsa child'lardan otomatik toplanır).
/// Hırtların EnemyFollow'unda "Activate On Start" KAPALI olmalı; yoksa oyuncuyu
/// ıslıktan önce kovalarlar.
///
/// Islık animasyonu: whistleTrigger ("Whistle") Enemy.controller'daki Whistle
/// state'ini tetikler. Klip henüz hazır değilse state boş kalır; animasyonu
/// bitirince Tools/Enemy/Create Enemy Animator ile bağlanır ya da controller'daki
/// Whistle state'ine elle sürüklenir. Klip yokken de akış çalışır (sadece animasyon oynamaz).
/// </summary>
[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public class HirtRoomAmbush : MonoBehaviour
{
    [Header("Tetikleyici")]
    [Tooltip("Açıksa pusu yalnızca bir kez kurulur.")]
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("Oyuncu girdikten sonra ıslığın çalması için beklenecek süre.")]
    [SerializeField] private float delayBeforeWhistle = 0.25f;

    [Header("Islıkçı hırt")]
    [Tooltip("Islığı çalacak hırt. Boşsa enemies listesindeki ilk hırt kullanılır.")]
    [SerializeField] private EnemyFollow whistler;
    [Tooltip("Islıkçının Animator'ı. Boşsa whistler üzerinde aranır.")]
    [SerializeField] private Animator whistlerAnimator;
    [Tooltip("Islık animasyonunun trigger adı (Enemy.controller'daki Whistle state'i).")]
    [SerializeField] private string whistleTrigger = "Whistle";
    [Tooltip("Islığın süresi; bu süre sonunda hırtlar saldırıya geçer.")]
    [SerializeField] private float whistleDuration = 2f;
    [Tooltip("Açıksa süre yerine ıslık animasyonunun bitmesi beklenir.")]
    [SerializeField] private bool holdUntilAnimationEnds = false;
    [Tooltip("Beklenecek state'in Animator'daki adı (ör. Whistle). Boşsa o an oynayan state kullanılır.")]
    [SerializeField] private string whistleStateName = "Whistle";
    [Tooltip("Animasyonun bulunduğu Animator layer'ı (genelde 0).")]
    [SerializeField] private int whistleLayer = 0;
    [Tooltip("Animasyon beklerken güvenlik sınırı; dolarsa akış yine de devam eder.")]
    [SerializeField] private float animationTimeout = 8f;

    [Header("Hırtlar")]
    [Tooltip("Islık bitince saldırıya geçecek hırtlar.")]
    [SerializeField] private EnemyFollow[] enemies;
    [Tooltip("Liste boşsa bu objenin child'larındaki tüm EnemyFollow'lar toplanır.")]
    [SerializeField] private Transform enemiesParent;
    [Tooltip("Açıksa ıslık başlar başlamaz hepsi oyuncuya döner (saldırı ıslık bitince başlar).")]
    [SerializeField] private bool turnToPlayerOnWhistle = true;
    [Tooltip("Hırtlar arasında saldırı başlangıcına eklenecek gecikme (saniye). 0 = hepsi aynı anda.")]
    [SerializeField] private float staggerBetweenEnemies = 0.15f;

    [Header("Oyuncu kilidi")]
    [Tooltip("Açıksa ıslık boyunca oyuncunun kontrolü kapanır.")]
    [SerializeField] private bool freezePlayer = true;
    [Tooltip("Kontrolün kapalı kalacağı süre. 0 veya daha küçükse ıslık süresi kullanılır.")]
    [SerializeField] private float freezeDuration = 0f;

    [Header("Sinematik kamera (opsiyonel)")]
    [Tooltip("Islık anında geçilecek kamera. Islıkçının yüzünü çerçeveleyecek şekilde yerleştir. Boşsa kesme yapılmaz.")]
    [SerializeField] private Camera cutsceneCamera;
    [Tooltip("Islık başladıktan kaç saniye sonra sinematik kameraya kesilsin. 0 = aynı anda.")]
    [SerializeField] private float cutInDelay = 0f;
    [Tooltip("Sinematik kamerada kalınacak süre. 0 veya daha küçükse ıslık bitene kadar kalır.")]
    [SerializeField] private float cutDuration = 0f;
    [Tooltip("Açıksa sinematik kamera oyun başında kapatılır (sahnede açık unutulsa bile).")]
    [SerializeField] private bool disableCutsceneCameraOnStart = true;
    [Tooltip("Oyuncunun normal kamerası. Boşsa Start'ta Camera.main'den alınır.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Kesme sırasında PlayerLook kapatılsın mı? (Kamera oynamasın.)")]
    [SerializeField] private bool disablePlayerLookDuringCut = true;
    [Tooltip("Kapatılacak PlayerLook. Boşsa sahnede aranır.")]
    [SerializeField] private PlayerLook playerLook;

    [Header("Kamera sarsıntısı")]
    [Tooltip("Sarsılacak kamera transformu. Boşsa ekranda olan kamera kullanılır.")]
    [SerializeField] private Transform shakeTransform;
    [SerializeField] private float shakeDuration = 0.3f;
    [Tooltip("Sarsıntı şiddeti (birim). 0 = sarsıntı yok.")]
    [SerializeField] private float shakeStrength = 0f;

    [Header("Ses")]
    [Tooltip("Boşsa Awake'te otomatik AudioSource eklenir ve 3D olarak ayarlanır.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Islık sesi. Animasyonla aynı anda çalar.")]
    [SerializeField] private AudioClip whistleClip;
    [Tooltip("Oyuncu odaya girer girmez çalar (gerilim). Opsiyonel.")]
    [SerializeField] private AudioClip enterClip;
    [Tooltip("Hırtlar saldırıya geçtiği anda çalar (bağırma, koşu). Opsiyonel.")]
    [SerializeField] private AudioClip chargeClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar")]
    [Tooltip("Islık çalındığı anda tetiklenir (müzik, ışık, UI).")]
    public UnityEvent onWhistle;
    [Tooltip("Hırtlar saldırıya geçtiğinde tetiklenir.")]
    public UnityEvent onAmbushStart;

    private PlayerController _playerController;
    private Rigidbody _playerRb;
    private bool _hasTriggered;
    private bool _isPlaying;
    private bool _cutsceneActive;
    private bool _animationFinished;

    /// <summary>Pusu daha önce kuruldu mu? (Başka sistemler okuyabilir.)</summary>
    public bool HasTriggered => _hasTriggered;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"{name}: HirtRoomAmbush collider'ı 'Is Trigger' olmalı.", this);

        CollectEnemies();

        // Islıkçı verilmediyse listedeki ilk hırt ıslığı çalsın.
        if (whistler == null && enemies != null && enemies.Length > 0) whistler = enemies[0];
        if (whistlerAnimator == null && whistler != null)
            whistlerAnimator = whistler.GetComponentInChildren<Animator>(true);

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

        // Kamerayı kesmeden ÖNCE çözümle: kesme sırasında oyuncunun kamerası
        // kapandığı için Camera.main null'a düşer.
        if (playerCamera == null) playerCamera = Camera.main;

        if (cutsceneCamera != null)
        {
            // İki AudioListener açık kalırsa Unity uyarır ve ses bozulur.
            var cutsceneListener = cutsceneCamera.GetComponent<AudioListener>();
            if (cutsceneListener != null) cutsceneListener.enabled = false;

            if (cutsceneCamera.CompareTag("MainCamera"))
                Debug.LogWarning($"{name}: Sinematik kamera 'MainCamera' tag'li. Camera.main'i çalar; tag'ini Untagged yap.", this);

            if (disableCutsceneCameraOnStart)
            {
                cutsceneCamera.enabled = false;
                cutsceneCamera.gameObject.SetActive(false);
            }
        }

        // Pusuda bekleyen hırtın "Activate On Start"ı açık kalmışsa uyar.
        StartCoroutine(WarnIfEnemiesAwake());
    }

    /// <summary>
    /// Hırtların uyanık olup olmadığını bir kare sonra kontrol eder: EnemyFollow.Start()
    /// bizimkinden sonra çalışabilir, o yüzden aynı karede bakmak yanıltıcı olur.
    /// </summary>
    private IEnumerator WarnIfEnemiesAwake()
    {
        yield return null;

        if (enemies == null) yield break;

        foreach (EnemyFollow enemy in enemies)
        {
            if (enemy != null && enemy.IsActive)
                Debug.LogWarning($"{name}: {enemy.name} zaten aktif. EnemyFollow'daki " +
                                 "'Activate On Start' kapatilmazsa hirtlar islik beklemeden saldirir.", enemy);
        }
    }

    /// <summary>Liste boşsa hırtları enemiesParent'tan (yoksa kendi child'larından) toplar.</summary>
    private void CollectEnemies()
    {
        if (enemies != null && enemies.Length > 0) return;

        Transform root = enemiesParent != null ? enemiesParent : transform;
        enemies = root.GetComponentsInChildren<EnemyFollow>(true);

        if (enemies.Length == 0)
            Debug.LogWarning($"{name}: Hic hirt bulunamadi. enemies listesini doldur ya da " +
                             "enemiesParent'a hirtlarin parent'ini ata.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isPlaying) return;
        if (triggerOnce && _hasTriggered) return;

        // Oyuncuyu DamageSource/BossRoomTurn ile aynı şekilde tanı (child collider'lar da çalışsın).
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead) return;

        _hasTriggered = true;
        _playerController = health.GetComponent<PlayerController>();
        _playerRb = health.GetComponent<Rigidbody>();

        StartCoroutine(AmbushRoutine());
    }

    /// <summary>Pusu akışı: giriş, ıslık, saldırıya geçiş.</summary>
    private IEnumerator AmbushRoutine()
    {
        _isPlaying = true;

        PlaySfx(enterClip);
        if (delayBeforeWhistle > 0f) yield return new WaitForSeconds(delayBeforeWhistle);

        // --- Islık anı ---
        if (whistlerAnimator != null && !string.IsNullOrEmpty(whistleTrigger))
            SetTriggerSafe(whistlerAnimator, whistleTrigger);

        PlaySfx(whistleClip);
        onWhistle?.Invoke();

        // Islık duyulur duyulmaz hepsi oyuncuya dönsün; saldırı ıslık bitince başlar.
        if (turnToPlayerOnWhistle) SnapAllToPlayer();

        bool frozen = freezePlayer && LockPlayer(true);

        if (cutsceneCamera != null) StartCoroutine(CutsceneRoutine());

        if (disablePlayerLookDuringCut && cutsceneCamera != null && playerLook != null)
            playerLook.enabled = false;

        // Sarsıntı EKRANDA OLAN kameraya uygulanmalı.
        if (shakeStrength > 0f)
        {
            bool cutsAtOnce = cutsceneCamera != null && cutInDelay <= 0f;
            Transform shakeTarget = shakeTransform != null ? shakeTransform
                                  : (cutsAtOnce ? cutsceneCamera.transform
                                                : (playerCamera != null ? playerCamera.transform : null));
            if (shakeTarget != null) StartCoroutine(ShakeRoutine(shakeTarget));
        }

        // --- Islık süresi ---
        bool waitForAnimation = holdUntilAnimationEnds && whistlerAnimator != null;
        _animationFinished = false;
        if (waitForAnimation) StartCoroutine(WaitForWhistleAnimation());

        float lockTime = freezeDuration > 0f ? freezeDuration
                       : (waitForAnimation ? Mathf.Infinity : whistleDuration);
        float elapsed = 0f;

        while (true)
        {
            elapsed += Time.deltaTime;

            // Kilit süresi ıslıktan kısaysa kontrolü erken geri ver.
            if (frozen && elapsed >= lockTime)
            {
                LockPlayer(false);
                frozen = false;
            }

            bool finished = waitForAnimation ? _animationFinished : elapsed >= whistleDuration;
            if (finished) break;

            yield return null;
        }

        // --- Toparlanma ve saldırı ---
        if (frozen) LockPlayer(false);

        // Hâlâ sinematik kameradaysak oyuncunun kamerasına dön.
        SetCutsceneCamera(false);
        if (playerLook != null && !playerLook.enabled)
        {
            // Kamerayı biz kestik; PlayerLook açılmadan önce açılarını pivottan okusun.
            playerLook.SyncFromPivot();
            playerLook.enabled = true;
        }

        PlaySfx(chargeClip);
        onAmbushStart?.Invoke();

        yield return StartCoroutine(ReleaseEnemies());

        _isPlaying = false;
    }

    /// <summary>Hırtları saldırıya salar; staggerBetweenEnemies varsa sırayla uyanırlar.</summary>
    private IEnumerator ReleaseEnemies()
    {
        if (enemies == null) yield break;

        foreach (EnemyFollow enemy in enemies)
        {
            if (enemy == null) continue;

            enemy.BeginChase();

            if (staggerBetweenEnemies > 0f)
                yield return new WaitForSeconds(staggerBetweenEnemies);
        }
    }

    /// <summary>Tüm hırtları anında oyuncuya döndürür (henüz saldırmazlar).</summary>
    private void SnapAllToPlayer()
    {
        if (enemies == null) return;

        foreach (EnemyFollow enemy in enemies)
        {
            if (enemy != null) enemy.SnapToTarget();
        }
    }

    /// <summary>
    /// Islık animasyonunun state'e girip bitmesini bekler. Loop'a giren ya da hiç
    /// tetiklenmeyen animasyonlarda animationTimeout devreye girer.
    /// </summary>
    private IEnumerator WaitForWhistleAnimation()
    {
        float deadline = Time.time + animationTimeout;
        bool checkName = !string.IsNullOrEmpty(whistleStateName);

        // 1) Trigger'dan sonra state'e geçiş birkaç frame sürebilir; girişi bekle.
        while (Time.time < deadline)
        {
            if (!whistlerAnimator.IsInTransition(whistleLayer))
            {
                AnimatorStateInfo info = whistlerAnimator.GetCurrentAnimatorStateInfo(whistleLayer);
                if (!checkName || info.IsName(whistleStateName)) break;
            }
            yield return null;
        }

        // 2) State'in sonunu (normalizedTime >= 1) ya da başka state'e çıkışı bekle.
        while (Time.time < deadline)
        {
            AnimatorStateInfo info = whistlerAnimator.GetCurrentAnimatorStateInfo(whistleLayer);
            if (checkName && !info.IsName(whistleStateName)) break;
            if (!whistlerAnimator.IsInTransition(whistleLayer) && info.normalizedTime >= 1f) break;
            yield return null;
        }

        _animationFinished = true;
    }

    /// <summary>Oyuncu hareket/saldırı kontrolünü açar-kapatır.</summary>
    private bool LockPlayer(bool locked)
    {
        if (_playerController != null) _playerController.enabled = !locked;

        // Kilitlenirken yatay hızı sıfırla ki oyuncu kayarak gitmesin.
        if (locked && _playerRb != null)
            _playerRb.linearVelocity = new Vector3(0f, _playerRb.linearVelocity.y, 0f);

        return locked;
    }

    /// <summary>Sinematik kamera akışı: cutInDelay sonra keser, cutDuration kadar kalır.</summary>
    private IEnumerator CutsceneRoutine()
    {
        if (cutInDelay > 0f) yield return new WaitForSeconds(cutInDelay);

        SetCutsceneCamera(true);

        if (cutDuration > 0f)
        {
            yield return new WaitForSeconds(cutDuration);
            SetCutsceneCamera(false);
        }
    }

    /// <summary>
    /// Oyuncunun kamerası ile sinematik kamera arasında geçiş yapar. Aynı anda tek
    /// kamera render eder; AudioListener oyuncunun kamerasında kalır.
    /// </summary>
    private void SetCutsceneCamera(bool active)
    {
        if (cutsceneCamera == null || _cutsceneActive == active) return;
        _cutsceneActive = active;

        if (active)
        {
            cutsceneCamera.gameObject.SetActive(true);
            cutsceneCamera.enabled = true;
            if (playerCamera != null) playerCamera.enabled = false;
        }
        else
        {
            if (playerCamera != null) playerCamera.enabled = true;
            cutsceneCamera.enabled = false;
            cutsceneCamera.gameObject.SetActive(false);
        }
    }

    /// <summary>Verilen kamerayı localPosition offset'iyle sarsar, sonra eski yerine koyar.</summary>
    private IEnumerator ShakeRoutine(Transform shakeTarget)
    {
        Vector3 original = shakeTarget.localPosition;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            // Şiddet zamanla sönümlensin.
            float damper = 1f - (elapsed / shakeDuration);
            shakeTarget.localPosition = original + Random.insideUnitSphere * (shakeStrength * damper);

            elapsed += Time.deltaTime;
            yield return null;
        }

        shakeTarget.localPosition = original;
    }

    /// <summary>Trigger'ı yalnızca controller'da gerçekten varsa tetikler.</summary>
    private void SetTriggerSafe(Animator target, string parameter)
    {
        foreach (var p in target.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == parameter)
            {
                target.SetTrigger(parameter);
                return;
            }
        }

        Debug.LogWarning($"{name}: '{parameter}' parametresi {target.name} controller'inda yok. " +
                         "Tools/Enemy/Create Enemy Animator calistir; islik animasyonu olmadan akis devam eder.", target);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, sfxVolume);
    }

    /// <summary>Test/akış için pusuyu dışarıdan tetikler.</summary>
    public void TriggerManually(Transform player)
    {
        if (_isPlaying || (triggerOnce && _hasTriggered)) return;

        _hasTriggered = true;
        if (player != null)
        {
            _playerController = player.GetComponent<PlayerController>();
            _playerRb = player.GetComponent<Rigidbody>();
        }
        StartCoroutine(AmbushRoutine());
    }

    private void OnDrawGizmos()
    {
        // Trigger alanını ve hırtlara giden bağları sahnede göster.
        var col = GetComponent<Collider>();
        if (col != null)
        {
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.25f);
            Gizmos.DrawCube(col.bounds.center, col.bounds.size);
        }

        if (enemies == null) return;

        foreach (EnemyFollow enemy in enemies)
        {
            if (enemy == null) continue;

            // Islıkçıya giden bağ farklı renkte.
            Gizmos.color = enemy == whistler ? Color.cyan : new Color(1f, 0.6f, 0f);
            Gizmos.DrawLine(transform.position, enemy.transform.position);
        }
    }
}
