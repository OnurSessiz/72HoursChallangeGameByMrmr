using UnityEngine;

/// <summary>
/// Basit düşman takip (chase) davranışı: hedef "detectionRange" mesafesine girince
/// takibe başlar, "stopDistance"a kadar yaklaşır ve hedefe döner. NavMesh gerektirmez.
/// Takibe başlama mesafesi Inspector'dan ayarlanabilir (public/SerializeField).
/// </summary>
public class EnemyFollow : MonoBehaviour
{
    [Header("Hedef")]
    [Tooltip("Takip edilecek hedef. Boşsa Start'ta 'Player' tag'li obje otomatik bulunur.")]
    [SerializeField] private Transform target;

    [Header("Mesafeler")]
    [Tooltip("Bu mesafeye girince takip başlar.")]
    [SerializeField] private float detectionRange = 10f;
    [Tooltip("Hedefe bu kadar yaklaşınca durur (üst üste binmeyi önler).")]
    [SerializeField] private float stopDistance = 1.5f;

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Attack (itme)")]
    [Tooltip("Oyuncu bu mesafedeyken saldırır (genelde stopDistance civarı).")]
    [SerializeField] private float attackRange = 2f;
    [Tooltip("İki saldırı arası minimum süre (saniye).")]
    [SerializeField] private float attackCooldown = 1.5f;
    [Tooltip("Oyuncuyu yatayda ne kadar sert iteceği.")]
    [SerializeField] private float knockbackForce = 8f;
    [Tooltip("İtmeye eklenen yukarı yön bileşeni (0 = düz itme).")]
    [SerializeField] private float knockbackUpward = 2f;
    [Tooltip("Her saldırıda verilecek hasar.")]
    [SerializeField] private float damage = 15f;

    [Header("Animasyon")]
    [Tooltip("Mobun Animator'ı. Boşsa bu objede ve child'larında aranır.")]
    [SerializeField] private Animator animator;
    [Tooltip("Idle <-> Walk geçişini süren float parametre (Enemy.controller ile aynı).")]
    [SerializeField] private string speedParameter = "Speed";
    [Tooltip("Hız değerinin yumuşatma süresi.")]
    [SerializeField] private float speedDampTime = 0.15f;

    private Rigidbody _targetRb;
    private PlayerHealth _targetHealth;
    private float _lastAttackTime = Mathf.NegativeInfinity;

    private Health _health;
    private int _speedHash;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        _health = GetComponent<Health>();
        _speedHash = Animator.StringToHash(speedParameter);
    }

    private void Start()
    {
        // Hedef atanmamışsa Player tag'li objeyi bul.
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        // Knockback ve hasar için hedefin bileşenlerini önbelleğe al.
        if (target != null)
        {
            _targetRb = target.GetComponent<Rigidbody>();
            _targetHealth = target.GetComponent<PlayerHealth>();
        }
    }

    private void Update()
    {
        // Kendi canımız bittiyse hiçbir şey yapma; ölüm animasyonu Health tarafından oynatılır.
        if (_health != null && _health.IsDead) return;

        if (target == null)
        {
            SetAnimatorSpeed(0f);
            return;
        }

        // Oyuncu öldüyse kovalamayı ve saldırmayı bırak.
        if (_targetHealth != null && _targetHealth.IsDead)
        {
            SetAnimatorSpeed(0f);
            return;
        }

        // Yatay düzlemde mesafe (yükseklik farkını yok say).
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        // Menzil dışındaysa takip etme.
        if (distance > detectionRange)
        {
            SetAnimatorSpeed(0f);
            return;
        }

        // Hedefe dön (tüm yönler, yumuşak).
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        // stopDistance'a kadar yaklaş, sonra dur. Speed parametresi Walk animasyonunu açar.
        if (distance > stopDistance)
        {
            Vector3 step = toTarget.normalized * (moveSpeed * Time.deltaTime);
            transform.position += step;
            SetAnimatorSpeed(1f);
        }
        else
        {
            SetAnimatorSpeed(0f);
        }

        // Menzildeyse ve cooldown dolmuşsa saldır (oyuncuyu it).
        if (distance <= attackRange && Time.time >= _lastAttackTime + attackCooldown)
            Attack(toTarget.normalized);
    }

    /// <summary>Oyuncuyu düşmandan uzağa iter (geçici attack).</summary>
    private void Attack(Vector3 horizontalDir)
    {
        _lastAttackTime = Time.time;

        // Hasar ver (hit animasyonu ve dodge i-frame kontrolü PlayerHealth içinde).
        if (_targetHealth != null) _targetHealth.TakeDamage(damage);

        if (_targetRb == null) return;

        // Yatay itme + isteğe bağlı yukarı bileşen.
        Vector3 force = horizontalDir * knockbackForce + Vector3.up * knockbackUpward;
        _targetRb.AddForce(force, ForceMode.Impulse);
    }

    /// <summary>Animator'daki hız parametresini yumuşatarak yazar (Idle &lt;-&gt; Walk).</summary>
    private void SetAnimatorSpeed(float value)
    {
        if (animator == null) return;
        animator.SetFloat(_speedHash, value, speedDampTime, Time.deltaTime);
    }

    /// <summary>Devre dışı kalınca (ör. ölümde Health kapatır) yürüyüş animasyonu asılı kalmasın.</summary>
    private void OnDisable()
    {
        if (animator != null) animator.SetFloat(_speedHash, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        // Algılama menzili ve durma mesafesini sahnede göster.
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, stopDistance);
    }
}