using HarmonyLib;
using UnityEngine;

namespace SilksongRL
{
    public enum MoveDirection { None = 0, Left = 1, Right = 2 }
    public enum LookDirection { None = 0, Up = 1, Down = 2 }

    /// <summary>
    /// Available action space types for different encounters (used by IBossEncounter,
    /// inherited from the original RL action-space design).
    /// </summary>
    public enum ActionSpaceType
    {
        Basic,
        Extended
    }

    public class Action
    {
        public MoveDirection move;
        public LookDirection look;
        public bool jump;
        public bool attack;
        public bool dash;

        public Action()
        {
            move = MoveDirection.None;
            look = LookDirection.None;
            jump = false;
            attack = false;
            dash = false;
        }
    }

    // Patch over the legacy input system because that's what the
    // Debug Mod uses
    [HarmonyPatch(typeof(Input), "GetKeyDown", typeof(KeyCode))]
    public static class GetKeyDownPatch
    {
        public static bool Prefix(KeyCode key, ref bool __result)
        {
            if (!RLManager.isLoggingEnabled)
                return true;

            if (key == KeyCode.F5)
            {
                if (RLManager.simulateF5Press)
                {
                    __result = true;
                    RLManager.simulateF5Press = false;
                    return false;
                }
            }

            return true;
        }
    }

}
