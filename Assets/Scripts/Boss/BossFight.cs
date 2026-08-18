using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Boss dövüş davranışı: oyuncuyu kovalar (Walk animasyonu), menzile girince
/// Attack1/Attack2 combo'sunu sırayla oynatır ve vuruş anında hasar + knockback uygular.
///
/// Dövüş kendiliğinden başlamaz: jump scare bitince BossRoomTurn -> BeginFight() çağırır.
/// Böylece boss, scare animasyonu oynarken oyuncuya yürümez.
///
/// Animator sözleşmesi (BossAnimatorSetup ile aynı):
///   Speed   : float   -> Idle &lt;-&gt; Walk geçişi
///   Attack1 : trigger  \ Any State üzerinden oynar, bitince Idle'a döner
///   Attack2 : trigger  /
///   Stun    : trigger -> sersemleme animasyonu (Any State üzerinden)
///   Stunned : bool    -> sersemleme boyunca true; false olunca Idle'a döner
///
/// Oyuncunun uçan tekmesi boss'u sersemletir (PlayerAttack.airKick.stunDuration ->
/// Health -> IStunnable.Stun). Sersemken boss yürümez, dönmez, saldırmaz.
/// </summary>
[DisallowMultipleComponent]
public class BossFight : MonoBehaviour, IStunnable
{
    [Header("Hedef")]
    [Tooltip("Kovalanacak hedef. Boşsa 'Player' tag'li obje otomatik bulunur.")]
    [SerializeField] private Transform target;

    [Header("Başlangıç")]
    [Tooltip("Açıksa dövüş oyun başında başlar. Jump scare'den sonra başlayacaksa KAPALI bırak.")]
    [SerializeField] private bool startOnAwake = false;

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 2.5f;
    [SerializeField] private float rotationSpeed = 5f;
    [Tooltip("Oyuncuya bu kadar yaklaşınca durur (üst üste binmeyi önler).")]
    [SerializeField] private float stopDistance = 2.2f;
    [Tooltip("Bu mesafenin dışındaysa boss oyuncuyu kovalamaz (odada kalsın diye).")]
    [SerializeField] private float chaseRange = 30f;

    [Header("Saldırı")]
    [Tooltip("Oyuncu bu mesafedeyken saldırı başlatır.")]
    [SerializeField] private float attackRange = 2.5f;
    [Tooltip("Saldırı animasyonunun toplam süresi; bu süre boyunca boss yerinde durur.")]
    [SerializeField] private float attackDuration = 1.2f;
    [Tooltip("Saldırı başladıktan kaç saniye sonra hasar uygulanacak (animasyondaki vuruş anı).")]
    [SerializeField] private float hitDelay = 0.45f;
    [Tooltip("Vuruş anında oyuncu bu mesafedeyse hasar alır; uzaklaştıysa ıskalar.")]
    [SerializeField] private float hitRange = 3f;
    [Tooltip("Açıksa vuruş anı hitDelay yerine animasyondaki AttackHit event'inden gelir. Event'i olmayan clip'lerde hitDelay yedek olarak devreye girer.")]
    [SerializeField] private bool useAnimationEventHit = false;
    [Tooltip("İki saldırı arası bekleme (saniye).")]
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float damage = 25f;
    [SerializeField] private float knockbackForce = 10f;
    [SerializeField] private float knockbackUpward = 2f;

    [Header("Animasyon")]
    [Tooltip("Boss'un Animator'ı. Boşsa bu objede ve child'larında aranır.")]
    [SerializeField] private Animator animator;
    [Tooltip("Idle <-> Walk geçişini süren float parametre.")]
    [SerializeField] private string speedParameter = "Speed";
    [Tooltip("Hız değerinin yumuşatma süresi.")]
    [SerializeField] private float speedDampTime = 0.15f;
    [Tooltip("Saldırı trigger adları; sırayla (veya rastgele) oynatılır.")]
    [SerializeField] private string[] attackTriggers = { "Attack1", "Attack2" };
    [Tooltip("Açıksa saldırılar sırayla, kapalıysa rastgele seçilir.")]
    [SerializeField] private bool alternateAttacks = true;

    [Header("Sersemleme (stun)")]
    [Tooltip("Boss sersemletilebilir mi? Kapalıysa gelen stun yok sayılır.")]
    [SerializeField] private bool canBeStunned = true;
    [Tooltip("Gelen stun süresi bu çarpanla uygulanır (0.5 = yarı süre).")]
    [SerializeField] private float stunResistance = 1f;
    [Tooltip("Tek seferde sersemleyebileceği en uzun süre (üst üste vuruşlara karşı tavan).")]
    [SerializeField] private float maxStunDuration = 4f;
    [Tooltip("Sersemleme bitince ilk saldırıya kadar beklenecek ek süre.")]
    [SerializeField] private float stunRecoveryDelay = 0.4f;
    [Tooltip("Sersemleyince tetiklenecek trigger. Animator'da yoksa sessizce atlanır.")]
    [SerializeField] private string stunTrigger = "Stun";
    [Tooltip("Sersemleme boyunca true kalan bool (Stun state'i döngüde tutmak için). Boş bırakılabilir.")]
    [SerializeField] private string stunnedParameter = "Stunned";
    [Tooltip("Sersemleme anında çalar (kafa çınlaması, sersemleme sesi).")]
    [SerializeField] private AudioClip stunClip;

    [Header("Ses (ön hazırlık, klipler sonra atanacak)")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Dövüş başladığı anda çalar (kükreme, müzik cue'su).")]
    [SerializeField] private AudioClip fightStartClip;
    [Tooltip("Her saldırı başlangıcında çalar (savurma sesi).")]
    [SerializeField] private AudioClip attackClip;
    [Tooltip("Vuruş isabet ettiğinde çalar (darbe sesi).")]
    [SerializeField] private AudioClip hitClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar")]
    [Tooltip("Dövüş başladığında tetiklenir (boss müziği, health bar UI vb.).")]
    public UnityEvent onFightStart;
    [Tooltip("Her saldırı başlangıcında tetiklenir.")]
    public UnityEvent onAttack;
    [Tooltip("Boss sersemlediğinde tetiklenir (UI uyarısı, efekt vb.).")]
    public UnityEvent onStunned;
    [Tooltip("Sersemleme bitip boss kendine geldiğinde tetiklenir.")]
    public UnityEvent onStunEnded;

    private Rigidbody _targetRb;
    private PlayerHealth _targetHealth;
    private bool _fightStarted;
    private bool _isAttacking;
    private float _lastAttackTime = Mathf.NegativeInfinity;
    private int _nextAttackIndex;
    private int _speedHash;
    private float _stunEndTime = Mathf.NegativeInfinity;
    private Coroutine _attackRoutine;
    private Coroutine _stunRoutine;
    private bool _damageAppliedThisAttack;

    /// <summary>Dövüş başladı mı? (UI/müzik sistemleri okuyabilir.)</summary>
    public bool FightStarted => _fightStarted;

    /// <summary>Boss şu an sersemlemiş mi? (Sersemken yürümez, saldırmaz, dönmez.)</summary>
    public bool IsStunned => Time.time < _stunEndTime;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        _speedHash = Animator.StringToHash(speedParameter);

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }
    }

    private void Start()
    {
        TryFindTarget();
        if (startOnAwake) BeginFight();
    }

    /// <summary>
    /// Dövüşü başlatır. Jump scare bittiğinde BossRoomTurn tarafından çağrılır;
    /// Inspector'daki UnityEvent'lere de bağlanabilir. Tekrar çağrılırsa bir şey yapmaz.
    /// </summary>
    public void BeginFight()
    {
        if (_fightStarted) return;

        _fightStarted = true;
        TryFindTarget();
        // İlk saldırı hemen patlamasın, oyuncu toparlansın.
        _lastAttackTime = Time.time;

        PlaySfx(fightStartClip);
        onFightStart?.Invoke();
    }

    /// <summary>Dövüşü durdurur (cutscene, boss ölümü vb.).</summary>
    public void StopFight()
    {
        _fightStarted = false;
        _isAttacking = false;
        CancelAttack();
        ClearStun();
        SetAnimatorSpeed(0f);
    }

    /// <summary>
    /// IStunnable: boss'u verilen süre boyunca sersemletir. Devam eden saldırı kesilir,
    /// boss yerinde donar ve Stun animasyonu oynar. Sersemken gelen yeni stun süreyi
    /// (maxStunDuration tavanına kadar) uzatır.
    /// </summary>
    public void Stun(float duration)
    {
        if (!canBeStunned || duration <= 0f || stunResistance <= 0f) return;

        float scaled = Mathf.Min(duration * stunResistance, maxStunDuration);
        float newEnd = Time.time + scaled;
        bool wasStunned = IsStunned;
        if (newEnd <= _stunEndTime) return;   // daha kısa bir stun, mevcut olanı kısaltmasın
        _stunEndTime = newEnd;

        // Saldırının ortasındaysa kes: sersemleyen boss vuruşunu tamamlamasın.
        CancelAttack();
        SetAnimatorSpeed(0f);

        SetTriggerSafe(stunTrigger);
        SetBoolSafe(stunnedParameter, true);

        if (!wasStunned)
        {
            PlaySfx(stunClip);
            onStunned?.Invoke();
        }

        if (_stunRoutine != null) StopCoroutine(_stunRoutine);
        _stunRoutine = StartCoroutine(StunRoutine());
    }

    /// <summary>Sersemleme bitene kadar bekler, sonra boss'u tekrar aktif eder.</summary>
    private IEnumerator StunRoutine()
    {
        while (IsStunned) yield return null;

        _stunRoutine = null;
        SetBoolSafe(stunnedParameter, false);
        // Sersemlemeden çıkar çıkmaz vurmasın; oyuncuya kaçma payı kalsın.
        _lastAttackTime = Time.time - attackCooldown + stunRecoveryDelay;
        onStunEnded?.Invoke();
    }

    /// <summary>Devam eden saldırıyı iptal eder (stun, dövüş sonu).</summary>
    private void CancelAttack()
    {
        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = null;
        _isAttacking = false;
    }

    /// <summary>Sersemleme durumunu sıfırlar (dövüş durdurulunca).</summary>
    private void ClearStun()
    {
        if (_stunRoutine != null) StopCoroutine(_stunRoutine);
        _stunRoutine = null;
        _stunEndTime = Mathf.NegativeInfinity;
        SetBoolSafe(stunnedParameter, false);
    }

    private void Update()
    {
        if (!_fightStarted) return;

        if (target == null)
        {
            TryFindTarget();
            return;
        }

        // Sersemken kıpırdamaz: ne yürür, ne döner, ne saldırır. Bu, uçan tekmenin ödülü.
        if (IsStunned)
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

        if (distance > chaseRange)
        {
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

        // stopDistance'a kadar yürü; Walk animasyonu Speed parametresiyle açılır.
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
    /// Animation Event alıcısı. Boss'un saldırı clip'leri oyuncunun FBX'inden geldiği için
    /// içlerinde AttackHit event'i var; alıcı olmayınca Unity "has no receiver" uyarısı basar.
    /// useAnimationEventHit açıksa vuruş tam bu frame'de uygulanır, kapalıysa event yutulur.
    /// </summary>
    public void AttackHit()
    {
        if (!useAnimationEventHit || !_isAttacking || _damageAppliedThisAttack) return;

        _damageAppliedThisAttack = true;
        TryDealDamage();
    }

    /// <summary>
    /// Animation Event alıcısı (oyuncu clip'lerinden gelen bitiş event'i). Boss'un saldırı
    /// süresini attackDuration yönettiği için burada bir şey yapılmaz; uyarıyı susturur.
    /// </summary>
    public void AttackStepEnd()
    {
    }

    /// <summary>Saldırı: trigger, vuruş penceresinde hasar, sonra toparlanma.</summary>
    private IEnumerator AttackRoutine()
    {
        _isAttacking = true;
        _damageAppliedThisAttack = false;
        _lastAttackTime = Time.time;

        // Sıradaki saldırıyı seç (sırayla ya da rastgele).
        if (animator != null && attackTriggers != null && attackTriggers.Length > 0)
        {
            int index = alternateAttacks
                ? _nextAttackIndex % attackTriggers.Length
                : Random.Range(0, attackTriggers.Length);
            _nextAttackIndex++;

            animator.SetTrigger(attackTriggers[index]);
        }

        PlaySfx(attackClip);
        onAttack?.Invoke();

        // Animasyondaki vuruş anını bekle. AttackHit event'i bu süre içinde geldiyse
        // hasar zaten uygulanmıştır; yoksa hitDelay yedek olarak çalışır.
        yield return new WaitForSeconds(hitDelay);
        if (!_damageAppliedThisAttack)
        {
            _damageAppliedThisAttack = true;
            TryDealDamage();
        }

        // Saldırının kalan süresi boyunca boss yerinde kalır.
        float remaining = attackDuration - hitDelay;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        _isAttacking = false;
        _attackRoutine = null;
    }

    /// <summary>Vuruş anında oyuncu hâlâ menzildeyse hasar + knockback uygular.</summary>
    private void TryDealDamage()
    {
        if (target == null || _targetHealth == null) return;
        if (_targetHealth.IsDead) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        // Oyuncu kaçtıysa saldırı ıskalar; dodge'un anlamı olsun.
        if (toTarget.magnitude > hitRange) return;

        _targetHealth.TakeDamage(damage);
        PlaySfx(hitClip);

        if (_targetRb != null)
        {
            Vector3 force = toTarget.normalized * knockbackForce + Vector3.up * knockbackUpward;
            _targetRb.AddForce(force, ForceMode.Impulse);
        }
    }

    /// <summary>Hedefe yatayda yumuşakça döner.</summary>
    private void FaceTarget(Vector3 toTarget, float speed)
    {
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, speed * Time.deltaTime);
    }

    /// <summary>Trigger'ı yalnızca controller'da gerçekten varsa tetikler (uyarı üretmemek için).</summary>
    private void SetTriggerSafe(string parameter)
    {
        if (!HasParameter(parameter, AnimatorControllerParameterType.Trigger)) return;
        animator.SetTrigger(parameter);
    }

    /// <summary>Bool'u yalnızca controller'da gerçekten varsa yazar.</summary>
    private void SetBoolSafe(string parameter, bool value)
    {
        if (!HasParameter(parameter, AnimatorControllerParameterType.Bool)) return;
        animator.SetBool(parameter, value);
    }

    private bool HasParameter(string parameter, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(parameter)) return false;

        foreach (var p in animator.parameters)
            if (p.type == type && p.name == parameter) return true;

        return false;
    }

    private void SetAnimatorSpeed(float value)
    {
        if (animator == null) return;
        animator.SetFloat(_speedHash, value, speedDampTime, Time.deltaTime);
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, sfxVolume);
    }

    private void TryFindTarget()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        if (target == null) return;

        // Hasar ve knockback için hedefin bileşenlerini önbelleğe al.
        if (_targetHealth == null) _targetHealth = target.GetComponent<PlayerHealth>();
        if (_targetRb == null) _targetRb = target.GetComponent<Rigidbody>();
    }

    private void OnDrawGizmosSelected()
    {
        // Kovalama, saldırı ve vuruş menzilleri.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, chaseRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, hitRange);
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, stopDistance);
    }
}
