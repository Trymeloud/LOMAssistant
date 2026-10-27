using HarmonyLib;
using UnityEngine;

namespace LOMAssistant;

/// <summary>
/// 界面语言不是简体时，本 Mod 用 IMGUI 画的窗口、小窗、提示（文字都是简体写的）画之前转成繁体或换成韩语、日语（见 TextConverter.ForDisplay）。
/// IMGUI 画字最后都经过 GUIStyle 的这两个内部方法：画的那一下把文字换掉，画完换回来，不改动调用方的 GUIContent。
/// 量字宽、字高（CalcSize 等，自动排版也用它们）同样按换过的文字量，换成更长的韩语、日语也不会被截掉。
/// 输入框正在编辑时走的是另一条路（DrawWithTextSelection），打的字原样显示，不受影响。
/// </summary>
public static class DisplayTextPatch
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(GUIStyle), "Internal_Draw")]
	public static void Draw_Prefix(GUIContent __1, out string __state) => __state = Swap(__1);

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(GUIStyle), "Internal_Draw")]
	public static void Draw_Finalizer(GUIContent __1, string __state) => Restore(__1, __state);

	[HarmonyPrefix]
	[HarmonyPatch(typeof(GUIStyle), "Internal_Draw2")]
	public static void Draw2_Prefix(GUIContent __1, out string __state) => __state = Swap(__1);

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(GUIStyle), "Internal_Draw2")]
	public static void Draw2_Finalizer(GUIContent __1, string __state) => Restore(__1, __state);

	[HarmonyPrefix]
	[HarmonyPatch(typeof(GUIStyle), nameof(GUIStyle.CalcSize))]
	public static void CalcSize_Prefix(GUIContent __0, out string __state) => __state = Swap(__0);

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(GUIStyle), nameof(GUIStyle.CalcSize))]
	public static void CalcSize_Finalizer(GUIContent __0, string __state) => Restore(__0, __state);

	[HarmonyPrefix]
	[HarmonyPatch(typeof(GUIStyle), nameof(GUIStyle.CalcHeight))]
	public static void CalcHeight_Prefix(GUIContent __0, out string __state) => __state = Swap(__0);

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(GUIStyle), nameof(GUIStyle.CalcHeight))]
	public static void CalcHeight_Finalizer(GUIContent __0, string __state) => Restore(__0, __state);

	[HarmonyPrefix]
	[HarmonyPatch(typeof(GUIStyle), nameof(GUIStyle.CalcMinMaxWidth))]
	public static void CalcMinMaxWidth_Prefix(GUIContent __0, out string __state) => __state = Swap(__0);

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(GUIStyle), nameof(GUIStyle.CalcMinMaxWidth))]
	public static void CalcMinMaxWidth_Finalizer(GUIContent __0, string __state) => Restore(__0, __state);

	/// <summary>换成当前界面语言的文字，返回原来的文字；没换返回 null。</summary>
	private static string Swap(GUIContent content)
	{
		if (content == null || string.IsNullOrEmpty(content.text))
		{
			return null;
		}
		string original = content.text;
		// 开头带 Verbatim 记号的（AI 的回答这类本来就是界面语言写的）不查表转换，只换颜色
		string converted = original[0] == TextConverter.Verbatim ? PanelTheme.MapColors(original) : PanelTheme.MapColors(TextConverter.ForDisplay(original));
		if (converted == original)
		{
			return null;
		}
		content.text = converted;
		return original;
	}

	private static void Restore(GUIContent content, string original)
	{
		if (original != null)
		{
			content.text = original;
		}
	}
}
