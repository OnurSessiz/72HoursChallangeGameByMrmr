using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Mob davranışı: hedefi kovalar (Walk animasyonu), menzile girince saldırı
/// animasyonunu oynatır ve vuruş anında hasar + knockback uygular. NavMesh gerektirmez.
///
/// Uyanık/uykuda ayrımı var:
///   - activateOnStart AÇIK  : oyuncu detectionRange'e girince kendiliğinden uyanır
///   - activateOnStart KAPALI: ıslık çalınana kadar hiç kıpırdamaz (HirtRoomAmbush -> BeginChase)
/// Uyandıktan sonra detectionRange umurunda değildir; oyuncuyu nereye giderse kovalar.
///
/// Animator sözleşmesi (EnemyAnimatorSetup ile aynı):
///   Speed   : float   -> Idle &lt;-&gt; Walk geçişi
///   Attack1 : trigger -> saldırı animasyonu (Any State üzerinden)
///   Hit/Die : trigger -> Health tarafından tetiklenir
/// Parametre controller'da yoksa sessizce atlanır.
/// </summary>
[DisallowMultipleComponent]
public class EnemyFollow : MonoBehaviour
{
    [Header("Hedef")]
    [Tooltip("Takip edilecek hedef. Boşsa Start'ta 'Player' tag'li obje otomatik bulunur.")]
    [SerializeField] private Transform target;

    [Header("Başlangıç")]
    [Tooltip("Açıksa mob oyuncuyu kendisi fark eder (detectionRange). Pusuda bekleyecekse KAPAT: ıslığa kadar hiç kıpırdamaz.")]
    [SerializeField] private bool activateOnStart = true;
    [Tooltip("Uyanınca oyuncuya anında dönsün mü? (Pusu anında hepsi birden dönsün diye.)")]
    [SerializeField] private bool snapToTargetOnActivate = true;

    [Header("Mesafeler")]
    [Tooltip("Oyuncu bu mesafeye girince mob kendiliğinden uyanır. UYANDIKTAN SONRA mesafe fark etmez; oyuncuyu odanın öbür ucundan da kovalar.")]
    [SerializeField] private float detectionRange = 10f;
    [Tooltip("Hedefe bu kadar yaklaşınca durur (üst üste binmeyi önler).")]
    [SerializeField] private float stopDistance = 1.5f;

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float rotationSpeed = 8f;

    [Header("Saldırı")]
    [Tooltip("Oyuncu bu mesafedeyken saldırı başlatır (genelde stopDistance civarı).")]
    [SerializeField] private float attackRange = 2f;
    [Tooltip("İki saldırı arası minimum süre (saniye).")]
    [SerializeField] private float attackCooldown = 1.5f;
    [Tooltip("Saldırı animasyonunun toplam süresi; bu süre boyunca mob yerinde durur.")]
    [SerializeField] private float attackDuration = 1f;
    [Tooltip("Saldırı başladıktan kaç saniye sonra hasar uygulanacak (animasyondaki vuruş anı).")]
    [SerializeField] private float hitDelay = 0.4f;
    [Tooltip("Vuruş anında oyuncu bu mesafedeyse hasar alır; uzaklaştıysa ıskalar.")]
    [SerializeField] private float hitRange = 2.5f;
    [Tooltip("Her saldırıda verilecek hasar.")]
    [SerializeField] private float damage = 15f;
    [Tooltip("Oyuncuyu yatayda ne kadar sert iteceği.")]
    [SerializeField] private float knockbackForce = 8f;
    [Tooltip("İtmeye eklenen yukarı yön bileşeni (0 = düz itme).")]
    [SerializeField] private float knockbackUpward = 2f;

    [Header("Animasyon")]
    [Tooltip("Mobun Animator'ı. Boşsa bu objede ve child'larında aranır.")]
    [SerializeField] private Animator animator;
    [Tooltip("Idle <-> Walk geçişini süren float parametre (Enemy.controller ile aynı).")]
    [SerializeField] private string speedParameter = "Speed";
    [Tooltip("Hız değerinin yumuşatma süresi.")]
    [SerializeField] private float speedDampTime = 0.15f;
    [Tooltip("Saldırı animasyonunun trigger adı. Controller'da yoksa sessizce atlanır.")]
    [SerializeField] private string attackTrigger = "Attack1";
    [Tooltip("Açıksa vuruş anı hitDelay yerine animasyondaki AttackHit event'inden gelir. Event'i olmayan kliplerde hitDelay yedek olarak devreye girer.")]
    [SerializeField] private bool useAnimationEventHit = false;

    [Header("Olaylar")]
    [Tooltip("Mob uyanıp kovalamaya başladığında tetiklenir (ses, efekt).")]
    public UnityEvent onActivated;
    [Tooltip("Her saldırı başlangıcında tetiklenir.")]
    public UnityEvent onAttack;

    private Rigidbody _targetRb;
    private PlayerHealth _targetHealth;
    private Health _health;
    private float _lastAttackTime = Mathf.NegativeInfinity;
    private int _speedHash;
    private bool _active;
    private bool _isAttacking;
    private bool _damageAppliedThisAttack;
    private Coroutine _attackRoutine;
    private bool _autoDetect;

    /// <summary>Mob uyandı mı? (Pusu script'i okuyabilir.)</summary>
    public bool IsActive => _active;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        _health = GetComponent<Health>();
        _speedHash = Animator.StringToHash(speedParameter);
    }

    private void Start()
    {
        TryFindTarget();
        // activateOnStart = mob oyuncuyu KENDİSİ fark edebilir. Uyanması yine de
        // detectionRange'e girmesine bağlı; uyandıktan sonra mesafe umurunda olmaz.
        _autoDetect = activateOnStart;
    }

    /// <summary>
    /// Mobu uyandırır: oyuncuya döner ve kovalamaya başlar. Pusuda (hırt odası)
    /// HirtRoomAmbush ıslıktan sonra çağırır; Inspector'daki UnityEvent'lere de bağlanabilir.
    /// Tekrar çağrılırsa bir şey yapmaz.
    /// </summary>
    public void BeginChase() => Activate(snapToTargetOnActivate);

    /// <summary>Uyandırma çekirdeği. snap: oyuncuya anında dönsün mü (pusu anı).</summary>
    private void Activate(bool snap)
    {
        if (_active) return;

        _active = true;
        TryFindTarget();
        // İlk saldırı hemen patlamasın; oyuncu ne olduğunu görsün.
        _lastAttackTime = Time.time;

        if (snap) SnapToTarget();

        onActivated?.Invoke();
    }

    /// <summary>Kovalamayı durdurur (cutscene, oyuncu ölümü vb.).</summary>
    public void StopChase()
    {
        _active = false;
        _autoDetect = false;
        CancelAttack();
        SetAnimatorSpeed(0f);
    }

    /// <summary>Yumuşak dönüş beklemeden hedefe anında bakar (pusu anı).</summary>
    public void SnapToTarget()
    {
        if (target == null) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f) return;

        transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
    }

    private void Update()
    {
        // Canımız bittiyse kıpırdama (ölüm animasyonunu Health oynatır).
        if (_health != null && _health.IsDead) return;

        if (target == null)
        {
            TryFindTarget();
            SetAnimatorSpeed(0f);
            return;
        }

        // Henüz uyanmadıysak: pusuda bekliyoruz. Kendi kendine fark edecekse
        // (activateOnStart) oyuncu detectionRange'e girer girmez uyanırız.
        if (!_active)
        {
            SetAnimatorSpeed(0f);
            if (!_autoDetect) return;

            Vector3 toPlayerFlat = target.position - transform.position;
            toPlayerFlat.y = 0f;
            if (toPlayerFlat.magnitude > detectionRange) return;

            // Kendi fark etti: ani dönüş yok, hedefe yumuşakça dönsün.
            Activate(false);
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

        // Saldırı sırasında yerinde dur, sadece hedefe hafifçe dönmeye devam et.
        if (_isAttacking)
        {
            FaceTarget(toTarget, rotationSpeed * 0.4f);
            SetAnimatorSpeed(0f);
            return;
        }

        FaceTarget(toTarget, rotationSpeed);

        // Menzildeyse ve cooldown dolduysa saldır.
        if (distance <= attackRange && Time.time >= _lastAttackTime + attackCooldown)
        {
            _attackRoutine = StartCoroutine(AttackRoutine());
            return;
        }

        // stopDistance'a kadar yaklaş, sonra dur. Speed parametresi Walk animasyonunu açar.
        if (distance > stopDistance)
        {
            transform.position += toTarget.normalized * (moveSpeed * Time.deltaTime);
            SetAnimatorSpeed(1f);
        }
        else
        {
            SetAnimatorSpeed(0f);
        }
    }

    /// <summary>
    /// EnemyAnimationEvents köprüsünden gelir: animasyondaki vuruş frame'i.
    /// useAnimationEventHit açıksa hasar tam bu anda uygulanır, kapalıysa event yutulur
    /// (klipler oyuncunun FBX'inden geldiği için event'ler zaten içlerinde geliyor).
    /// </summary>
    public void NotifyAttackHit()
    {
        if (!useAnimationEventHit || !_isAttacking || _damageAppliedThisAttack) return;

        _damageAppliedThisAttack = true;
        TryDealDamage();
    }

    /// <summary>
    /// Köprüden gelen bitiş event'i. Saldırı süresini attackDuration yönettiği için
    /// burada bir şey yapılmaz; "has no receiver" uyarısını susturur.
    /// </summary>
    public void NotifyAttackStepEnd()
    {
    }

    /// <summary>Saldırı: trigger, vuruş penceresinde hasar, sonra toparlanma.</summary>
    private IEnumerator AttackRoutine()
    {
        _isAttacking = true;
        _damageAppliedThisAttack = false;
        _lastAttackTime = Time.time;

        SetAnimatorSpeed(0f);
        SetTriggerSafe(attackTrigger);
        onAttack?.Invoke();

        // Animasyondaki vuruş anını bekle. AttackHit event'i bu süre içinde geldiyse
        // hasar zaten uygulanmıştır; yoksa hitDelay yedek olarak çalışır.
        yield return new WaitForSeconds(hitDelay);
        if (!_damageAppliedThisAttack)
        {
            _damageAppliedThisAttack = true;
            TryDealDamage();
        }

        // Saldırının kalan süresi boyunca mob yerinde kalır.
        float remaining = attackDuration - hitDelay;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        _isAttacking = false;
        _attackRoutine = null;
    }

    /// <summary>Vuruş anında oyuncu hâlâ menzildeyse hasar + knockback uygular.</summary>
    private void TryDealDamage()
    {
        if (target == null || _targetHealth == null || _targetHealth.IsDead) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        // Oyuncu kaçtıysa saldırı ıskalar; dodge'un anlamı olsun.
        if (toTarget.magnitude > hitRange) return;

        _targetHealth.TakeDamage(damage);

        if (_targetRb == null) return;

        // Yatay itme + isteğe bağlı yukarı bileşen.
        Vector3 force = toTarget.normalized * knockbackForce + Vector3.up * knockbackUpward;
        _targetRb.AddForce(force, ForceMode.Impulse);
    }

    /// <summary>Hedefe yatayda yumuşakça döner.</summary>
    private void FaceTarget(Vector3 toTarget, float speed)
    {
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, speed * Time.deltaTime);
    }

    private void CancelAttack()
    {
        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = null;
        _isAttacking = false;
    }

    /// <summary>Animator'daki hız parametresini yumuşatarak yazar (Idle &lt;-&gt; Walk).</summary>
    private void SetAnimatorSpeed(float value)
    {
        if (animator == null) return;
        animator.SetFloat(_speedHash, value, speedDampTime, Time.deltaTime);
    }

    /// <summary>Trigger'ı yalnızca controller'da gerçekten varsa tetikler (uyarı üretmemek için).</summary>
    private void SetTriggerSafe(string parameter)
    {
        if (animator == null || string.IsNullOrEmpty(parameter)) return;

        foreach (var p in animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == parameter)
            {
                animator.SetTrigger(parameter);
                return;
            }
        }
    }

    private void TryFindTarget()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        if (target == null) return;

        // Knockback ve hasar için hedefin bileşenlerini önbelleğe al.
        if (_targetRb == null) _targetRb = target.GetComponent<Rigidbody>();
        if (_targetHealth == null) _targetHealth = target.GetComponent<PlayerHealth>();
    }

    /// <summary>Devre dışı kalınca (ör. ölümde Health kapatır) yürüyüş animasyonu asılı kalmasın.</summary>
    private void OnDisable()
    {
        CancelAttack();
        if (animator != null) animator.SetFloat(_speedHash, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        // Algılama, saldırı ve vuruş menzillerini sahnede göster.
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, stopDistance);
    }
}
