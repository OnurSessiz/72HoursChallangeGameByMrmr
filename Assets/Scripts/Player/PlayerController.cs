using System;
using UnityEngine;

/// <summary>
/// State machine'in beyni. Aktif state'i tutar, geçişleri yönetir ve tüm ortak
/// referansları + ayar değerlerini state'lere sağlar. Cooldown zaman damgaları
/// burada tutulur ki state nesneleri tekrar kullanıldığında sıfırlanmasın.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Referanslar")]
    [SerializeField] private Rigidbody rb;
    [SerializeField] private PlayerInputReader inputReader;
    [Tooltip("Kameranın bağlı olduğu pivot; hareket bunun forward/right'ına göre hesaplanır.")]
    [SerializeField] private Transform cameraPivot;
    [Tooltip("Zemin kontrolünün yapıldığı child transform (ayak hizası).")]
    [SerializeField] private Transform groundCheck;
    [Tooltip("Locomotion animasyonlarını süren Animator. Boşsa aynı GameObject'ten alınır.")]
    [SerializeField] private Animator animator;

    [Header("Animasyon")]
    [Tooltip("Animator'daki hız parametresinin adı (Idle<->Walk geçişi bununla yapılır).")]
    [SerializeField] private string speedParameter = "Speed";
    [Tooltip("Hız değerinin yumuşatma süresi; küçük değer = daha keskin geçiş.")]
    [SerializeField] private float speedDampTime = 0.1f;

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Zıplama")]
    [SerializeField] private float jumpForce = 7f;

    [Header("Fizik Sağlamlığı")]
    [Tooltip("Açıksa karakter yalnızca Y ekseninde döner; çarpışma/itme sonrası yana yatıp devrilmez.")]
    [SerializeField] private bool keepUpright = true;
    [Tooltip("Açıksa hareket kareler arası yumuşatılır (fizik titremesi azalır).")]
    [SerializeField] private bool useInterpolation = true;
    [Tooltip("Açıksa hızlı hareketlerde (dash/uçan tekme) duvarın içinden geçmeyi önleyen sürekli çarpışma kullanılır.")]
    [SerializeField] private bool useContinuousCollision = true;

    [Header("Zemin Kontrolü")]
    [SerializeField] private float groundCheckRadius = 0.25f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 18f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 1f;

    [Header("Dodge (takla)")]
    [SerializeField] private float dodgeSpeed = 12f;
    [SerializeField] private float dodgeDuration = 0.4f;
    [SerializeField] private float dodgeCooldown = 1.5f;

    [Header("Attack")]
    [Tooltip("Her combo adımının süresi (saniye). Bu süre içinde tekrar basılırsa sonraki adım kuyruğa alınır.")]
    [SerializeField] private float attackDuration = 0.5f;
    [Tooltip("Combo adımlarının animasyon trigger adları; dizi sırası = combo adımı. Uzunluğu combo adım sayısını belirler.")]
    [SerializeField] private string[] attackTriggers = { "Attack1", "Attack2", "Attack3" };

    [Header("Air Attack (uçan tekme)")]
    [Tooltip("Havadayken saldırıya basılınca oynayacak animasyon trigger'ı.")]
    [SerializeField] private string airAttackTrigger = "Kick1";
    [Tooltip("Tekme sırasında ileri atılma hızı.")]
    [SerializeField] private float kickForwardSpeed = 12f;
    [Tooltip("Tekme başlarken eklenen yukarı itiş (uçan tekme hissi). 0 = düz dalış.")]
    [SerializeField] private float kickUpwardBoost = 2.5f;
    [Tooltip("Tekmenin üst sınır süresi; bu sürede yere inilmezse biter.")]
    [SerializeField] private float kickMaxDuration = 1f;
    [Tooltip("Zemin kontrolüne başlamadan önceki minimum hava süresi (hemen bitmesin diye).")]
    [SerializeField] private float kickMinAirTime = 0.15f;
    [Tooltip("Tekme sonundaki ileri hızın başlangıca oranı (0.5 = yarı yarıya yavaşlar).")]
    [Range(0.1f, 1f)]
    [SerializeField] private float kickSpeedFalloff = 0.5f;
    [Tooltip("Zıpladıktan sonra bu süre içinde saldırıya basılırsa (ayak hâlâ yerdeyken) yine uçan tekme atılır.")]
    [SerializeField] private float jumpAttackGrace = 0.25f;

    // --- Ayarlara okuma erişimi (state'ler için) ---
    public Rigidbody Rb => rb;
    public PlayerInputReader Input => inputReader;
    public Transform CameraPivot => cameraPivot;

    public float MoveSpeed => moveSpeed;
    public float RotationSpeed => rotationSpeed;
    public float JumpForce => jumpForce;
    public float DashSpeed => dashSpeed;
    public float DashDuration => dashDuration;
    public float DodgeSpeed => dodgeSpeed;
    public float DodgeDuration => dodgeDuration;
    public float AttackDuration => attackDuration;

    // --- Uçan tekme ayarları (AirAttackState okur) ---
    public float KickForwardSpeed => kickForwardSpeed;
    public float KickUpwardBoost => kickUpwardBoost;
    public float KickMaxDuration => kickMaxDuration;
    public float KickMinAirTime => kickMinAirTime;
    public float KickSpeedFalloff => kickSpeedFalloff;

    /// <summary>Combo'daki toplam adım sayısı (attackTriggers dizisinin uzunluğu).</summary>
    public int MaxComboStep => attackTriggers != null ? attackTriggers.Length : 0;

    // --- i-frame; başka sistemler (ör. hasar) okuyabilir ---
    public bool IsInvincible { get; set; }

    // --- Animation Event köprüsü (PlayerAnimationEvents bunları tetikler) ---
    /// <summary>Saldırı animasyonunun bitiş frame'inde tetiklenir; combo bu event'le ilerler.</summary>
    public event Action AttackStepEnded;
    /// <summary>Saldırı animasyonunun vuruş frame'inde tetiklenir; hasar bu anda uygulanır.</summary>
    public event Action AttackHit;
    /// <summary>Yeni bir combo adımı başladığında tetiklenir (1 tabanlı adım no).
    /// PlayerAttack bunu dinleyip adımın hasarını seçer ve vurulanlar listesini sıfırlar.</summary>
    public event Action<int> AttackStepStarted;

    /// <summary>Şu an oynayan combo adımı (1 tabanlı); uçan tekmede 0, saldırı dışında son değeri korur.</summary>
    public int CurrentComboStep { get; private set; }

    /// <summary>Uçan tekme sürüyor mu? PlayerAttack hasar penceresini buna göre açık tutar.</summary>
    public bool IsAirAttack { get; private set; }

    public void NotifyAttackStepEnd() => AttackStepEnded?.Invoke();
    public void NotifyAttackHit() => AttackHit?.Invoke();

    // --- Hazır state örnekleri (her geçişte yeniden new'lememek için) ---
    public LocomotionState Locomotion { get; private set; }
    public DashState Dash { get; private set; }
    public AttackState Attack { get; private set; }
    public DodgeState Dodge { get; private set; }
    public AirAttackState AirAttack { get; private set; }

    private IPlayerState _current;

    // Cooldown zaman damgaları (Time.time bazlı).
    private float _lastDashTime = Mathf.NegativeInfinity;
    private float _lastDodgeTime = Mathf.NegativeInfinity;
    private float _lastJumpTime = Mathf.NegativeInfinity;

    // Bu hava süresinde uçan tekme kullanıldı mı? (yere değince sıfırlanır)
    private bool _airAttackUsed;

    // Animator hız parametresinin hash'i (string yerine performans için).
    private int _speedHash;

    // Combo trigger'larının önceden hesaplanmış hash'leri (adım sırasıyla).
    private int[] _attackTriggerHashes;

    // Hava (düşme/iniş) parametreleri; controller'da yoksa besleme atlanır.
    private int _groundedHash;
    private int _verticalSpeedHash;
    private bool _hasAirParams;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponent<Animator>();

        // Inspector'da unutulsa bile karakter devrilmesin/titremesin.
        ApplyRigidbodySettings();
        _speedHash = Animator.StringToHash(speedParameter);

        // Combo trigger adlarını bir kez hash'le.
        _attackTriggerHashes = new int[attackTriggers.Length];
        for (int i = 0; i < attackTriggers.Length; i++)
            _attackTriggerHashes[i] = Animator.StringToHash(attackTriggers[i]);

        // Hava parametreleri: controller'da tanımlıysa besle.
        _groundedHash = Animator.StringToHash("IsGrounded");
        _verticalSpeedHash = Animator.StringToHash("VerticalSpeed");
        _hasAirParams = HasParameter(_groundedHash) && HasParameter(_verticalSpeedHash);

        Locomotion = new LocomotionState(this);
        Dash = new DashState(this);
        Attack = new AttackState(this);
        Dodge = new DodgeState(this);
        AirAttack = new AirAttackState(this);
    }

    private void Start()
    {
        ChangeState(Locomotion);
    }

    private void Update()
    {
        _current?.Tick();
        UpdateLocomotionAnimation();
        UpdateAirAnimation();

        // Yere değince uçan tekme hakkı yenilenir (havada sonsuz tekmeyi engeller).
        if (_airAttackUsed && IsGrounded()) _airAttackUsed = false;
    }

    /// <summary>
    /// Düşme/iniş geçişlerini besler: zemin durumu ve dikey hız Animator'a yazılır.
    /// Animator bunlarla JumpStart-&gt;Falling-&gt;Land ve ledge'den düşme geçişlerini yapar.
    /// </summary>
    private void UpdateAirAnimation()
    {
        if (animator == null || !_hasAirParams) return;
        animator.SetBool(_groundedHash, IsGrounded());
        animator.SetFloat(_verticalSpeedHash, rb.linearVelocity.y);
    }

    /// <summary>
    /// Idle&lt;-&gt;Walk geçişini besler: input'un büyüklüğünü (0..1) Animator'daki
    /// Speed parametresine yumuşatarak yazar. Fizikten bağımsız olduğu için tüm
    /// state'lerde tutarlı çalışır ve MovePosition'a takılmaz.
    /// </summary>
    private void UpdateLocomotionAnimation()
    {
        if (animator == null) return;

        float target = inputReader != null ? Mathf.Clamp01(inputReader.Move.magnitude) : 0f;
        animator.SetFloat(_speedHash, target, speedDampTime, Time.deltaTime);
    }

    private void FixedUpdate()
    {
        _current?.FixedTick();

        // Constraint'i aşan bir etki (cutscene, dış kuvvet, elle yapılan rotasyon)
        // karakteri yatırdıysa dikliği geri al.
        if (keepUpright) EnforceUpright();
    }

    /// <summary>
    /// Karakterin fizik davranışını sağlamlaştırır:
    ///   - X/Z rotasyonu kilitlenir  -> düşmana/duvara çarpınca devrilmez (asıl "yamulma" sebebi)
    ///   - Interpolate               -> kareler arası titreme gider
    ///   - ContinuousDynamic         -> dash/tekme gibi hızlı hareketlerde duvarı delip geçmez
    /// Inspector'daki mevcut ayarları ezmemek için yalnızca eksik olanları tamamlar.
    /// </summary>
    private void ApplyRigidbodySettings()
    {
        if (rb == null) return;

        if (keepUpright)
        {
            rb.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.angularVelocity = Vector3.zero;
        }

        if (useInterpolation && rb.interpolation == RigidbodyInterpolation.None)
            rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (useContinuousCollision && rb.collisionDetectionMode == CollisionDetectionMode.Discrete)
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    /// <summary>Yalnızca Y rotasyonunu koruyup karakteri dik tutar (yatmışsa düzeltir).</summary>
    private void EnforceUpright()
    {
        if (rb == null) return;

        Vector3 e = rb.rotation.eulerAngles;
        float tiltX = Mathf.DeltaAngle(e.x, 0f);
        float tiltZ = Mathf.DeltaAngle(e.z, 0f);

        // Küçük sapmalarda karışma; state'lerin MoveRotation'ıyla itişmesin.
        if (Mathf.Abs(tiltX) < 0.5f && Mathf.Abs(tiltZ) < 0.5f) return;

        rb.MoveRotation(Quaternion.Euler(0f, e.y, 0f));
        rb.angularVelocity = Vector3.zero;
    }

    /// <summary>Aktif state'i değiştirir: eskisinin Exit'i, yeninin Enter'ı çağrılır.</summary>
    public void ChangeState(IPlayerState next)
    {
        _current?.Exit();
        _current = next;
        _current?.Enter();
    }

    // --- Ortak yardımcılar ---

    /// <summary>Input'u kamera-relative dünya yönüne çevirir (y=0, normalize).</summary>
    public Vector3 GetCameraRelativeMoveDirection()
    {
        Vector2 input = inputReader.Move;
        if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

        // Kamera forward/right'ını yataya düzleştir.
        Vector3 forward = cameraPivot.forward;
        Vector3 right = cameraPivot.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();

        // Çapraz dahil tüm yönleri kapsar.
        return (forward * input.y + right * input.x).normalized;
    }

    public bool IsGrounded()
    {
        return Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Verilen combo adımını (1 tabanlı) başlatır: adımı yayınlar (hasar sistemi için)
    /// ve animasyon trigger'ını ateşler. Animator yoksa yalnızca event yayınlanır ki
    /// hasar sistemi animasyondan bağımsız çalışsın.
    /// </summary>
    public void TriggerAttack(int step)
    {
        CurrentComboStep = step;
        AttackStepStarted?.Invoke(step);

        if (animator == null) return;
        int index = step - 1;
        if (index < 0 || index >= _attackTriggerHashes.Length) return;
        animator.SetTrigger(_attackTriggerHashes[index]);
    }

    /// <summary>
    /// Uçan tekmeyi başlatır: hasar sistemine "tekme adımı" (0) olduğunu bildirir
    /// ve Kick1 animasyonunu tetikler. AirAttackState tarafından çağrılır.
    /// </summary>
    public void TriggerAirAttack()
    {
        IsAirAttack = true;
        CurrentComboStep = 0;                 // 0 = uçan tekme (PlayerAttack bunu ayrı ayarla eşler)
        AttackStepStarted?.Invoke(0);
        SetAnimTrigger(airAttackTrigger);
    }

    /// <summary>Uçan tekme bitti; hasar penceresi kapanır.</summary>
    public void EndAirAttack() => IsAirAttack = false;

    /// <summary>Zıplama anında işaretlenir; hemen ardından gelen saldırı uçan tekmeye sayılır.</summary>
    public void MarkJumpUsed() => _lastJumpTime = Time.time;

    /// <summary>Zıplama girdisinin üzerinden çok kısa süre geçtiyse (ayak henüz yerden kalkmamış olabilir).</summary>
    public bool JustJumped => Time.time < _lastJumpTime + jumpAttackGrace;

    /// <summary>Bu hava süresinde henüz tekme atılmadıysa true.</summary>
    public bool CanAirAttack => !_airAttackUsed;

    /// <summary>Tekme hakkını harcar; yere değince otomatik yenilenir.</summary>
    public void MarkAirAttackUsed() => _airAttackUsed = true;

    /// <summary>Adı verilen Animator trigger'ını ateşler (Dash/Dodge/Jump gibi tek seferlik geçişler).</summary>
    public void SetAnimTrigger(string triggerName)
    {
        if (animator != null) animator.SetTrigger(triggerName);
    }

    /// <summary>
    /// Oyuncu hasar aldığında çağrılır (ör. düşman saldırısı): Hit animasyonunu oynatır.
    /// Dodge i-frame'i sırasında hasar yok sayılır. Can/ölüm sistemi buraya eklenebilir.
    /// </summary>
    public void TakeHit()
    {
        if (IsInvincible) return;
        SetAnimTrigger("Hit");
    }

    /// <summary>Controller'da verilen hash'e sahip bir parametre var mı?</summary>
    private bool HasParameter(int nameHash)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
            if (p.nameHash == nameHash) return true;
        return false;
    }

    // --- Cooldown API'si ---
    public bool CanDash() => Time.time >= _lastDashTime + dashCooldown;
    public bool CanDodge() => Time.time >= _lastDodgeTime + dodgeCooldown;
    public void MarkDashUsed() => _lastDashTime = Time.time;
    public void MarkDodgeUsed() => _lastDodgeTime = Time.time;

    // --- Cooldown UI için okuma erişimi ---
    public float DashCooldown => dashCooldown;
    public float DodgeCooldown => dodgeCooldown;
    public float DashCooldownRemaining => Mathf.Max(0f, (_lastDashTime + dashCooldown) - Time.time);
    public float DodgeCooldownRemaining => Mathf.Max(0f, (_lastDodgeTime + dodgeCooldown) - Time.time);

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}