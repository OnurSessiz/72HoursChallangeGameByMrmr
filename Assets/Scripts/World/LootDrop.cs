using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Ölünce eşya düşüren bileşen. Aynı objedeki Health'in Died event'ine abone olur;
/// can bitince prefab'ı ayakların dibine doğurur (sopalı hırt -> 1 portakal).
///
/// Health objeyi destroyDelay sonunda yok ettiği için loot ayrı bir obje olarak
/// doğar; hırdın cesedi silinince portakal yerde kalır.
///
/// Kurulum: hırda ekle ve dropPrefab'a portakal prefab'ını ata
/// (Tools/World/Add Orange Drop To Selection bunu senin yerine yapar).
/// </summary>
[DisallowMultipleComponent]
public class LootDrop : MonoBehaviour
{
    [Header("Düşen eşya")]
    [Tooltip("Ölünce doğacak prefab (portakal). Boşsa hiçbir şey düşmez.")]
    [SerializeField] private GameObject dropPrefab;
    [Tooltip("Kaç adet düşecek.")]
    [SerializeField] private int amount = 1;
    [Tooltip("Düşme ihtimali (1 = her zaman düşer, 0.5 = yarı yarıya).")]
    [Range(0f, 1f)]
    [SerializeField] private float dropChance = 1f;

    [Header("Konum")]
    [Tooltip("Eşyanın doğacağı nokta. Boşsa bu objenin konumu kullanılır.")]
    [SerializeField] private Transform dropPoint;
    [Tooltip("Doğduğu noktanın yerden yüksekliği (cesedin içinde kalmasın).")]
    [SerializeField] private float dropHeight = 0.6f;
    [Tooltip("Birden çok eşyada birbirlerinden yatayda saçılma yarıçapı.")]
    [SerializeField] private float scatterRadius = 0.4f;

    [Header("Olaylar")]
    [Tooltip("Eşya düştüğü anda tetiklenir (ses, ışık, UI).")]
    public UnityEvent onDropped;

    private Health _health;
    private bool _dropped;

    private void Awake()
    {
        _health = GetComponent<Health>();

        if (_health == null)
        {
            Debug.LogWarning($"{name}: LootDrop icin ayni objede Health gerekiyor; " +
                             "olum yakalanamadigi icin esya dusmez.", this);
            return;
        }

        _health.Died += Drop;
    }

    private void OnDestroy()
    {
        if (_health != null) _health.Died -= Drop;
    }

    /// <summary>
    /// Eşyayı düşürür. Health.Died ile otomatik çağrılır; istersen Inspector'daki
    /// UnityEvent'lerden (ör. sandık açma) elle de tetikleyebilirsin.
    /// </summary>
    public void Drop()
    {
        // Ölüm bir kez olur ama elle de çağrılabildiği için kendimizi koruyalım.
        if (_dropped || dropPrefab == null || amount <= 0) return;
        _dropped = true;

        if (dropChance < 1f && Random.value > dropChance) return;

        Vector3 origin = (dropPoint != null ? dropPoint.position : transform.position)
                         + Vector3.up * dropHeight;

        for (int i = 0; i < amount; i++)
        {
            Vector3 position = origin;

            // Tek eşyada saçılma yok; birden çoksa üst üste binmesinler.
            if (amount > 1 && scatterRadius > 0f)
            {
                Vector2 offset = Random.insideUnitCircle * scatterRadius;
                position += new Vector3(offset.x, 0f, offset.y);
            }

            GameObject drop = Instantiate(dropPrefab, position, Quaternion.identity);

            // Prefab'ın süzülme merkezi doğduğu yer olsun.
            var pickup = drop.GetComponent<HealthPickup>();
            if (pickup != null) pickup.ResetBobOrigin();
        }

        onDropped?.Invoke();
    }

    private void OnDrawGizmosSelected()
    {
        // Eşyanın doğacağı noktayı sahnede göster.
        Vector3 origin = (dropPoint != null ? dropPoint.position : transform.position)
                         + Vector3.up * dropHeight;

        Gizmos.color = new Color(1f, 0.6f, 0f);
        Gizmos.DrawWireSphere(origin, Mathf.Max(0.1f, scatterRadius));
    }
}
