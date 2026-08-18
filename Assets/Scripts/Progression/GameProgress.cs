using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Oyunun ilerleme adımları. Sıra bu enum'daki sıradır; araya adım eklersen
/// sondan eklemeye dikkat et (Inspector'daki seçimler indekse göre kaydedilir).
/// </summary>
public enum GameStage
{
    /// <summary>1) Hırt odasını temizle.</summary>
    HirtRoom = 0,
    /// <summary>2) Okyanus odasında BlackSwordsman'ı kes.</summary>
    Swordsman = 1,
    /// <summary>3) Boss'u yen.</summary>
    Boss = 2,
    /// <summary>4) Kaya tuzağını tırmanarak geç.</summary>
    RockTrap = 3
}

/// <summary>
/// Oyunun ilerleme otoritesi. Hangi adımın bittiğini yalnızca burası bilir;
/// kapılar (StageGate) ve hedefler (EnemyClearObjective / ReachPointObjective)
/// bu tek kaynağa bakar.
///
/// Akış: bir hedef tamamlanınca CompleteStage(...) çağırır -> ilgili adım işaretlenir
/// -> StageCompleted event'i yayılır -> o adımı bekleyen kapılar açılır.
///
/// Sıra zorunluluğu: enforceOrder açıkken bir adım, kendinden öncekiler bitmeden
/// tamamlanmış sayılmaz (odalar collider'la kilitli olsa bile ikinci bir güvenlik).
///
/// Kurulum: sahnede tek bir boş GameObject'e ekle (Tools/Progression/Setup Game Flow
/// bunu senin yerine yapar). Sahnede hiç yoksa ilk erişimde kendisi oluşur.
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class GameProgress : MonoBehaviour
{
    [Header("Kurallar")]
    [Tooltip("Açıksa bir adım, kendinden öncekiler bitmeden tamamlanmış sayılmaz.")]
    [SerializeField] private bool enforceOrder = true;
    [Tooltip("Açıksa oyuncu ölüp sahne yeniden yüklenince ilerleme korunur (odalar açık kalır). " +
             "Kapalıysa her ölümde oyun baştan başlar.")]
    [SerializeField] private bool keepProgressOnRestart = false;
    [Tooltip("Açıksa her adım Console'a yazılır; akışı test ederken faydalı.")]
    [SerializeField] private bool logProgress = true;

    [Header("Olaylar")]
    [Tooltip("Herhangi bir adım tamamlanınca tetiklenir (ses, UI, kayıt).")]
    public UnityEvent onStageCompleted;
    [Tooltip("Son adım da bitince tetiklenir (kazanma ekranı, jenerik).")]
    public UnityEvent onAllStagesComplete;

    /// <summary>Toplam adım sayısı (enum'dan okunur).</summary>
    public static readonly int StageCount = Enum.GetValues(typeof(GameStage)).Length;

    // Sahne yeniden yüklenince ilerlemeyi tasimak icin; keepProgressOnRestart acikken kullanilir.
    private static bool[] _savedStages;
    private static GameProgress _instance;

    private bool[] _completed;

    /// <summary>
    /// Sahnedeki ilerleme otoritesi. Sahnede yoksa kendiliğinden oluşturulur,
    /// böylece kapı/hedef script'leri hiçbir zaman null ile uğraşmaz.
    /// </summary>
    public static GameProgress Instance
    {
        get
        {
            if (_instance != null) return _instance;

            _instance = FindAnyObjectByType<GameProgress>();
            if (_instance == null)
            {
                Debug.LogWarning("[GameProgress] Sahnede GameProgress yok; otomatik olusturuldu. " +
                                 "Ayarlarini kontrol edebilmek icin Tools/Progression/Setup Game Flow calistir.");
                _instance = new GameObject("GameProgress").AddComponent<GameProgress>();
            }

            return _instance;
        }
    }

    /// <summary>Sıradaki (ilk tamamlanmamış) adım. Hepsi bittiyse son adımı verir.</summary>
    public GameStage CurrentStage
    {
        get
        {
            for (int i = 0; i < _completed.Length; i++)
                if (!_completed[i]) return (GameStage)i;

            return (GameStage)(StageCount - 1);
        }
    }

    /// <summary>Tüm adımlar bitti mi?</summary>
    public bool AllComplete
    {
        get
        {
            foreach (bool done in _completed)
                if (!done) return false;

            return true;
        }
    }

    /// <summary>Tamamlanan adım sayısı (UI ilerleme çubuğu için).</summary>
    public int CompletedCount
    {
        get
        {
            int count = 0;
            foreach (bool done in _completed)
                if (done) count++;

            return count;
        }
    }

    /// <summary>Bir adım tamamlandığı anda yayılır. Kapılar buna abone olur.</summary>
    public event Action<GameStage> StageCompleted;

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning($"{name}: Sahnede birden fazla GameProgress var; bu kopya siliniyor.", this);
            Destroy(this);
            return;
        }

        _instance = this;
        _completed = new bool[StageCount];

        // Olum sonrasi sahne yeniden yuklendiginde ilerlemeyi geri yukle.
        if (keepProgressOnRestart && _savedStages != null && _savedStages.Length == StageCount)
            Array.Copy(_savedStages, _completed, StageCount);
        else
            _savedStages = null;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    /// <summary>Verilen adım tamamlandı mı?</summary>
    public bool IsStageComplete(GameStage stage)
    {
        int index = (int)stage;
        return index >= 0 && index < _completed.Length && _completed[index];
    }

    /// <summary>Bu adım şu anda oynanabilir mi? (Öncekiler bitmiş ve kendisi bitmemiş.)</summary>
    public bool IsStageAvailable(GameStage stage)
    {
        if (IsStageComplete(stage)) return false;
        return ArePreviousComplete(stage);
    }

    /// <summary>
    /// Adımı tamamlanmış işaretler. Hedef script'leri (EnemyClearObjective,
    /// ReachPointObjective) burayı çağırır. Zaten bitmiş adım sessizce yok sayılır.
    /// </summary>
    public void CompleteStage(GameStage stage)
    {
        int index = (int)stage;
        if (index < 0 || index >= _completed.Length) return;
        if (_completed[index]) return;

        if (enforceOrder && !ArePreviousComplete(stage))
        {
            Debug.LogWarning($"[GameProgress] {stage} adimi sirasi gelmeden tamamlanmaya calisildi; " +
                             $"once {CurrentStage} bitmeli. (Kapilarin kilidini kontrol et.)", this);
            return;
        }

        _completed[index] = true;
        SaveForRestart();

        if (logProgress)
            Debug.Log($"[GameProgress] Adim tamamlandi: {stage} ({CompletedCount}/{StageCount})", this);

        StageCompleted?.Invoke(stage);
        onStageCompleted?.Invoke();

        if (AllComplete)
        {
            if (logProgress) Debug.Log("[GameProgress] Tum adimlar tamamlandi.", this);
            onAllStagesComplete?.Invoke();
        }
    }

    /// <summary>UnityEvent'ten çağırmak için (Inspector int alanı: 0=HirtRoom ... 3=RockTrap).</summary>
    public void CompleteStage(int stageIndex) => CompleteStage((GameStage)stageIndex);

    /// <summary>İlerlemeyi sıfırlar (yeni oyun / test).</summary>
    public void ResetProgress()
    {
        _completed = new bool[StageCount];
        _savedStages = null;

        if (logProgress) Debug.Log("[GameProgress] Ilerleme sifirlandi.", this);
    }

    /// <summary>Test için: sıradaki adımı bitmiş say (Inspector'daki context menü).</summary>
    [ContextMenu("Siradaki adimi tamamla")]
    private void CompleteCurrentStage()
    {
        if (!AllComplete) CompleteStage(CurrentStage);
    }

    private bool ArePreviousComplete(GameStage stage)
    {
        for (int i = 0; i < (int)stage; i++)
            if (!_completed[i]) return false;

        return true;
    }

    private void SaveForRestart()
    {
        if (!keepProgressOnRestart)
        {
            _savedStages = null;
            return;
        }

        _savedStages = new bool[StageCount];
        Array.Copy(_completed, _savedStages, StageCount);
    }
}
