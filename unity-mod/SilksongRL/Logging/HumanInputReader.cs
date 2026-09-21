using UnityEngine;
using UnityEngine.InputSystem;

namespace SilksongRL
{
    public static class HumanInputReader
    {
        private static bool warnedNoGamepad = false;

        public static Action ReadCurrentAction()
        {
            var a = new Action();

            var pad = Gamepad.current;
            if (pad == null)
            {
                if (!warnedNoGamepad)
                {
                    RLManager.StaticLogger?.LogWarning("[HumanInputReader] Gamepad.current es null — no se detecta ningún control.");
                    warnedNoGamepad = true;
                }
                return a; // Action vacía en vez de tirar excepción
            }
            warnedNoGamepad = false;

            bool left = pad.leftStick.left.isPressed;
            bool right = pad.leftStick.right.isPressed;
            a.move = left && !right ? MoveDirection.Left
                   : right && !left ? MoveDirection.Right
                   : MoveDirection.None;

            bool up = pad.leftStick.up.isPressed;
            bool down = pad.leftStick.down.isPressed;
            a.look = up && !down ? LookDirection.Up
                   : down && !up ? LookDirection.Down
                   : LookDirection.None;

            a.jump = pad.buttonSouth.isPressed;
            a.attack = pad.buttonWest.isPressed;
            a.dash = pad.rightTrigger.isPressed; // sin gate por ActionSpaceType

            return a;
        }
    }
}
