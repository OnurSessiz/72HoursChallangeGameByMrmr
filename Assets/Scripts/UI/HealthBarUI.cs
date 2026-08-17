using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayerHealth'i dinleyip can barının dolgu oranını günceller.
/// fill: Image (Type = Filled, Horizontal). Referanslar boşsa sahnede otomatik aranır.
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    [SerializeField] private PlayerHealth health;
    [Tooltip("Dolgu Image'i (Type = Filled, Horizontal).")]
    [SerializeField] private Image fill;
    [Tooltip("Opsiyonel: '80 / 100' gibi yazı. Boş bırakılabilir.")]
    [SerializeField] private Text label;

    private void Start()
    {
        if (health == null) health = FindFirstObjectByType<PlayerHealth>();
        if (health == null) return;

        health.HealthChanged += OnHealthChanged;
        OnHealthChanged(health.Current, health.MaxHealth);
    }

    private void OnDestroy()
    {
        if (health != null) health.HealthChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(float current, float max)
    {
        if (fill != null) fill.fillAmount = max > 0f ? current / max : 0f;
        if (label != null) label.text = Mathf.CeilToInt(current) + " / " + Mathf.CeilToInt(max);
    }
}
