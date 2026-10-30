using System.Collections;
using HarmonyLib;
using LOMAssistant.Config;
using Mortal.Combat;
using Mortal.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LOMAssistant.Features.Combat;

internal sealed class DuelActionForecast : MonoBehaviour
{
	private const int ControlId = 0x444146; // "DAF"
	private const float DefaultWidth = 128f;
	private const float DefaultMargin = 8f;
	private const float ResizeHandleSize = 18f;

	private static DuelActionForecast _instance;

	private GUIStyle _style;
	private float _scale = -1f;
	private bool _visible;
	private Rect _rect;
	private bool _placed;
	private bool _horizontalLayout;
	private bool _dragging;
	private bool _resizing;
	private Vector2 _pointerOffset;
	private int _screenWidth;
	private int _screenHeight;
	private bool _geometryDirty;

	public static void Ensure(GameObject host)
	{
		if (_instance == null)
		{
			_instance = host.GetComponent<DuelActionForecast>() ?? host.AddComponent<DuelActionForecast>();
		}
	}

	public static void Show()
	{
		if (_instance != null && ConfigManager.EnableDuelActionForecast.Value)
		{
			_instance._visible = true;
		}
	}

	public static void Hide()
	{
		if (_instance != null)
		{
			_instance._visible = false;
		}
	}

	private void Update()
	{
		if ((_dragging || _resizing) && !Input.GetMouseButton(0))
		{
			StopPointerInteraction();
		}
	}

	private void OnGUI()
	{
		if (!_visible || !ConfigManager.EnableDuelActionForecast.Value || UiHider.Active
			|| SceneManager.GetActiveScene().name != "Combat")
		{
			return;
		}

		CombatManager manager = CombatManager.Instance;
		CombatUIManager ui = CombatUIManager.Instance;
		if (manager == null || ui == null)
		{
			return;
		}

		CombatActionResult enemyAction = Traverse.Create(manager).Field("_enemyActionResult").GetValue<CombatActionResult>();
		if (enemyAction == null)
		{
			return;
		}

		float scale = LOMAssistant.PanelTheme.UiScale;
		if (scale <= 0f)
		{
			return;
		}
		if (!Mathf.Approximately(scale, _scale))
		{
			_scale = scale;
			_style = PanelTheme.Toast(scale, 18);
			_style.alignment = TextAnchor.MiddleCenter;
			_style.padding = new RectOffset(
				Mathf.RoundToInt(12f * scale),
				Mathf.RoundToInt(12f * scale),
				Mathf.RoundToInt(6f * scale),
				Mathf.RoundToInt(6f * scale));
		}

		string actionName = ActionName(enemyAction.Action);
		string text = NativeKorean.Enabled ? KoreanText.DuelForecast(actionName, false, false) : "敌方预选\n\n" + actionName;
		string horizontalText = NativeKorean.Enabled ? KoreanText.DuelForecast(actionName, true, false) : "敌方预选 " + actionName;
		CombatActionController enemy = manager.EnemyAction;
		CombatActionController player = manager.PlayerAction;
		if (enemy != null && player != null && enemy.CanUseAnyAction()
			&& enemy.Stat.IntentionRate > 0f
			&& player.Stat.IntentionRate < enemy.Stat.IntentionRate)
		{
			string adaptive = NativeKorean.Text(KoreanMessage.EnemyAdaptive, "可能临场应变");
			text += "\n\n" + adaptive;
			horizontalText += " · " + adaptive;
		}

		EnsureRect(scale, text, horizontalText);
		HandlePointerInput(text, horizontalText);
		string displayText = TextConverter.ForDisplay(_horizontalLayout ? horizontalText : text);
		EnsureContentFits(displayText, scale);
		GUI.Box(_rect, displayText, PanelTheme.Sync(_style));
		DrawResizeHandle();
		InputGuard.Report("DuelForecast", _rect);
	}

	private void EnsureContentFits(string text, float scale)
	{
		float requiredHeight = _style.CalcHeight(new GUIContent(text), _rect.width);
		if (_rect.height + 0.5f < requiredHeight)
		{
			_rect.height = requiredHeight;
			ClampRect(scale, text);
			if (_resizing)
			{
				_geometryDirty = true;
			}
		}
	}

	private void EnsureRect(float scale, string text, string horizontalText)
	{
		if (_placed && (_screenWidth != Screen.width || _screenHeight != Screen.height))
		{
			_placed = false;
		}
		if (_placed)
		{
			return;
		}

		_screenWidth = Screen.width;
		_screenHeight = Screen.height;
		float margin = DefaultMargin * scale;
		float width = ConfigManager.DuelForecastWidth.Value > 0f
			? ConfigManager.DuelForecastWidth.Value * Screen.width
			: DefaultWidth * scale;
		float height = ConfigManager.DuelForecastHeight.Value > 0f
			? ConfigManager.DuelForecastHeight.Value * Screen.height
			: _style.CalcHeight(new GUIContent(text), width);
		_rect = new Rect(
			ConfigManager.DuelForecastX.Value >= 0f
				? ConfigManager.DuelForecastX.Value * Screen.width
				: Screen.width - width - margin,
			ConfigManager.DuelForecastY.Value >= 0f
				? ConfigManager.DuelForecastY.Value * Screen.height
				: (Screen.height - height) * 0.5f,
			width,
			height);
		_horizontalLayout = ConfigManager.DuelForecastHeight.Value > 0f
			&& _rect.height < _style.CalcHeight(new GUIContent(TextConverter.ForDisplay(text)), width);
		ClampRect(scale, TextConverter.ForDisplay(_horizontalLayout ? horizontalText : text));
		_placed = true;
	}

	private void HandlePointerInput(string verticalText, string horizontalText)
	{
		Event e = Event.current;
		if (e == null || e.button != 0)
		{
			return;
		}

		Rect resizeHandle = GetResizeHandleRect();
		if (e.type == EventType.MouseDown && _rect.Contains(e.mousePosition))
		{
			_resizing = resizeHandle.Contains(e.mousePosition);
			_dragging = !_resizing;
			_pointerOffset = e.mousePosition - new Vector2(_rect.x, _rect.y);
			GUIUtility.hotControl = ControlId;
			e.Use();
			return;
		}

		if (e.type == EventType.MouseDrag && GUIUtility.hotControl == ControlId)
		{
			if (_resizing)
			{
				_rect.width = e.mousePosition.x - _rect.x;
				_rect.height = e.mousePosition.y - _rect.y;
				float verticalHeight = _style.CalcHeight(
					new GUIContent(TextConverter.ForDisplay(verticalText)), _rect.width);
				_horizontalLayout = _rect.height + 0.5f < verticalHeight;
			}
			else if (_dragging)
			{
				_rect.x = e.mousePosition.x - _pointerOffset.x;
				_rect.y = e.mousePosition.y - _pointerOffset.y;
			}
			ClampRect(_scale, TextConverter.ForDisplay(_horizontalLayout ? horizontalText : verticalText));
			_geometryDirty = true;
			e.Use();
			return;
		}

		if (e.rawType == EventType.MouseUp && GUIUtility.hotControl == ControlId)
		{
			StopPointerInteraction();
			e.Use();
		}
	}

	private void StopPointerInteraction()
	{
		if (_geometryDirty)
		{
			SaveGeometry();
		}
		_dragging = false;
		_resizing = false;
		_geometryDirty = false;
		if (GUIUtility.hotControl == ControlId)
		{
			GUIUtility.hotControl = 0;
		}
	}

	private void SaveGeometry()
	{
		ConfigManager.DuelForecastX.Value = _rect.x / Screen.width;
		ConfigManager.DuelForecastY.Value = _rect.y / Screen.height;
		ConfigManager.DuelForecastWidth.Value = _rect.width / Screen.width;
		ConfigManager.DuelForecastHeight.Value = _rect.height / Screen.height;
	}

	private void ClampRect(float scale, string text)
	{
		float margin = DefaultMargin * scale;
		float maxWidth = Mathf.Max(1f, Screen.width - margin * 2f);
		float maxHeight = Mathf.Max(1f, Screen.height - margin * 2f);
		float minWidth = _horizontalLayout
			? _style.CalcSize(new GUIContent(text)).x
			: _style.CalcSize(new GUIContent("敌")).x;
		minWidth = Mathf.Min(minWidth, maxWidth);
		_rect.width = Mathf.Clamp(_rect.width, minWidth, maxWidth);
		float minHeight = Mathf.Min(_style.CalcHeight(new GUIContent(text), _rect.width), maxHeight);
		_rect.height = Mathf.Clamp(_rect.height, minHeight, maxHeight);
		_rect.x = Mathf.Clamp(_rect.x, margin, Screen.width - margin - _rect.width);
		_rect.y = Mathf.Clamp(_rect.y, margin, Screen.height - margin - _rect.height);
	}

	private Rect GetResizeHandleRect()
	{
		float size = ResizeHandleSize * _scale;
		return new Rect(_rect.xMax - size, _rect.yMax - size, size, size);
	}

	private void DrawResizeHandle()
	{
		Rect handle = GetResizeHandleRect();
		Color previous = GUI.color;
		GUI.color = new Color(1f, 1f, 1f, 0.55f);
		for (int i = 0; i < 3; i++)
		{
			float offset = (i + 1) * handle.width / 4f;
			GUI.DrawTexture(new Rect(handle.x + offset, handle.yMax - offset, handle.width - offset, 1f), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(handle.xMax - offset, handle.y + offset, 1f, handle.height - offset), Texture2D.whiteTexture);
		}
		GUI.color = previous;
	}

	private static string ActionName(CombatActionType action)
	{
		return action switch
		{
			CombatActionType.嘴砲 => "嘴攻",
			CombatActionType.捅人 => "捅人",
			CombatActionType.暗器 => "暗器",
			CombatActionType.備揍 => "备揍",
			CombatActionType.絕招1 or CombatActionType.絕招2 or CombatActionType.絕招3 => "绝招",
			_ => "无动作"
		};
	}
}

internal static class DuelActionForecastPatch
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(CombatUIManager), nameof(CombatUIManager.ShowAction))]
	private static void ShowAction_Postfix(ref IEnumerator __result)
	{
		if (ConfigManager.EnableDuelActionForecast.Value && __result != null)
		{
			__result = ShowForecastAfterActionControls(__result);
		}
		else
		{
			DuelActionForecast.Hide();
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(CombatUIManager), nameof(CombatUIManager.HideAction))]
	private static void HideAction_Prefix()
	{
		DuelActionForecast.Hide();
	}

	private static IEnumerator ShowForecastAfterActionControls(IEnumerator showAction)
	{
		while (showAction.MoveNext())
		{
			yield return showAction.Current;
		}
		DuelActionForecast.Show();
	}
}
