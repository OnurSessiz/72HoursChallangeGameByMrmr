using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Ekranın üstünde beliren büyük boss can barı. Yalnızca dövüş sırasında görünür:
/// BossFight.BeginFight() ile açılır (yumuşak fade), boss ölünce kısa gecikmeyle kapanır.
///
/// İki katmanlı bar: kırmızı "fill" hasarı anında gösterir, arkasındaki açık renkli
/// "chip" kısa bir gecikmeyle onu takip eder — vurulan can miktarı gözle görünür olur.
///
/// Referanslar boş bırakılırsa sahnedeki BossFight ve üzerindeki Health otomatik bulunur.
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    [Header("Referanslar")]
    [Tooltip("Boss'un Health bileşeni. Boşsa BossFight'ın üzerinde aranır.")]
    [SerializeField] private Health bossHealth;
    [Tooltip("Barın ne zaman görüneceğini belirler. Boşsa sahnede aranır.")]
    [SerializeField] private BossFight bossFight;
    [Tooltip("Barın tamamını saydamlaştıran CanvasGroup. Boşsa bu objede aranır/eklenir.")]
    [SerializeField] private CanvasGroup group;

    [Header("Bar parçaları")]
    [Tooltip("Kırmızı dolgu (Image, Type = Filled, Horizontal).")]
    [SerializeField] private Image fill;
    [Tooltip("Gecikmeli hasar izi; fill'in arkasında durur. Opsiyonel.")]
    [SerializeField] private Image chip;
    [Tooltip("Boss adı yazısı. Opsiyonel.")]
    [SerializeField] private Text nameLabel;
    [Tooltip("'450 / 500' gibi sayısal can. Opsiyonel.")]
    [SerializeField] private Text valueLabel;

    [Header("Görünüm")]
    [Tooltip("Barda yazacak isim.")]
    [SerializeField] private string bossName = "BOSS";
    [Tooltip("Açıksa bar yalnızca dövüş başladıktan sonra görünür (jump scare'den önce gizli).")]
    [SerializeField] private bool showOnlyDuringFight = true;
    [Tooltip("Açılma/kapanma süresi (saniye).")]
    [SerializeField] private float fadeDuration = 0.4f;
    [Tooltip("Boss öldükten kaç saniye sonra bar kaybolsun.")]
    [SerializeField] private float hideAfterDeathDelay = 2f;

    [Header("Hasar izi (chip)")]
    [Tooltip("Hasardan sonra izin erimeye başlaması için beklenen süre.")]
    [SerializeField] private float chipDelay = 0.35f;
    [Tooltip("İzin erime hızı (saniyede canın yüzde kaçı; 0.6 = %60).")]
    [SerializeField] private float chipSpeed = 0.6f;

    private float _targetAlpha;
    private float _lastDamageTime = Mathf.NegativeInfinity;
    private float _deathTime = Mathf.NegativeInfinity;
    private bool _forcedVisible;   // Show() ile elle açıldıysa dövüş şartını atla.

    private void Awake()
    {
        if (bossFight == null) bossFight = FindAnyObjectByType<BossFight>();
        if (bossHealth == null && bossFight != null) bossHealth = bossFight.GetComponent<Health>();
        if (group == null)
        {
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        // UI tıklamaları engellemesin.
        group.interactable = false;
        group.blocksRaycasts = false;

        // Dövüş başlamadan görünmesin.
        group.alpha = showOnlyDuringFight ? 0f : 1f;
        _targetAlpha = group.alpha;

        if (nameLabel != null) nameLabel.text = bossName;
    }

    private void OnEnable()
    {
        if (bossHealth == null) return;
        bossHealth.HealthChanged += OnHealthChanged;
        bossHealth.Died += OnDied;
    }

    private void OnDisable()
    {
        if (bossHealth == null) return;
        bossHealth.HealthChanged -= OnHealthChanged;
        bossHealth.Died -= OnDied;
    }

    private void Start()
    {
        // Health.Start'taki ilk bildirimi kaçırmış olabiliriz; barı mevcut cana göre kur.
        if (bossHealth != null)
        {
            SetBarInstant(bossHealth.Normalized);
            UpdateValueLabel(bossHealth.Current, bossHealth.MaxHealth);
        }
    }

    private void Update()
    {
        UpdateVisibility();
        UpdateFade();
        UpdateChip();
    }

    /// <summary>Barı elle açar (BossFight.onFightStart UnityEvent'ine bağlanabilir).</summary>
    public void Show()
    {
        _forcedVisible = true;
        _targetAlpha = 1f;
    }

    /// <summary>Barı elle kapatır (cutscene, boss kaçtı vb.).</summary>
    public void Hide()
    {
        _forcedVisible = false;
        _targetAlpha = 0f;
    }

    /// <summary>Dövüş durumu ve ölüm zamanına göre hedef saydamlığı belirler.</summary>
    private void UpdateVisibility()
    {
        if (bossHealth != null && bossHealth.IsDead)
        {
            // Ölümden sonra barın boşaldığı görülsün, sonra kaybolsun.
            if (Time.time >= _deathTime + hideAfterDeathDelay) _targetAlpha = 0f;
            return;
        }

        if (_forcedVisible) return;

        bool fighting = !showOnlyDuringFight || (bossFight != null && bossFight.FightStarted);
        _targetAlpha = fighting ? 1f : 0f;
    }

    private void UpdateFade()
    {
        if (group == null) return;
        if (Mathf.Approximately(group.alpha, _targetAlpha)) return;

        float step = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;
        group.alpha = Mathf.MoveTowards(group.alpha, _targetAlpha, step);
    }

    /// <summary>Hasar izini gecikmeli olarak gerçek cana yaklaştırır.</summary>
    private void UpdateChip()
    {
        if (chip == null || fill == null) return;
        if (chip.fillAmount <= fill.fillAmount)
        {
            chip.fillAmount = fill.fillAmount;
            return;
        }

        if (Time.time < _lastDamageTime + chipDelay) return;

        chip.fillAmount = Mathf.MoveTowards(
            chip.fillAmount, fill.fillAmount, chipSpeed * Time.unscaledDeltaTime);
    }

    private void OnHealthChanged(float current, float max)
    {
        float normalized = max > 0f ? current / max : 0f;

        if (fill != null)
        {
            // Can arttıysa (heal) iz de anında yetişsin.
            if (chip != null && normalized > fill.fillAmount) chip.fillAmount = normalized;
            fill.fillAmount = normalized;
        }

        _lastDamageTime = Time.time;
        UpdateValueLabel(current, max);
    }

    private void OnDied()
    {
        _deathTime = Time.time;
        if (fill != null) fill.fillAmount = 0f;
        UpdateValueLabel(0f, bossHealth != null ? bossHealth.MaxHealth : 0f);
    }

    private void SetBarInstant(float normalized)
    {
        if (fill != null) fill.fillAmount = normalized;
        if (chip != null) chip.fillAmount = normalized;
    }

    private void UpdateValueLabel(float current, float max)
    {
        if (valueLabel == null) return;
        valueLabel.text = Mathf.CeilToInt(current) + " / " + Mathf.CeilToInt(max);
    }
}
