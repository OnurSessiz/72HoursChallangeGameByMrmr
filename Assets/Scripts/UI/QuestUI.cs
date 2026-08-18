using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekranın sağ altındaki görev paneli. GameProgress'e bakıp sıradaki adımın
/// hangi odada, ne yapılacağını yazar.
///
/// Akış: adım tamamlanınca panel kısa süre yeşil "✓ tamamlandı" gösterir, sonra
/// sıradaki göreve geçer. Düşman temizleme adımlarında (EnemyClearObjective)
/// kalan düşman sayacı da canlı güncellenir.
///
/// Kurulum: Tools/UI/Create Quest HUD paneli kurar, metinleri doldurur ve bağlar.
/// Metinleri sonradan Inspector'daki entries listesinden değiştirebilirsin.
/// </summary>
[DisallowMultipleComponent]
public class QuestUI : MonoBehaviour
{
    /// <summary>Bir adımın panelde görünecek metinleri.</summary>
    [Serializable]
    public class StageEntry
    {
        [Tooltip("Hangi adım.")]
        public GameStage stage;
        [Tooltip("Üst satır: gidilecek yer (ör. \"Hırt Odası\").")]
        public string room = "";
        [Tooltip("Alt satır: yapılacak iş (ör. \"Odadaki bütün hırtları temizle\").")]
        public string task = "";
    }

    [Header("Metinler")]
    [Tooltip("Her adımın oda ve görev yazısı. Sıra önemli değil; adım eşleşmesine bakılır.")]
    [SerializeField] private StageEntry[] entries;
    [Tooltip("Tüm adımlar bitince yazılacak metin.")]
    [SerializeField] private string allDoneText = "Tüm görevler tamamlandı";
    [Tooltip("Adım tamamlandığında görev yazısının önüne eklenecek işaret.")]
    [SerializeField] private string completedPrefix = "✓ ";

    [Header("Referanslar")]
    [Tooltip("Gizlemek/göstermek için panel kökü. Boşsa bu objenin kendisi kullanılır.")]
    [SerializeField] private GameObject panel;
    [Tooltip("Üst satır (oda adı).")]
    [SerializeField] private Text roomLabel;
    [Tooltip("Alt satır (yapılacak iş).")]
    [SerializeField] private Text taskLabel;
    [Tooltip("Kalan düşman sayacı. Sayaç gerekmeyen adımlarda gizlenir.")]
    [SerializeField] private Text counterLabel;

    [Header("Davranış")]
    [Tooltip("Adım bitince \"tamamlandı\" yazısının ekranda kalma süresi.")]
    [SerializeField] private float completedFlashDuration = 1.5f;
    [Tooltip("Normal görev yazısının rengi.")]
    [SerializeField] private Color taskColor = new Color(0.85f, 0.87f, 0.9f);
    [Tooltip("Tamamlandı yazısının rengi.")]
    [SerializeField] private Color completedColor = new Color(0.30f, 0.82f, 0.38f);
    [Tooltip("Açıksa tüm adımlar bitince panel bir süre sonra gizlenir (kapanış sekansı için).")]
    [SerializeField] private bool hideWhenAllComplete = true;
    [Tooltip("hideWhenAllComplete açıkken gizlenmeden önce beklenecek süre.")]
    [SerializeField] private float hideDelay = 3f;

    private GameProgress _progress;
    private EnemyClearObjective _counterSource;
    private int _lastRemaining = -1;
    private int _lastTotal = -1;

    private void Start()
    {
        if (panel == null) panel = gameObject;

        _progress = GameProgress.Instance;
        _progress.StageCompleted += OnStageCompleted;

        Refresh();
    }

    private void OnDestroy()
    {
        if (_progress != null) _progress.StageCompleted -= OnStageCompleted;
    }

    private void Update()
    {
        if (counterLabel == null || _counterSource == null) return;

        // Sayaci her karede yazmak yerine yalnizca degisince guncelliyoruz.
        int remaining = _counterSource.Remaining;
        int total = _counterSource.Total;
        if (remaining == _lastRemaining && total == _lastTotal) return;

        _lastRemaining = remaining;
        _lastTotal = total;
        counterLabel.text = $"Kalan: {remaining} / {total}";
    }

    private void OnStageCompleted(GameStage stage)
    {
        StopAllCoroutines();
        StartCoroutine(CompletedFlashRoutine(stage));
    }

    /// <summary>Biten görevi yeşil olarak kısa süre gösterir, sonra sıradakine geçer.</summary>
    private IEnumerator CompletedFlashRoutine(GameStage stage)
    {
        StageEntry entry = FindEntry(stage);

        if (taskLabel != null)
        {
            taskLabel.text = completedPrefix + (entry != null ? entry.task : stage.ToString());
            taskLabel.color = completedColor;
        }

        if (counterLabel != null) counterLabel.gameObject.SetActive(false);
        _counterSource = null;

        if (completedFlashDuration > 0f) yield return new WaitForSeconds(completedFlashDuration);

        Refresh();
    }

    /// <summary>Paneli sıradaki göreve göre günceller.</summary>
    public void Refresh()
    {
        if (taskLabel != null) taskLabel.color = taskColor;

        if (_progress.AllComplete)
        {
            if (roomLabel != null) roomLabel.text = allDoneText;
            if (taskLabel != null) taskLabel.text = "";
            if (counterLabel != null) counterLabel.gameObject.SetActive(false);
            _counterSource = null;

            if (hideWhenAllComplete) StartCoroutine(HideAfterDelay());
            return;
        }

        GameStage stage = _progress.CurrentStage;
        StageEntry entry = FindEntry(stage);

        if (roomLabel != null) roomLabel.text = entry != null ? entry.room : stage.ToString();
        if (taskLabel != null) taskLabel.text = entry != null ? entry.task : "";

        // Dusman temizleme adimiysa canli sayaci bagla.
        _counterSource = FindObjectiveFor(stage);
        _lastRemaining = -1;
        _lastTotal = -1;

        if (counterLabel != null) counterLabel.gameObject.SetActive(_counterSource != null);
    }

    /// <summary>Paneli gizler (kapanış sekansı buraya bağlanabilir).</summary>
    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
    }

    /// <summary>Paneli tekrar gösterir.</summary>
    public void Show()
    {
        if (panel != null) panel.SetActive(true);
    }

    private IEnumerator HideAfterDelay()
    {
        if (hideDelay > 0f) yield return new WaitForSeconds(hideDelay);
        Hide();
    }

    private StageEntry FindEntry(GameStage stage)
    {
        if (entries == null) return null;

        foreach (StageEntry entry in entries)
            if (entry != null && entry.stage == stage) return entry;

        return null;
    }

    /// <summary>Bu adımın düşman temizleme hedefini bulur; yoksa null (sayaç gizlenir).</summary>
    private static EnemyClearObjective FindObjectiveFor(GameStage stage)
    {
        foreach (EnemyClearObjective objective in
                 FindObjectsByType<EnemyClearObjective>(FindObjectsInactive.Include))
        {
            if (objective.Stage == stage) return objective;
        }

        return null;
    }
}
