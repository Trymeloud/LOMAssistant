using System;
using System.Linq;
using Mono.Cecil;

internal static class BuiltModChecks
{
    internal static void Run(string path, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        var module = assembly.MainModule;
        var context = module.Types.Single(t => t.FullName == "LOMAssistant.StoryContextPatch");
        var hooks = context.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "HarmonyPatch")).ToList();
        string[] expected = { "DiceMenuDialog.ExecuteRoll", "CustomMenuDialog.AddOption", "LeanLocalizationResolver.GetStoryText", "LuaEnvironment.LoadLuaFunction" };
        var targets = hooks.SelectMany(m => m.CustomAttributes.Where(a => a.AttributeType.Name == "HarmonyPatch"))
            .Select(a => ((TypeReference)a.ConstructorArguments[0].Value).Name + "." + a.ConstructorArguments[1].Value).ToArray();
        check(targets.OrderBy(s => s).SequenceEqual(expected.OrderBy(s => s)), "Compiled read-only story hooks changed");
        foreach (var hook in hooks)
        {
            check(hook.ReturnType.FullName == "System.Void" && hook.Parameters.All(p => !p.ParameterType.IsByReference), "Tracking must not replace results or skip game execution: " + hook.Name);
            check(hook.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand)
                .All(m => m.DeclaringType.FullName == "LOMAssistant.OptionInfoPatch"), "Tracking hook must only observe context: " + hook.Name);
        }
        var option = module.Types.Single(t => t.FullName == "LOMAssistant.OptionInfoPatch");
        foreach (string name in new[] { "DiceMenuDialog_ExecuteRoll_Prefix", "CustomMenuDialog_AddOption_Prefix", "LeanLocalizationResolver_GetStoryText_Remember", "LuaEnvironment_LoadLuaFunction_Prefix" })
            check(option.Methods.Single(m => m.Name == name).CustomAttributes.All(a => a.AttributeType.Name != "HarmonyPatch"), "Duplicate old context hook: " + name);
        var startup = module.Types.Single(t => t.FullName == "LOMAssistant.LOMAssistantPlugin").Methods.Single(m => m.Name == "ApplyPatches").Body.Instructions.ToList();
        int install = startup.FindIndex(i => i.Operand is TypeReference t && t.FullName == context.FullName);
        int hintToggle = startup.FindIndex(i => i.Operand is FieldReference f && f.Name == "EnableOptionInfo");
        check(install >= 0 && hintToggle > install, "Current context must be installed before the hint-enabled branch");
        var parse = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Story.StoryScripts").Methods.Single(m => m.Name == "Parse");
        var actionReads = parse.Body.Instructions.Where(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "LOMAssistant.LuaParse.Action")
            .Select(i => ((MethodReference)i.Operand).Name).ToList();
        check(actionReads.Contains("get_CanonicalDescription") && !actionReads.Contains("get_Description"), "Analysis must not parse Korean presentation strings");
        check(module.Resources.Any(r => r.Name == "LOMAssistant.flag-names-ko.tsv"), "Built DLL lacks the stable flag ID resource");
        check(module.Resources.Any(r => r.Name == "LOMAssistant.event-names-ko.tsv"), "Built DLL lacks the default contextual event translations");
        bool Calls(string type, string method, string owner, string called) => module.Types.Single(t => t.FullName == type).Methods.Single(m => m.Name == method)
            .Body.Instructions.Any(i => i.Operand is MethodReference r && r.DeclaringType.FullName == owner && r.Name == called);
        check(Calls("LOMAssistant.Features.Story.StoryReviewPanel", "DrawSettings", "LOMAssistant.KoreanText", "Warning")
            && Calls("LOMAssistant.Features.Story.StoryReviewPanel", "DrawSettings", "LOMAssistant.KoreanText", "ActionModeWarning"), "Settings warning composition must use Korean helpers");
        check(Calls("LOMAssistant.Features.Story.StoryReviewPanel", "BookBonus", "LOMAssistant.KoreanText", "BookBonusLine")
            && Calls("LOMAssistant.Features.Story.StoryReviewPanel", "ItemRows", "LOMAssistant.KoreanText", "Warning"), "Book stage and warning helpers must be wired into the built UI");
        check(Calls("LOMAssistant.Features.Combat.DuelActionForecast", "OnGUI", "LOMAssistant.KoreanText", "DuelForecast"), "Built duel forecast must use Korean composition");
        check(Calls("LOMAssistant.Features.Story.StoryReviewPanel", "AnytimeContext", "LOMAssistant.NativeKorean", "Text"), "Built quick-save memo must use a native message");
        var skip = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Extras.SkipActionPatch").Methods.Single(m => m.Name == "OnSkipClick");
        check(skip.Body.Instructions.Count(i => i.Operand is MethodReference r && r.DeclaringType.FullName == "LOMAssistant.TextConverter" && r.Name == "ForDisplay") == 2,
            "Both game SayDialog calls must explicitly localize before entering the game UI");
        var jump = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Story.StoryJump");
        foreach (var spec in new[] { (Name: "Recompute", Count: 3), (Name: "Walk", Count: 2) })
            check(jump.Methods.Single(m => m.Name == spec.Name).Body.Instructions.Count(i => i.Operand is MethodReference m
                && m.DeclaringType.FullName == "LOMAssistant.Features.Story.StoryUi" && m.Name == "Text") == spec.Count,
                "Built jump limitation notes must use Korean message IDs: " + spec.Name);
        var flagNotify = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Extras.FlagNotifyPatch")
            .Methods.Single(m => m.Name == "Notify");
        var noticeCalls = flagNotify.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => (MethodReference)i.Operand).ToList();
        check(noticeCalls.Any(m => m.DeclaringType.FullName == "LOMAssistant.NativeKorean" && m.Name == "get_Enabled")
            && noticeCalls.Any(m => m.DeclaringType.FullName == "LOMAssistant.KoreanLabels" && m.Name == "Flag")
            && noticeCalls.Any(m => m.DeclaringType.FullName == "LOMAssistant.KoreanText" && m.Name == "Get"),
            "Game flag notifications must compose Korean text from flag IDs before display");
        check(!noticeCalls.Any(m => m.DeclaringType.FullName == "LOMAssistant.TextConverter" && m.Name == "ForDisplay")
            && noticeCalls.Any(m => m.Name == "DisplayMessageText"),
            "Non-Korean game flag notifications must preserve the upstream message payload");
        var adapter = module.Types.Single(t => t.FullName == "LOMAssistant.NativeKorean");
        check(adapter.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand)
            .All(m => !m.DeclaringType.Name.Contains("StatModify") && !m.DeclaringType.Name.Contains("Random")), "Presentation must not mutate game stats or draw random numbers");
        var storyUi = module.Types.FirstOrDefault(t => t.FullName == "LOMAssistant.Features.Story.StoryUi");
        if (storyUi != null)
        {
            var when = storyUi.Methods.Single(m => m.Name == "When");
            check(when.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "LOMAssistant.KoreanStoryText" && m.Name == "Period"), "Story UI must render mission periods from date data");
            var missionTitle = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Story.StoryMission").Methods.Single(m => m.Name == "get_Title");
            check(missionTitle.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "LOMAssistant.KoreanLabels" && m.Name == "MissionName"), "Story mission titles must use the complete-name resolver");
        }
        var ai = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Ai.AiAnalysis");
        var aiMethods = ai.Methods.Concat(ai.NestedTypes.SelectMany(t => t.Methods)).Where(m => m.HasBody);
        var effectReads = aiMethods.SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference r
            && r.DeclaringType.FullName == "LOMAssistant.Features.Story.StoryScript").Select(i => ((MethodReference)i.Operand).Name).ToList();
        check(effectReads.Contains("CanonicalEffectsOn") && !effectReads.Contains("EffectsOn"), "Compiled AI route analysis must use canonical effects");
        var tools = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Ai.AiKnowledge").Methods.Single(m => m.Name == "get_Tools");
        check(tools.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "KoreanAiTools" && m.Name == "Localize"), "Built model tools lack Korean schemas");
        var persona = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Ai.AiPersona");
        check(persona.Methods.Single(m => m.Name == "Prompt").Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "AiPrompts" && m.Name == "KoreanPersona"), "Built persona prompt must use the central language entry point");
        var prompts = module.Types.Single(t => t.FullName == "LOMAssistant.Features.Ai.AiPrompts");
        check(prompts.Methods.Single(m => m.Name == "KoreanPersona").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "KoreanPrompt"), "Central persona entry point lacks native Korean history and voice rules");
        Console.WriteLine("Built DLL: read-only context hooks, independent hint setting, canonical analysis and flag resource verified");
    }
}
