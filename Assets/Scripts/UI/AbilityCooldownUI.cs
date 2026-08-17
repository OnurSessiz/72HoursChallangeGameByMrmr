using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dash/Dodge için cooldown göstergesi:
///   - Radial (dönen) karartma süpürmesi cooldown boyunca saat yönünde açılır.
///   - Ortada kalan süre sayısı.
///   - Hazır olunca: glyph parlar, hafif nabız (nefes) + hazır anında "punch" büyümesi + glow.
/// Veriyi PlayerController'ın cooldown accessor'larından okur.
/// </summary>
public class AbilityCooldownUI : MonoBehaviour
{
    public enum Ability { Dash, Dodge }

    [Header("Kaynak")]
    [SerializeField] private PlayerController player;
    [SerializeField] private Ability ability = Ability.Dash;

    [Header("Referanslar")]
    [Tooltip("Nabız/punch uygulanacak kök (genelde ikon gövdesi).")]
    [SerializeField] private RectTransform scaleTarget;
    [Tooltip("Radial Filled Image (dönen karartma).")]
    [SerializeField] private Image sweep;
    [Tooltip("Kalan süre yazısı.")]
    [SerializeField] private Text countdown;
    [Tooltip("Cooldown'da soluklaşacak glyph/isim.")]
    [SerializeField] private Graphic glyph;
    [Tooltip("Hazırken nabız atan parlama (opsiyonel).")]
    [SerializeField] private Image glow;

    [Header("Ayar")]
    [SerializeField] private float dimAlpha = 0.35f;
    [SerializeField] private float readyPunchScale = 1.25f;
    [SerializeField] private float readyPunchTime = 0.22f;
    [SerializeField] private float idlePulseAmount = 0.04f;
    [SerializeField] private float pulseSpeed = 3f;

    private bool _wasReady = true;
    private float _punchT;
    private Vector3 _baseScale = Vector3.one;

    private void Reset()
    {
        scaleTarget = transform as RectTransform;
    }

    private void Awake()
    {
        if (player == null) player = FindFirstObjectByType<PlayerController>();
        if (scaleTarget == null) scaleTarget = transform as RectTransform;
        _baseScale = scaleTarget.localScale;
    }

    private void Update()
    {
        if (player == null) return;

        float cd = ability == Ability.Dash ? player.DashCooldown : player.DodgeCooldown;
        float rem = ability == Ability.Dash ? player.DashCooldownRemaining : player.DodgeCooldownRemaining;
        bool ready = rem <= 0.0001f;

        // Dönen karartma: cooldown oranı kadar dolu, hazırken kaybolur.
        if (sweep != null)
        {
            sweep.enabled = !ready;
            sweep.fillAmount = cd > 0f ? Mathf.Clamp01(rem / cd) : 0f;
        }

        // Kalan süre: >=1sn tam sayı, <1sn ondalık.
        if (countdown != null)
        {
            countdown.enabled = !ready;
            if (!ready)
                countdown.text = rem >= 1f ? Mathf.Ceil(rem).ToString("0") : rem.ToString("0.0");
        }

        // Glyph parlaklığı.
        if (glyph != null)
        {
            Color c = glyph.color;
            c.a = ready ? 1f : dimAlpha;
            glyph.color = c;
        }

        // Hazır olma anında punch tetikle.
        if (ready && !_wasReady) _punchT = readyPunchTime;
        _wasReady = ready;

        // Ölçek: punch (sönümlenen) ya da hazırken hafif nefes.
        float scale = 1f;
        if (_punchT > 0f)
        {
            _punchT -= Time.deltaTime;
            float k = Mathf.Clamp01(_punchT / readyPunchTime); // 1 -> 0
            scale = Mathf.Lerp(1f, readyPunchScale, k);
        }
        else if (ready)
        {
            scale = 1f + Mathf.Sin(Time.time * pulseSpeed) * idlePulseAmount;
        }
        if (scaleTarget != null) scaleTarget.localScale = _baseScale * scale;

        // Glow: hazırken nabız, cooldownda kapalı.
        if (glow != null)
        {
            float a = ready ? 0.35f + Mathf.Sin(Time.time * pulseSpeed) * 0.2f : 0f;
            Color gc = glow.color;
            gc.a = Mathf.Max(0f, a);
            glow.color = gc;
        }
    }
}
