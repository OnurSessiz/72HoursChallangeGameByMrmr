using UnityEngine;

/// <summary>
/// 3. şahıs kamera pivotunu mouse ile döndürür (yaw + pitch) ve pivotu karakterin
/// konumuna kilitler. ÖNEMLİ: cameraPivot karakterin CHILD'ı OLMAMALI; aksi halde
/// karakter dönünce pivot da döner ve hareket yönü kaymaya (yerinde fırıl fırıl dönme)
/// yol açar. Pivot bağımsızdır, sadece konumu takip eder; rotasyonu mouse belirler.
/// Kamera bu pivotun child'ı olarak arkaya/yukarıya offset'lenir.
/// </summary>
public class PlayerLook : MonoBehaviour
{
    [Header("Referanslar")]
    [SerializeField] private PlayerInputReader inputReader;
    [Tooltip("Döndürülecek kamera pivotu (kamera bunun child'ı). Karakterin child'ı OLMASIN.")]
    [SerializeField] private Transform cameraPivot;
    [Tooltip("Pivotun konumca takip edeceği hedef (genelde Player).")]
    [SerializeField] private Transform followTarget;
    [Tooltip("Pivotun omuz/baş hizasına göre yükseklik ofseti.")]
    [SerializeField] private Vector3 followOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Ayarlar")]
    [Tooltip("Yatay (sağa/sola) bakış hızı.")]
    [SerializeField] private float horizontalSensitivity = 0.1f;
    [Tooltip("Dikey (yukarı/aşağı) bakış hızı. Kamera çok fırlıyorsa bunu düşür.")]
    [SerializeField] private float verticalSensitivity = 0.05f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 70f;
    [SerializeField] private bool lockCursor = true;

    private float _yaw;
    private float _pitch;

    private void Start()
    {
        // Başlangıç açılarını mevcut pivot rotasyonundan al.
        Vector3 e = cameraPivot.eulerAngles;
        _yaw = e.y;
        _pitch = e.x;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void LateUpdate()
    {
        // Mouse delta zaten kare-bazlı; Time.deltaTime ile çarpma.
        Vector2 look = inputReader.Look;
        _yaw += look.x * horizontalSensitivity;
        _pitch -= look.y * verticalSensitivity;
        _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

        // Konumu takip et, rotasyonu mouse'tan mutlak olarak ata (feedback yok).
        cameraPivot.position = followTarget.position + followOffset;
        cameraPivot.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }
}