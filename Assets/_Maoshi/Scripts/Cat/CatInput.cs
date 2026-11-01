using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Maoshi.Cat
{
    /// <summary>
    /// Ввод для кота: клавиатура + мышь и геймпад.
    /// Действия создаются в коде, чтобы не зависеть от .inputactions-ассета.
    /// Живёт только у владельца кота (на чужих котах ввод не нужен).
    /// </summary>
    public sealed class CatInput : IDisposable
    {
        /// <summary>Открыто меню (пауза, настройки) — кот не слушается.</summary>
        public static bool Blocked { get; set; }

        /// <summary>Мышь над кнопкой или панелью интерфейса — клик мышью не считается толчком.</summary>
        public static bool PointerOverUI { get; set; }

        /// <summary>На экране окно с кнопками (аукцион, свалка) — курсор должен быть свободен.</summary>
        public static bool UIWantsCursor { get; set; }

        private readonly InputAction _move;
        private readonly InputAction _jump;
        private readonly InputAction _push;
        private readonly InputAction _crouch;

        public CatInput()
        {
            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _jump = new InputAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _push = new InputAction("Push", InputActionType.Button);
            _push.AddBinding("<Mouse>/leftButton");
            _push.AddBinding("<Keyboard>/f");
            _push.AddBinding("<Gamepad>/buttonWest");

            _crouch = new InputAction("Crouch", InputActionType.Button);
            _crouch.AddBinding("<Keyboard>/leftCtrl");
            _crouch.AddBinding("<Keyboard>/c");
            _crouch.AddBinding("<Gamepad>/buttonEast");
        }

        /// <summary>Кот крадётся (зажат C или Ctrl, B на геймпаде).</summary>
        public bool CrouchHeld => !Blocked && _crouch.IsPressed();

        /// <summary>Направление движения, длина от 0 до 1.</summary>
        public Vector2 Move => Blocked ? Vector2.zero : Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f);

        /// <summary>Прыжок нажат в этом кадре (читать в Update).</summary>
        public bool JumpPressed => !Blocked && _jump.WasPressedThisFrame();

        /// <summary>Толчок нажат в этом кадре (читать в Update).</summary>
        public bool PushPressed
        {
            get
            {
                if (Blocked || !_push.WasPressedThisFrame()) return false;
                // Клик по кнопке интерфейса — не толчок.
                bool byMouse = _push.activeControl != null && _push.activeControl.device is Mouse;
                return !(byMouse && PointerOverUI);
            }
        }

        public void Enable()
        {
            _move.Enable();
            _jump.Enable();
            _push.Enable();
            _crouch.Enable();
        }

        public void Disable()
        {
            _move.Disable();
            _jump.Disable();
            _push.Disable();
            _crouch.Disable();
        }

        public void Dispose()
        {
            _move.Dispose();
            _jump.Dispose();
            _push.Dispose();
            _crouch.Dispose();
        }
    }
}
