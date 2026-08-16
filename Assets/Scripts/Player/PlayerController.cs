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

    [Header("Hareket")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float rotationSpeed = 12f;

    [Header("Zıplama")]
    [SerializeField] private float jumpForce = 7f;

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
    [SerializeField] private float attackDuration = 0.5f;

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

    // --- i-frame; başka sistemler (ör. hasar) okuyabilir ---
    public bool IsInvincible { get; set; }

    // --- Hazır state örnekleri (her geçişte yeniden new'lememek için) ---
    public LocomotionState Locomotion { get; private set; }
    public DashState Dash { get; private set; }
    public AttackState Attack { get; private set; }
    public DodgeState Dodge { get; private set; }

    private IPlayerState _current;

    // Cooldown zaman damgaları (Time.time bazlı).
    private float _lastDashTime = Mathf.NegativeInfinity;
    private float _lastDodgeTime = Mathf.NegativeInfinity;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();

        Locomotion = new LocomotionState(this);
        Dash = new DashState(this);
        Attack = new AttackState(this);
        Dodge = new DodgeState(this);
    }

    private void Start()
    {
        ChangeState(Locomotion);
    }

    private void Update()
    {
        _current?.Tick();
    }

    private void FixedUpdate()
    {
        _current?.FixedTick();
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

    // --- Cooldown API'si ---
    public bool CanDash() => Time.time >= _lastDashTime + dashCooldown;
    public bool CanDodge() => Time.time >= _lastDodgeTime + dodgeCooldown;
    public void MarkDashUsed() => _lastDashTime = Time.time;
    public void MarkDodgeUsed() => _lastDodgeTime = Time.time;

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}