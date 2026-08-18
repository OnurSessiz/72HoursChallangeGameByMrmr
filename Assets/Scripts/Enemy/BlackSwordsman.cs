using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Çift kılıçlı nöbetçi (BlackSwordsMan). Diğer hırtların aksine ASLA YER DEĞİŞTİRMEZ:
/// olduğu yerde durur, sadece oyuncuya döner.
///
/// Akış:
///   1) Oyuncu alertRange'e girer  -> SwordsReady animasyonu (bir kez), kılıçlar çekilir
///   2) Oyuncu attackRange'e girer -> DonenYaraks: 360 derece dönen saldırı
///   3) Vuruş anında oyuncu hitRange içindeyse az hasar (10) ama ÇOK ağır savrulma verir
///
/// 360 derece dönüş animasyonun içinde olduğu için kod rotasyona karışmaz; saldırı
/// boyunca hedefe dönme de durur (animasyon kendi dönüşünü yapsın).
///
/// Saldırı her yönü tararken oyuncu arkada bile olsa isabet eder (dönen saldırı),
/// bu yüzden koni kontrolü yoktur — tek ölçüt mesafe.
///
/// Animator sözleşmesi (BlackSwordsmanAnimatorSetup ile aynı):
///   Idle                    : varsayılan state
///   SwordsReady : trigger  -> oyuncuyu görünce, dönüşü Idle'a
///   DonenYaraks : trigger  -> dönen saldırı, dönüşü Idle'a
///   Hit / Die   : trigger  -> Health tetikler
/// Parametre controller'da yoksa sessizce atlanır.
/// </summary>
[DisallowMultipleComponent]
public class BlackSwordsman : MonoBehaviour
{
    [Header("Hedef")]
    [Tooltip("Takip edilecek hedef. Boşsa 'Player' tag'li obje otomatik bulunur.")]
    [SerializeField] private Transform target;

    [Header("Menziller")]
    [Tooltip("Oyuncu bu mesafeye girince kılıçları çeker (SwordsReady). Bir kez oynar.")]
    [SerializeField] private float alertRange = 12f;
    [Tooltip("Oyuncu bu mesafedeyken dönen saldırıyı başlatır.")]
    [SerializeField] private float attackRange = 3f;
    [Tooltip("Vuruş anında oyuncu bu mesafedeyse isabet eder. Dönen saldırı olduğu için yön aranmaz.")]
    [SerializeField] private float hitRange = 3.5f;

    [Header("Direnç")]
    [Tooltip("Açıksa hiç itilmez: Health'in knockback çarpanı Awake'te 0'a çekilir. " +
             "Yerinden kıpırdamayan bir nöbetçi olduğu için uçan tekmeyle geri itilerek " +
             "etkisiz hale getirilmesini engeller. Kapatırsan Health'teki çarpan geçerli olur.")]
    [SerializeField] private bool resistKnockback = true;

    [Header("Dönüş")]
    [Tooltip("Oyuncuya dönme hızı (Slerp katsayısı). Yerinden kıpırdamaz, sadece döner.")]
    [SerializeField] private float rotationSpeed = 4f;
    [Tooltip("Kılıçları çekene kadar oyuncuya dönsün mü? Kapalıysa uyanana kadar sabit bakar.")]
    [SerializeField] private bool faceTargetBeforeAlert = false;

    [Header("Saldırı")]
    [Tooltip("Saldırı animasyonunun toplam süresi; bu süre boyunca yeni saldırı başlamaz.")]
    [SerializeField] private float attackDuration = 1.6f;
    [Tooltip("Saldırı başladıktan kaç saniye sonra hasar uygulanacak (dönüşün savurduğu an).")]
    [SerializeField] private float hitDelay = 0.6f;
    [Tooltip("İki saldırı arası bekleme (saniye).")]
    [SerializeField] private float attackCooldown = 2.5f;
    [Tooltip("Kılıçları çektikten sonra ilk saldırıya kadar beklenecek süre.")]
    [SerializeField] private float firstAttackDelay = 0.6f;
    [Tooltip("Verilecek hasar. Bu düşmanın tehdidi hasar değil, savurma mesafesidir.")]
    [SerializeField] private float damage = 10f;

    [Header("Savrulma (asıl tehdit)")]
    [Tooltip("Oyuncunun yatayda kaç METRE fırlayacağı. İtme kuvveti bundan hesaplanır.")]
    [SerializeField] private float launchDistance = 8f;
    [Tooltip("Fırlarken çıkacağı tepe yüksekliği (metre). Yükseldikçe havada kalma süresi artar.")]
    [SerializeField] private float launchHeight = 2.5f;
    [Tooltip("Açıksa savurma yönü hep dışa doğrudur (dönen saldırı merkezden savurur).")]
    [SerializeField] private bool launchAwayFromSelf = true;

    [Header("Animasyon")]
    [Tooltip("Boşsa bu objede ve child'larında aranır.")]
    [SerializeField] private Animator animator;
    [Tooltip("Kılıçları çekme animasyonunun trigger adı.")]
    [SerializeField] private string readyTrigger = "SwordsReady";
    [Tooltip("Dönen saldırının trigger adı.")]
    [SerializeField] private string attackTrigger = "DonenYaraks";

    [Header("Ses")]
    [Tooltip("Boşsa Awake'te otomatik AudioSource eklenir ve 3D olarak ayarlanır.")]
    [SerializeField] private AudioSource audioSource;
    [Tooltip("Kılıçları çekerken çalar.")]
    [SerializeField] private AudioClip readyClip;
    [Tooltip("Her dönen saldırı başlangıcında çalar.")]
    [SerializeField] private AudioClip attackClip;
    [Tooltip("Oyuncuya isabet edince çalar.")]
    [SerializeField] private AudioClip hitClip;
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 1f;

    [Header("Olaylar")]
    [Tooltip("Kılıçları çektiği anda tetiklenir (müzik, UI).")]
    public UnityEvent onAlerted;
    [Tooltip("Her dönen saldırı başlangıcında tetiklenir.")]
    public UnityEvent onAttack;

    private PlayerHealth _targetHealth;
    private PlayerController _targetController;
    private Rigidbody _targetRb;
    private Health _health;

    private bool _alerted;
    private bool _isAttacking;
    private bool _damageAppliedThisAttack;
    private float _lastAttackTime = Mathf.NegativeInfinity;
    private Coroutine _attackRoutine;

    /// <summary>Kılıçlarını çekti mi? (Başka sistemler okuyabilir.)</summary>
    public bool IsAlerted => _alerted;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        _health = GetComponent<Health>();

        // Nöbetçinin tüm tasarımı "yerinden kıpırdamaz" üzerine kurulu; itilebilirse
        // oyuncu uçan tekmeyle onu köşeye sürükleyip hiç risk almadan yeniyor.
        if (resistKnockback && _health != null) _health.KnockbackMultiplier = 0f;

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
    }

    private void Update()
    {
        // Canımız bittiyse kıpırdama (ölüm animasyonunu Health oynatır).
        if (_health != null && _health.IsDead) return;

        if (target == null)
        {
            TryFindTarget();
            return;
        }

        if (_targetHealth != null && _targetHealth.IsDead) return;

        // Yatay düzlemde mesafe (yükseklik farkını yok say).
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        // Saldırı sırasında dönüşü animasyona bırak: 360 derece animasyonun içinde.
        if (_isAttacking) return;

        if (!_alerted)
        {
            if (faceTargetBeforeAlert) FaceTarget(toTarget);
            if (distance <= alertRange) Alert();
            return;
        }

        // Uyandıktan sonra: yerinden kıpırdamaz, sadece oyuncuya döner.
        FaceTarget(toTarget);

        if (distance <= attackRange && Time.time >= _lastAttackTime + attackCooldown)
            _attackRoutine = StartCoroutine(AttackRoutine());
    }

    /// <summary>Kılıçları çeker: SwordsReady bir kez oynar, sonra saldırı moduna geçer.</summary>
    public void Alert()
    {
        if (_alerted) return;

        _alerted = true;
        // İlk saldırı, kılıç çekme animasyonu oynarken patlamasın.
        _lastAttackTime = Time.time - attackCooldown + firstAttackDelay;

        SetTriggerSafe(readyTrigger);
        PlaySfx(readyClip);
        onAlerted?.Invoke();
    }

    /// <summary>Dönen saldırı: trigger, vuruş penceresinde hasar + savurma, sonra toparlanma.</summary>
    private IEnumerator AttackRoutine()
    {
        _isAttacking = true;
        _damageAppliedThisAttack = false;
        _lastAttackTime = Time.time;

        SetTriggerSafe(attackTrigger);
        PlaySfx(attackClip);
        onAttack?.Invoke();

        // Dönüşün savurduğu anı bekle.
        yield return new WaitForSeconds(hitDelay);
        if (!_damageAppliedThisAttack)
        {
            _damageAppliedThisAttack = true;
            TryDealDamage();
        }

        // Animasyonun kalan süresi.
        float remaining = attackDuration - hitDelay;
        if (remaining > 0f) yield return new WaitForSeconds(remaining);

        _isAttacking = false;
        _attackRoutine = null;
    }

    /// <summary>
    /// Animation Event köprüsünden gelebilir (EnemyAnimationEvents kullanılırsa).
    /// Vuruş penceresi içinde bir kez hasar uygular.
    /// </summary>
    public void NotifyAttackHit()
    {
        if (!_isAttacking || _damageAppliedThisAttack) return;

        _damageAppliedThisAttack = true;
        TryDealDamage();
    }

    /// <summary>Vuruş anında oyuncu menzildeyse az hasar + ağır savurma uygular.</summary>
    private void TryDealDamage()
    {
        if (target == null || _targetHealth == null || _targetHealth.IsDead) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.magnitude > hitRange) return;

        _targetHealth.TakeDamage(damage);
        PlaySfx(hitClip);

        LaunchTarget(toTarget);
    }

    /// <summary>
    /// Oyuncuyu launchDistance metre öteye fırlatır. Hız balistik olarak hesaplanır:
    ///   vy = sqrt(2 * g * h)          -> tepe yüksekliğine çıkacak dikey hız
    ///   t  = 2 * vy / g               -> havada kalma süresi (çıkış + iniş)
    ///   vx = mesafe / t               -> istenen mesafeyi verecek yatay hız
    /// Böylece Inspector'da "8 metre" yazınca gerçekten ~8 metre uçar; kuvvet
    /// deneme yanılmayla ayarlanmaz.
    /// </summary>
    private void LaunchTarget(Vector3 toTarget)
    {
        Vector3 direction = launchAwayFromSelf && toTarget.sqrMagnitude > 0.0001f
            ? toTarget.normalized
            : transform.forward;

        float gravity = Mathf.Abs(Physics.gravity.y);
        if (gravity < 0.01f) gravity = 9.81f;

        float height = Mathf.Max(0.25f, launchHeight);
        float verticalSpeed = Mathf.Sqrt(2f * gravity * height);
        float airTime = 2f * verticalSpeed / gravity;
        float horizontalSpeed = airTime > 0.01f ? launchDistance / airTime : launchDistance;

        Vector3 velocity = direction * horizontalSpeed + Vector3.up * verticalSpeed;

        // Tercih edilen yol: oyuncunun savrulma state'i. Locomotion her fizik karesinde
        // MovePosition çağırdığı için düz AddForce bir sonraki karede silinirdi.
        if (_targetController != null)
        {
            _targetController.Launch(velocity);
            return;
        }

        // PlayerController yoksa (ör. farklı bir hedef) elden geldiği kadarını yap.
        if (_targetRb != null) _targetRb.linearVelocity = velocity;
    }

    /// <summary>Hedefe yatayda yumuşakça döner; konumu asla değişmez.</summary>
    private void FaceTarget(Vector3 toTarget)
    {
        if (toTarget.sqrMagnitude < 0.0001f) return;

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desired, rotationSpeed * Time.deltaTime);
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

        if (_targetHealth == null) _targetHealth = target.GetComponent<PlayerHealth>();
        if (_targetController == null) _targetController = target.GetComponent<PlayerController>();
        if (_targetRb == null) _targetRb = target.GetComponent<Rigidbody>();
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, sfxVolume);
    }

    private void OnDisable()
    {
        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = null;
        _isAttacking = false;
    }

    private void OnDrawGizmosSelected()
    {
        // Uyanma, saldırı ve vuruş menzilleri.
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, alertRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, hitRange);

        // Savurma mesafesi: oyuncunun nereye kadar uçacağı.
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, launchDistance);
    }
}
