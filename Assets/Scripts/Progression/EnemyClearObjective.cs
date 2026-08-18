using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// "Şu düşmanları öldür" hedefi. Listedeki tüm Health'ler ölünce bağlı adımı
/// GameProgress'te tamamlanmış işaretler; o adımı bekleyen kapılar açılır.
///
/// Üç adımın üçünde de aynı script kullanılır:
///   - HirtRoom  : odadaki tüm hırtlar
///   - Swordsman : tek eleman (BlackSwordsman'ın Health'i)
///   - Boss      : tek eleman (boss'un Health'i)
///
/// Kurulum: boş bir GameObject'e ekle, stage'i seç ve düşmanları enemies listesine
/// sürükle. Liste boşsa enemiesParent'ın (yoksa kendi) child'larındaki Health'ler
/// toplanır. Tools/Progression/Setup Game Flow üçünü de hazır bağlar.
/// </summary>
[DisallowMultipleComponent]
public class EnemyClearObjective : MonoBehaviour
{
    [Header("Hedef")]
    [Tooltip("Bu düşmanlar temizlenince tamamlanacak adım.")]
    [SerializeField] private GameStage stage = GameStage.HirtRoom;

    [Header("Düşmanlar")]
    [Tooltip("Takip edilecek düşmanların Health bileşenleri.")]
    [SerializeField] private Health[] enemies;
    [Tooltip("Liste boşsa bu transform'un (yoksa kendi objesinin) child'larındaki Health'ler toplanır.")]
    [SerializeField] private Transform enemiesParent;

    [Header("Zamanlama")]
    [Tooltip("Son düşman öldükten sonra adımın tamamlanması için beklenecek süre " +
             "(ölüm animasyonu otursun, kapı hemen açılmasın).")]
    [SerializeField] private float completeDelay = 1f;

    [Header("Olaylar")]
    [Tooltip("Her düşman öldüğünde tetiklenir (sayaç UI'ı, ses).")]
    public UnityEvent onEnemyKilled;
    [Tooltip("Tüm düşmanlar temizlenince tetiklenir (adım tamamlanmadan hemen önce).")]
    public UnityEvent onCleared;

    private readonly List<Health> _tracked = new List<Health>();
    private int _remaining;
    private bool _completed;

    /// <summary>Kalan düşman sayısı (UI için).</summary>
    public int Remaining => _remaining;

    /// <summary>Takip edilen toplam düşman sayısı.</summary>
    public int Total => _tracked.Count;

    /// <summary>Bu hedefin bağlı olduğu adım.</summary>
    public GameStage Stage => stage;

    private void Start()
    {
        CollectEnemies();

        foreach (Health enemy in _tracked)
        {
            if (enemy.IsDead) continue;

            _remaining++;
            enemy.Died += OnEnemyDied;
        }

        if (_tracked.Count == 0)
        {
            Debug.LogWarning($"{name}: Takip edilecek dusman yok; '{stage}' adimi hicbir zaman " +
                             "tamamlanmaz. enemies listesini doldur ya da enemiesParent ata.", this);
            return;
        }

        // Ilerleme korunarak sahne yeniden yuklendiyse adim zaten bitmis olabilir.
        if (GameProgress.Instance.IsStageComplete(stage)) _completed = true;
        else if (_remaining == 0) Complete();
    }

    private void OnDestroy()
    {
        foreach (Health enemy in _tracked)
        {
            if (enemy != null) enemy.Died -= OnEnemyDied;
        }
    }

    private void OnEnemyDied()
    {
        if (_completed) return;

        _remaining = Mathf.Max(0, _remaining - 1);
        onEnemyKilled?.Invoke();

        if (_remaining > 0) return;

        if (completeDelay > 0f) StartCoroutine(CompleteAfterDelay());
        else Complete();
    }

    private IEnumerator CompleteAfterDelay()
    {
        yield return new WaitForSeconds(completeDelay);
        Complete();
    }

    private void Complete()
    {
        if (_completed) return;
        _completed = true;

        onCleared?.Invoke();
        GameProgress.Instance.CompleteStage(stage);
    }

    /// <summary>Düşman listesini çözer: elle verilen liste > enemiesParent > kendi child'ları.</summary>
    private void CollectEnemies()
    {
        _tracked.Clear();

        if (enemies != null && enemies.Length > 0)
        {
            foreach (Health enemy in enemies)
                if (enemy != null) _tracked.Add(enemy);

            return;
        }

        Transform root = enemiesParent != null ? enemiesParent : transform;
        _tracked.AddRange(root.GetComponentsInChildren<Health>(true));
    }

    private void OnDrawGizmosSelected()
    {
        // Hedefteki dusmanlara giden baglari sahnede goster.
        if (enemies == null) return;

        Gizmos.color = new Color(1f, 0.2f, 0.2f);
        foreach (Health enemy in enemies)
        {
            if (enemy != null) Gizmos.DrawLine(transform.position, enemy.transform.position);
        }
    }
}
