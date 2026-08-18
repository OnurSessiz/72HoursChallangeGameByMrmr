using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Oyunun kapanış sekansı. Son adım (varsayılan: RockTrap) tamamlanınca oyuncunun
/// kontrolü kesilir, kameralar sırayla gösterilir, sonra görüntü oyuncuya döner ve
/// returnHold saniye sonra oyun biter.
///
/// Akış:
///   1) GameProgress -> StageCompleted(triggerStage) yakalanır
///   2) Oyuncu dondurulur, top yağmuru durdurulur
///   3) shots listesindeki kameralar sırayla açılır (her biri kendi süresi kadar)
///   4) Oyuncunun kamerasına dönülür, PlayerLook geri açılır
///   5) returnHold beklenir -> onGameEnd + endAction
///
/// Kurulum: boş bir GameObject'e ekle, shots listesine sırayla HirtRoomCam, FinalCam
/// ve BossCam'i sürükle. Kameraları sahnede istediğin açıya yerleştir; sekans
/// başlayana kadar hepsi kapalı tutulur (playOnAwake gibi elle kapatmana gerek yok).
///
/// Not: kamera "MainCamera" tag'liyse Camera.main'i çalar; kapanış kameralarının
/// tag'ini Untagged bırak.
/// </summary>
[DisallowMultipleComponent]
public class EndingSequence : MonoBehaviour
{
    /// <summary>Sekansın kapanışta ne yapacağı.</summary>
    public enum EndAction
    {
        /// <summary>Hiçbir şey yapma; kapanışı onGameEnd'e bağladığın UI/ses halleder.</summary>
        OnlyEvent = 0,
        /// <summary>Oyunu kapat (Editor'de Play modundan çıkar).</summary>
        QuitGame = 1,
        /// <summary>Belirtilen sahneyi yükle (menü, jenerik).</summary>
        LoadScene = 2,
        /// <summary>Mevcut sahneyi baştan yükle.</summary>
        ReloadScene = 3
    }

    /// <summary>Sekanstaki tek bir kamera çekimi.</summary>
    [Serializable]
    public class CameraShot
    {
        [Tooltip("Bu çekimde ekranda olacak kamera.")]
        public Camera camera;
        [Tooltip("Kaç saniye bu kamerada kalınacak.")]
        public float duration = 2.5f;
        [Tooltip("Açıksa kamera çekim boyunca oyuncuya bakar (sabit açı istiyorsan kapalı bırak).")]
        public bool lookAtPlayer = false;
        [Tooltip("Çekim başladığı anda tetiklenir (müzik, altyazı, UI).")]
        public UnityEvent onShotStart;
    }

    [Header("Tetikleyici")]
    [Tooltip("Bu adım tamamlanınca kapanış sekansı başlar.")]
    [SerializeField] private GameStage triggerStage = GameStage.RockTrap;
    [Tooltip("Adım bittikten sonra sekansın başlaması için beklenecek süre.")]
    [SerializeField] private float startDelay = 0.5f;

    [Header("Çekimler")]
    [Tooltip("Sırayla gösterilecek kameralar. Sıralama: HirtRoomCam -> FinalCam -> BossCam.")]
    [SerializeField] private CameraShot[] shots;
    [Tooltip("lookAtPlayer açık çekimlerde kameranın oyuncunun kaç metre üstüne bakacağı (göğüs/kafa hizası).")]
    [SerializeField] private float lookAtHeight = 1.4f;

    [Header("Kapanış")]
    [Tooltip("Kamera oyuncuya döndükten sonra oyunun bitmesi için beklenecek süre.")]
    [SerializeField] private float returnHold = 2f;
    [Tooltip("Bekleme bitince ne olacak.")]
    [SerializeField] private EndAction endAction = EndAction.QuitGame;
    [Tooltip("endAction = LoadScene ise yüklenecek sahnenin adı (Build Settings'e ekli olmalı).")]
    [SerializeField] private string endSceneName = "";

    [Header("Oyuncu")]
    [Tooltip("Açıksa sekans boyunca oyuncunun kontrolü kapanır.")]
    [SerializeField] private bool freezePlayer = true;
    [Tooltip("Açıksa kamera oyuncuya dönünce kontrol geri verilir (kapanışa kadar oynayabilir).")]
    [SerializeField] private bool unfreezeOnReturn = false;
    [Tooltip("Oyuncunun kamerası. Boşsa Start'ta Camera.main'den alınır.")]
    [SerializeField] private Camera playerCamera;
    [Tooltip("Sekans boyunca kapatılacak PlayerLook. Boşsa sahnede aranır.")]
    [SerializeField] private PlayerLook playerLook;

    [Header("Temizlik")]
    [Tooltip("Açıksa sekans başlarken kaya tuzağı durur ve havadaki toplar temizlenir " +
             "(kapanış sırasında oyuncunun kafasına top düşmesin).")]
    [SerializeField] private bool stopBallSpawners = true;

    [Header("Olaylar")]
    [Tooltip("Sekans başladığı anda tetiklenir (müzik değişimi, HUD'u gizleme).")]
    public UnityEvent onSequenceStart;
    [Tooltip("Kamera oyuncuya döndüğü anda tetiklenir.")]
    public UnityEvent onReturnToPlayer;
    [Tooltip("Oyun bittiği anda tetiklenir (kazandın ekranı, jenerik). endAction'dan ÖNCE çalışır.")]
    public UnityEvent onGameEnd;

    private GameProgress _progress;
    private PlayerHealth _playerHealth;
    private PlayerController _playerController;
    private Rigidbody _playerRb;
    private bool _playing;

    /// <summary>Kapanış sekansı oynuyor mu?</summary>
    public bool IsPlaying => _playing;

    private void Awake()
    {
        // Kapanis kameralari sekansa kadar kapali kalsin; sahnede acik unutulmus olabilirler.
        if (shots == null) return;

        foreach (CameraShot shot in shots)
        {
            if (shot?.camera == null) continue;

            // Iki AudioListener acik kalirsa Unity uyarir ve ses bozulur.
            var listener = shot.camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;

            if (shot.camera.CompareTag("MainCamera"))
                Debug.LogWarning($"{name}: '{shot.camera.name}' 'MainCamera' tag'li. Camera.main'i " +
                                 "calar; tag'ini Untagged yap.", shot.camera);

            shot.camera.enabled = false;
            shot.camera.gameObject.SetActive(false);
        }
    }

    private void Start()
    {
        // Oyuncunun kamerasini kesmeden ONCE cozumle: kesme sirasinda kapali oldugu
        // icin Camera.main null'a duser.
        if (playerCamera == null) playerCamera = Camera.main;
        if (playerLook == null) playerLook = FindAnyObjectByType<PlayerLook>();
        _playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (_playerHealth != null)
        {
            _playerController = _playerHealth.GetComponent<PlayerController>();
            _playerRb = _playerHealth.GetComponent<Rigidbody>();
        }

        _progress = GameProgress.Instance;
        _progress.StageCompleted += OnStageCompleted;

        if (shots == null || shots.Length == 0)
            Debug.LogWarning($"{name}: Cekim listesi bos; kapanista sadece bekleyip oyun biter. " +
                             "shots listesine kameralari sirayla surukle.", this);
    }

    private void OnDestroy()
    {
        if (_progress != null) _progress.StageCompleted -= OnStageCompleted;
    }

    private void OnStageCompleted(GameStage stage)
    {
        if (stage != triggerStage || _playing) return;
        StartCoroutine(PlayRoutine());
    }

    /// <summary>Sekansı dışarıdan başlatır (test, cutscene, UnityEvent).</summary>
    public void Play()
    {
        if (!_playing) StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        _playing = true;

        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        onSequenceStart?.Invoke();

        if (stopBallSpawners) StopAllBallSpawners();
        if (freezePlayer) LockPlayer(true);

        // Kamerayi devraliyoruz: PlayerLook her LateUpdate'te rotasyonu mutlak yazdigi
        // icin acik kalirsa cekimler bozulur.
        if (playerLook != null) playerLook.enabled = false;

        // --- Cekimler ---
        if (shots != null)
        {
            foreach (CameraShot shot in shots)
            {
                if (shot?.camera == null) continue;

                SetActiveCamera(shot.camera);
                shot.onShotStart?.Invoke();

                yield return StartCoroutine(HoldShot(shot));
            }
        }

        // --- Oyuncuya donus ---
        SetActiveCamera(null);   // kapanis kameralarini kapat, oyuncununkini ac

        if (playerLook != null)
        {
            // Kamerayi biz kestik; PlayerLook acilmadan once acilarini pivottan okusun,
            // yoksa kontrol geri gelince kamera eski acisina ziplar.
            playerLook.SyncFromPivot();
            playerLook.enabled = true;
        }

        if (freezePlayer && unfreezeOnReturn) LockPlayer(false);

        onReturnToPlayer?.Invoke();

        if (returnHold > 0f) yield return new WaitForSeconds(returnHold);

        // --- Kapanis ---
        onGameEnd?.Invoke();
        RunEndAction();
    }

    /// <summary>Bir çekimi süresi boyunca tutar; gerekiyorsa kamerayı oyuncuya çevirir.</summary>
    private IEnumerator HoldShot(CameraShot shot)
    {
        float elapsed = 0f;

        while (elapsed < shot.duration)
        {
            if (shot.lookAtPlayer && _playerHealth != null)
                shot.camera.transform.LookAt(_playerHealth.transform.position + Vector3.up * lookAtHeight);

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// Aynı anda tek kamera render etsin diye hepsini kapatıp verileni açar.
    /// null verilirse oyuncunun kamerasına dönülür. AudioListener oyuncunun kamerasında kalır.
    /// </summary>
    private void SetActiveCamera(Camera target)
    {
        if (shots != null)
        {
            foreach (CameraShot shot in shots)
            {
                if (shot?.camera == null || shot.camera == target) continue;

                shot.camera.enabled = false;
                shot.camera.gameObject.SetActive(false);
            }
        }

        if (playerCamera != null) playerCamera.enabled = target == null;

        if (target == null) return;

        target.gameObject.SetActive(true);
        target.enabled = true;
    }

    /// <summary>Oyuncu hareket/saldırı kontrolünü açar-kapatır.</summary>
    private void LockPlayer(bool locked)
    {
        if (_playerController != null) _playerController.enabled = !locked;

        // Kilitlenirken yatay hizi sifirla ki oyuncu kayarak gitmesin.
        if (locked && _playerRb != null)
            _playerRb.linearVelocity = new Vector3(0f, _playerRb.linearVelocity.y, 0f);
    }

    /// <summary>Kapanış sırasında kafaya top düşmesin diye tüm tuzakları durdurur.</summary>
    private void StopAllBallSpawners()
    {
        foreach (RollingBallSpawner spawner in FindObjectsByType<RollingBallSpawner>(FindObjectsInactive.Include))
            spawner.StopAndClear();
    }

    private void RunEndAction()
    {
        switch (endAction)
        {
            case EndAction.LoadScene:
                if (string.IsNullOrEmpty(endSceneName))
                {
                    Debug.LogWarning($"{name}: endAction = LoadScene ama sahne adi bos.", this);
                    return;
                }
                SceneManager.LoadScene(endSceneName);
                return;

            case EndAction.ReloadScene:
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;

            case EndAction.QuitGame:
#if UNITY_EDITOR
                // Editor'de Application.Quit hicbir sey yapmaz; Play modundan cikiyoruz.
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                return;

            default:
                return;   // OnlyEvent: kapanisi onGameEnd halleder
        }
    }

    private void OnDrawGizmosSelected()
    {
        // Cekim kameralarina giden baglari sahnede goster.
        if (shots == null) return;

        Gizmos.color = new Color(0.4f, 0.8f, 1f);
        foreach (CameraShot shot in shots)
        {
            if (shot?.camera != null) Gizmos.DrawLine(transform.position, shot.camera.transform.position);
        }
    }
}
