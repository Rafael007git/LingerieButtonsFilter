using HarmonyLib;
using System;

namespace LingerieButtonsFilter
{
    // Input spam suppressor for Player.log cleanup optimization
    [HarmonyPatch(typeof(PMC_Setting), "GetKeyDown")]
    public class LogShieldPatch
    {
        [HarmonyFinalizer]
        public static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception != null)
            {
                __result = false;
                return null;
            }
            return null;
        }
    }
}
