using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// "Şuraya ulaş" hedefi. Oyuncu bu objenin trigger collider'ına girince bağlı adım
/// tamamlanır. Kaya tuzağının tepesine (4. adım) ve istersen ara kontrol
/// noktalarına konur.
///
/// Kurulum: tırmanışın bittiği yere boş bir GameObject koy, BoxCollider ekle,
/// "Is Trigger" işaretle ve stage'i seç. Kutuyu geçidin tamamını kaplayacak kadar
/// geniş yap ki oyuncu kenardan sıyrılıp hedefi atlamasın.
/// </summary>
// Soyut Collider yerine BoxCollider isteniyor: Unity soyut tipi kendisi uretemedigi
// icin RequireComponent(typeof(Collider)) ile bu script bos bir objeye hic eklenemiyordu.
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
public class ReachPointObjective : MonoBehaviour
{
    [Header("Hedef")]
    [Tooltip("Oyuncu buraya ulaşınca tamamlanacak adım.")]
    [SerializeField] private GameStage stage = GameStage.RockTrap;
    [Tooltip("Açıksa önceki adımlar bitmeden buraya girmek adımı tamamlamaz " +
             "(oyuncu bir şekilde arkadan dolaşırsa akış bozulmasın).")]
    [SerializeField] private bool requirePreviousStages = true;

    [Header("Olaylar")]
    [Tooltip("Oyuncu hedefe ulaştığında tetiklenir (ses, UI, kamera).")]
    public UnityEvent onReached;

    private bool _completed;

    /// <summary>Bu hedefin bağlı olduğu adım.</summary>
    public GameStage Stage => stage;

    private void Awake()
    {
        var col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"{name}: ReachPointObjective collider'i 'Is Trigger' olmali.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_completed) return;

        // Oyuncuyu DamageSource/BossRoomTurn ile ayni sekilde tani.
        var health = other.GetComponentInParent<PlayerHealth>();
        if (health == null || health.IsDead) return;

        GameProgress progress = GameProgress.Instance;
        if (progress.IsStageComplete(stage)) return;

        if (requirePreviousStages && !progress.IsStageAvailable(stage))
        {
            Debug.LogWarning($"{name}: Oyuncu '{stage}' hedefine sirasi gelmeden ulasti; " +
                             $"once {progress.CurrentStage} bitmeli. Kapilarin kilidini kontrol et.", this);
            return;
        }

        _completed = true;
        onReached?.Invoke();
        progress.CompleteStage(stage);
    }

    private void OnDrawGizmos()
    {
        var col = GetComponent<Collider>();
        if (col == null) return;

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.25f);
        Gizmos.DrawCube(col.bounds.center, col.bounds.size);
    }
}
