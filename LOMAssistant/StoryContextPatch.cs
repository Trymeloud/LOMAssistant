using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;

namespace LOMAssistant;

// Read-only story tracking belongs to the panel, independently of hint settings.
internal static class StoryContextPatch
{
    [HarmonyPrefix, HarmonyPatch(typeof(DiceMenuDialog), "ExecuteRoll")]
    private static void Dice(string[] __0) => OptionInfoPatch.DiceMenuDialog_ExecuteRoll_Prefix(__0);

    [HarmonyPrefix, HarmonyPatch(typeof(CustomMenuDialog), "AddOption")]
    private static void Menu(string __0) => OptionInfoPatch.CustomMenuDialog_AddOption_Prefix(__0);

    [HarmonyPostfix, HarmonyPriority(Priority.Last), HarmonyPatch(typeof(LeanLocalizationResolver), "GetStoryText")]
    private static void Remember(string key, string __result) => OptionInfoPatch.LeanLocalizationResolver_GetStoryText_Remember(key, __result);

    [HarmonyPrefix, HarmonyPatch(typeof(LuaEnvironment), "LoadLuaFunction")]
    private static void Script(string luaString, string friendlyName) => OptionInfoPatch.LuaEnvironment_LoadLuaFunction_Prefix(luaString, friendlyName);
}
