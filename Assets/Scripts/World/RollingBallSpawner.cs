using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Gökyüzündeki bir kutunun içinde rastgele noktalarda top doğurur; toplar düşüp
/// yokuş aşağı yuvarlanarak oyuncuyu ezmeye çalışır. Hasarı topun üzerindeki
/// DamageSource verir, temizliği RollingBall yapar; burada yalnızca "ne zaman,
/// nerede, kaç tane" sorusu çözülür.
///
/// Kurulum:
///   1) Yokuşun TEPESİNE, gökyüzüne boş bir GameObject koy (bu script + BoxCollider).
///   2) BoxCollider'ı doğma alanı olacak şekilde büyüt ve "Is Trigger" işaretle
///      (trigger değilse doğan toplar kutunun kendisine çarpar).
///   3) ballPrefab'a top prefab'ını ata (Tools/World/Create Rolling Ball üretir).
///   4) Başlatmayı BallSpawnTrigger'a bırak; "Spawn On Start" kapalı kalsın.
///
/// Doğan toplar sahne köküne bırakılır: spawner'ın ölçeği/dönüşü topları bozmasın.
/// </summary>
[DisallowMultipleComponent]
public class RollingBallSpawner : MonoBehaviour
{
    [Header("Doğma alanı")]
    [Tooltip("Topların doğacağı kutu. Boşsa bu objedeki BoxCollider kullanılır.")]
    [SerializeField] private BoxCollider spawnArea;

    [Header("Top")]
    [Tooltip("Doğurulacak top prefab'ı. Rigidbody + Collider + DamageSource içermeli.")]
    [SerializeField] private GameObject ballPrefab;
    [Tooltip("Her topun ölçeği bu aralıkta rastgele çarpanla değişir (x = en küçük, y = en büyük). 1-1 = hepsi aynı.")]
    [SerializeField] private Vector2 scaleRange = new Vector2(0.8f, 1.6f);
    [Tooltip("Açıksa her top rastgele bir açıyla doğar.")]
    [SerializeField] private bool randomRotation = true;

    [Header("Akış")]
    [Tooltip("Açıksa oyun başlar başlamaz doğurmaya başlar. Tetikleyici kullanıyorsan KAPALI olmalı.")]
    [SerializeField] private bool spawnOnStart = false;
    [Tooltip("Başlatıldıktan sonra ilk topun düşmesi için beklenecek süre.")]
    [SerializeField] private float startDelay = 0f;
    [Tooltip("İki dalga arasındaki süre (saniye).")]
    [SerializeField] private float interval = 0.6f;
    [Tooltip("Her dalgada kaç top düşsün.")]
    [SerializeField] private int burstCount = 1;
    [Tooltip("Toplam kaç top doğurulacak. 0 = sınırsız (süre veya StopSpawning ile biter).")]
    [SerializeField] private int totalBalls = 0;
    [Tooltip("Kaç saniye sonra kendiliğinden dursun. 0 = süresiz.")]
    [SerializeField] private float duration = 0f;
    [Tooltip("Açıksa oyuncu ölünce doğurma durur (ölüm ekranında top yağmasın).")]
    [SerializeField] private bool stopWhenPlayerDies = true;

    [Header("Sınır")]
    [Tooltip("Aynı anda sahnede duracak en fazla top. Dolu olduğunda dalga atlanır (0 = sınırsız).")]
    [SerializeField] private int maxAlive = 25;

    [Header("Fırlatma")]
    [Tooltip("Doğan topa verilecek ilk hız. Sadece düşsün istiyorsan sıfır bırak; yokuşa doğru itmek için Z ver.")]
    [SerializeField] private Vector3 launchVelocity = Vector3.zero;
    [Tooltip("Açıksa hız spawner'ın kendi eksenlerine göre uygulanır (objeyi döndürünce yön de döner).")]
    [SerializeField] private bool launchInLocalSpace = true;
    [Tooltip("İlk hıza eklenecek rastgele sapma (her eksen için ± bu değer).")]
    [SerializeField] private float velocityJitter = 0.5f;
    [Tooltip("Doğarken verilecek rastgele dönüş (rad/s). Toplar havada dönerek insin.")]
    [SerializeField] private float randomSpin = 2f;

    [Header("Olaylar")]
    [Tooltip("Doğurma başladığı anda tetiklenir (müzik, uyarı sesi, UI).")]
    public UnityEvent onSpawnStart;
    [Tooltip("Doğurma bittiğinde tetiklenir.")]
    public UnityEvent onSpawnEnd;

    private readonly List<GameObject> _alive = new List<GameObject>();
    private PlayerHealth _playerHealth;
    private Coroutine _routine;
    private int _spawnedCount;

    /// <summary>Şu anda top doğuruyor mu? (Tetikleyici/UI okuyabilir.)</summary>
    public bool IsSpawning => _routine != null;

    /// <summary>Bu spawner'ın şu ana kadar doğurduğu toplam top sayısı.</summary>
    public int SpawnedCount => _spawnedCount;

    private void Awake()
    {
        if (spawnArea == null) spawnArea = GetComponent<BoxCollider>();

        if (spawnArea == null)
            Debug.LogWarning($"{name}: Dogma alani yok. Bu objeye BoxCollider ekle ya da " +
                             "spawnArea alanina bir BoxCollider ata.", this);
        else if (!spawnArea.isTrigger)
            Debug.LogWarning($"{name}: Dogma alani collider'i 'Is Trigger' olmali; " +
                             "yoksa dogan toplar kutunun kendisine carpar.", this);

        if (ballPrefab == null)
            Debug.LogWarning($"{name}: ballPrefab bos. Tools/World/Create Rolling Ball ile " +
                             "top prefab'i uretip buraya ata.", this);
    }

    private void Start()
    {
        if (stopWhenPlayerDies) _playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (spawnOnStart) BeginSpawning();
    }

    /// <summary>Doğurmayı başlatır. Zaten çalışıyorsa hiçbir şey yapmaz.</summary>
    public void BeginSpawning()
    {
        if (_routine != null) return;
        if (ballPrefab == null || spawnArea == null) return;

        _routine = StartCoroutine(SpawnRoutine());
    }

    /// <summary>Doğurmayı durdurur. Sahnedeki mevcut toplar yerinde kalır.</summary>
    public void StopSpawning()
    {
        if (_routine == null) return;

        StopCoroutine(_routine);
        _routine = null;
        onSpawnEnd?.Invoke();
    }

    /// <summary>Doğurmayı durdurup sahnedeki tüm topları temizler (oda sıfırlama).</summary>
    public void StopAndClear()
    {
        StopSpawning();

        foreach (GameObject ball in _alive)
        {
            if (ball == null) continue;

            var rollingBall = ball.GetComponent<RollingBall>();
            if (rollingBall != null) rollingBall.Despawn();
            else Destroy(ball);
        }

        _alive.Clear();
    }

    private IEnumerator SpawnRoutine()
    {
        if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

        onSpawnStart?.Invoke();

        float deadline = duration > 0f ? Time.time + duration : Mathf.Infinity;
        var wait = new WaitForSeconds(Mathf.Max(0.02f, interval));

        while (Time.time < deadline)
        {
            if (stopWhenPlayerDies && _playerHealth != null && _playerHealth.IsDead) break;

            for (int i = 0; i < burstCount; i++)
            {
                if (totalBalls > 0 && _spawnedCount >= totalBalls) break;
                if (!HasRoomForMore()) break;

                SpawnBall();
            }

            if (totalBalls > 0 && _spawnedCount >= totalBalls) break;

            yield return wait;
        }

        _routine = null;
        onSpawnEnd?.Invoke();
    }

    /// <summary>Yok edilmiş topları listeden düşürüp canlı sayısını sınırla karşılaştırır.</summary>
    private bool HasRoomForMore()
    {
        _alive.RemoveAll(ball => ball == null);
        return maxAlive <= 0 || _alive.Count < maxAlive;
    }

    private void SpawnBall()
    {
        Quaternion rotation = randomRotation ? Random.rotation : ballPrefab.transform.rotation;

        // Parent vermiyoruz: spawner'in olcegi topu deforme etmesin.
        GameObject ball = Instantiate(ballPrefab, RandomPointInArea(), rotation);

        float scale = Random.Range(scaleRange.x, scaleRange.y);
        if (!Mathf.Approximately(scale, 1f))
            ball.transform.localScale = ballPrefab.transform.localScale * scale;

        ApplyLaunch(ball);

        _alive.Add(ball);
        _spawnedCount++;
    }

    /// <summary>Kutunun içinde rastgele bir dünya noktası (kutu dönmüş olsa da doğru çalışır).</summary>
    private Vector3 RandomPointInArea()
    {
        Vector3 half = spawnArea.size * 0.5f;
        Vector3 local = spawnArea.center + new Vector3(
            Random.Range(-half.x, half.x),
            Random.Range(-half.y, half.y),
            Random.Range(-half.z, half.z));

        return spawnArea.transform.TransformPoint(local);
    }

    /// <summary>İlk hızı ve dönüşü uygular; Rigidbody yoksa sessizce atlanır.</summary>
    private void ApplyLaunch(GameObject ball)
    {
        Vector3 velocity = launchInLocalSpace
            ? transform.TransformDirection(launchVelocity)
            : launchVelocity;

        if (velocityJitter > 0f)
            velocity += new Vector3(
                Random.Range(-velocityJitter, velocityJitter),
                Random.Range(-velocityJitter, velocityJitter),
                Random.Range(-velocityJitter, velocityJitter));

        Vector3 spin = randomSpin > 0f ? Random.insideUnitSphere * randomSpin : Vector3.zero;

        var rollingBall = ball.GetComponent<RollingBall>();
        if (rollingBall != null)
        {
            rollingBall.Launch(velocity, spin);
            return;
        }

        var rb = ball.GetComponent<Rigidbody>();
        if (rb == null) return;

        rb.linearVelocity = velocity;
        rb.angularVelocity = spin;
    }

    private void OnDrawGizmos()
    {
        BoxCollider area = spawnArea != null ? spawnArea : GetComponent<BoxCollider>();
        if (area == null) return;

        // Kutuyu kendi ekseninde çiz; döndürülmüş spawner'da da doğru görünsün.
        Gizmos.matrix = area.transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.15f);
        Gizmos.DrawCube(area.center, area.size);
        Gizmos.color = new Color(1f, 0.85f, 0f);
        Gizmos.DrawWireCube(area.center, area.size);

        // Fırlatma yönünü kutunun merkezinden bir okla göster.
        if (launchVelocity.sqrMagnitude < 0.0001f) return;

        Gizmos.matrix = Matrix4x4.identity;
        Vector3 origin = area.transform.TransformPoint(area.center);
        Vector3 direction = launchInLocalSpace
            ? transform.TransformDirection(launchVelocity)
            : launchVelocity;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(origin, origin + direction);
    }
}
