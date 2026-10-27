using System.Collections.Generic;
using LOMAssistant.Config;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LOMAssistant;

/// <summary>
/// 隐藏界面看 CG（类似 galgame 的右键隐藏）：剧情里按热键（默认鼠标右键，可改成别的键）隐藏对话框、标题等文字界面和本 Mod 的窗口，
/// 只留背景、CG 和立绘，方便截图；再按一次、点左键或按别的键恢复（截图键、功能键、修饰键不算），恢复的那一下不会推进对话。
/// 判断哪些界面要藏：画布里有文字或按钮的藏，只有图片的（背景、CG、立绘）留着。
/// </summary>
public class UiHider : MonoBehaviour
{
	public static bool Active { get; private set; }

	/// <summary>恢复之后，触发恢复的那次按键松开之前都不交给游戏。</summary>
	public static bool Swallowing => Active || _swallowUntilRelease;

	private static bool _swallowUntilRelease;
	private static UiHider _instance;
	private readonly List<Canvas> _hidden = new List<Canvas>();
	private int _hiddenFrame;

	public static void Ensure(GameObject host)
	{
		if (_instance == null)
		{
			_instance = host.GetComponent<UiHider>() ?? host.AddComponent<UiHider>();
			InputGuard.Ensure();
		}
	}

	/// <summary>回想（对话记录）开着的时候右键不隐藏界面。</summary>
	private static bool LogOpen()
	{
		Mortal.Story.StoryManager story = Mortal.Story.StoryManager.Instance;
		return story != null && story.GetFieldValue<bool>("_logOpen");
	}

	private static bool InStory()
	{
		return SceneManager.GetActiveScene().name == "Story";
	}

	/// <summary>
	/// 行动选择（江陵等地按行动点选做什么，BreakOptionButton）、地点选择（PositionOptionButton）开着：画面上没有对白和 CG 可看，
	/// 不隐藏界面，右键交还给游戏（游戏的右键和 ESC 一样打开菜单，被拿走就打不开）。对话选项、掷骰时照旧隐藏。
	/// 选项按钮在菜单关掉后会停用，FindObjectOfType 只找启用的，所以找到就是菜单开着。
	/// </summary>
	private static bool ActionMenuOpen()
	{
		return FindObjectOfType<Mortal.Core.BreakOptionButton>() != null || FindObjectOfType<Mortal.Core.PositionOptionButton>() != null;
	}

	/// <summary>这次右键是拿来隐藏界面的（热键是鼠标右键时）：游戏的输入回调不要再处理它（右键先于本组件的 Update 送到游戏）。</summary>
	public static bool ClaimsRightClick(InputControl control)
	{
		return _instance != null && !Active && ConfigManager.HideUIKey.Value == KeyCode.Mouse1 && InStory()
			&& control != null && Mouse.current != null && control == Mouse.current.rightButton && !EventPicker.IsWaiting && !LogOpen() && !InputGuard.ModWindowOpen
			&& !ActionMenuOpen();
	}

	private void Update()
	{
		if (_swallowUntilRelease && !AnyHeld())
		{
			_swallowUntilRelease = false;
		}
		if (Active)
		{
			if (!InStory())
			{
				Restore();
			}
			// 和 galgame 一样，再按热键、点左键、按别的键都恢复；截图（F12、PrintScreen）、功能键、Alt+Tab 这类修饰键不算
			else if (Time.frameCount > _hiddenFrame && (TogglePressed() || RestorePressed()))
			{
				Restore();
				_swallowUntilRelease = true;
			}
			return;
		}
		// 开着 Mod 的窗口（比如 F9、剧情回顾）时右键不隐藏界面，免得想关窗口却把界面全藏了
		if (!InStory() || EventPicker.IsWaiting || TextInputFocused() || LogOpen() || InputGuard.ModWindowOpen)
		{
			return;
		}
		if (TogglePressed() && !InputGuard.PointerOverModWindow && !ActionMenuOpen())
		{
			Hide();
		}
	}

	private static bool TogglePressed()
	{
		return InputGuard.HotkeyDown(ConfigManager.HideUIKey.Value);
	}

	private static bool RestorePressed()
	{
		Mouse m = Mouse.current;
		if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame))
		{
			return true;
		}
		if (Keyboard.current != null)
		{
			foreach (KeyControl k in Keyboard.current.allKeys)
			{
				if (k != null && k.wasPressedThisFrame && !KeepsHidden(k.keyCode))
				{
					return true;
				}
			}
		}
		Gamepad pad = Gamepad.current;
		if (pad != null)
		{
			foreach (var b in new[] { pad.buttonSouth, pad.buttonEast, pad.buttonWest, pad.buttonNorth, pad.startButton, pad.selectButton, pad.leftShoulder, pad.rightShoulder })
			{
				if (b.wasPressedThisFrame)
				{
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>截图、功能键（F1～F12 常被录屏、显卡软件占用）、修饰键（Alt+Tab、Win 键）按了不恢复界面。</summary>
	private static bool KeepsHidden(Key key)
	{
		return (key >= Key.F1 && key <= Key.F12) || key == Key.PrintScreen
			|| key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftCtrl || key == Key.RightCtrl
			|| key == Key.LeftShift || key == Key.RightShift || key == Key.LeftMeta || key == Key.RightMeta || key == Key.Tab;
	}

	private static bool TextInputFocused()
	{
		return GUIUtility.keyboardControl != 0;
	}

	private void Hide()
	{
		_hidden.Clear();
		foreach (Canvas canvas in FindObjectsOfType<Canvas>())
		{
			if (canvas == null || !canvas.enabled || !canvas.gameObject.activeInHierarchy || canvas.name.StartsWith("LOMAssistant"))
			{
				continue;
			}
			// 只管最外层画布下面、自己这一层直接显示的内容：里面有文字或按钮就藏
			if (HasTextOrControls(canvas))
			{
				canvas.enabled = false;
				_hidden.Add(canvas);
			}
		}
		Active = true;
		_hiddenFrame = Time.frameCount;
		FileLogger.Log("[隐藏界面] 隐藏 " + _hidden.Count + " 个画布：" + string.Join("、", _hidden.ConvertAll(c => c.name).ToArray()));
	}

	private static bool HasTextOrControls(Canvas canvas)
	{
		foreach (Graphic g in canvas.GetComponentsInChildren<Graphic>(false))
		{
			if (!g.enabled || g.canvas != canvas)
			{
				continue;
			}
			if ((g is Text t && !string.IsNullOrEmpty(t.text)) || (g is TMP_Text tmp && !string.IsNullOrEmpty(tmp.text)))
			{
				return true;
			}
		}
		foreach (Selectable s in canvas.GetComponentsInChildren<Selectable>(false))
		{
			if (s.enabled && s.GetComponentInParent<Canvas>() == canvas)
			{
				return true;
			}
		}
		return false;
	}

	private void Restore()
	{
		foreach (Canvas canvas in _hidden)
		{
			if (canvas != null)
			{
				canvas.enabled = true;
			}
		}
		_hidden.Clear();
		Active = false;
	}

	private static bool AnyHeld()
	{
		if (Keyboard.current != null && Keyboard.current.anyKey.isPressed) return true;
		Mouse m = Mouse.current;
		if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed)) return true;
		Gamepad pad = Gamepad.current;
		if (pad != null)
		{
			foreach (var b in new[] { pad.buttonSouth, pad.buttonEast, pad.buttonWest, pad.buttonNorth, pad.startButton, pad.selectButton, pad.leftShoulder, pad.rightShoulder, pad.dpad.up, pad.dpad.down, pad.dpad.left, pad.dpad.right })
			{
				if (b.isPressed) return true;
			}
		}
		return false;
	}
}
