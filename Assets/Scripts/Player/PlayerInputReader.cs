using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tüm input'un okunduğu TEK nokta. Başka hiçbir script "new MainControls()" yapmamalı;
/// herkes bu component'in property/event'lerini okur.
/// Sürekli değerler property olarak, anlık aksiyonlar C# event olarak sunulur.
/// </summary>
public class PlayerInputReader : MonoBehaviour, MainControls.ICharacterControlsActions
{
    // --- Sürekli değerler ---
    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }

    // --- Anlık olaylar (buton basımları) ---
    public event Action JumpPressed;
    public event Action DashPressed;
    public event Action AttackPressed;
    public event Action DodgePressed;

    private MainControls _controls;

    private void OnEnable()
    {
        // Input asset'ini oluştur, callback'leri bağla ve enable et.
        if (_controls == null)
        {
            _controls = new MainControls();
            _controls.CharacterControls.AddCallbacks(this);
        }
        _controls.Enable();
    }

    private void OnDisable()
    {
        // Sızıntıyı önlemek için map'i disable et.
        _controls?.Disable();
    }

    private void OnDestroy()
    {
        _controls?.Dispose();
    }

    // --- ICharacterControlsActions implementasyonu ---
    // (Generate edilmiş interface; asset'te dodge/attack küçük harf olsa da metod adları büyük.)

    public void OnMove(InputAction.CallbackContext context)
    {
        Move = context.ReadValue<Vector2>();
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        Look = context.ReadValue<Vector2>();
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (context.performed) JumpPressed?.Invoke();
    }

    public void OnDash(InputAction.CallbackContext context)
    {
        if (context.performed) DashPressed?.Invoke();
    }

    public void OnDodge(InputAction.CallbackContext context)
    {
        if (context.performed) DodgePressed?.Invoke();
    }

    public void OnAttack(InputAction.CallbackContext context)
    {
        if (context.performed) AttackPressed?.Invoke();
    }
}
