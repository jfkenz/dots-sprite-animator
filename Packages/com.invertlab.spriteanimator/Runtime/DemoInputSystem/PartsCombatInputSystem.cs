using UnityEngine;
using UnityEngine.InputSystem;

namespace InvertLab.Sprites.DOTS
{
    public static class PartsCombatInputSystem
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Install()
        {
            PartsCombatKeyboard.IsHeld = IsHeld;
            PartsCombatKeyboard.FirePressed = () => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        static bool IsHeld(KeyCode key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            switch (key)
            {
                case KeyCode.A: return keyboard.aKey.isPressed;
                case KeyCode.D: return keyboard.dKey.isPressed;
                case KeyCode.W: return keyboard.wKey.isPressed;
                case KeyCode.S: return keyboard.sKey.isPressed;
                case KeyCode.LeftArrow: return keyboard.leftArrowKey.isPressed;
                case KeyCode.RightArrow: return keyboard.rightArrowKey.isPressed;
                case KeyCode.UpArrow: return keyboard.upArrowKey.isPressed;
                case KeyCode.DownArrow: return keyboard.downArrowKey.isPressed;
                default: return false;
            }
        }
    }
}
