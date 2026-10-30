using UnityEngine;

namespace LOMAssistant.Features.Story;

/// <summary>
/// 更新内容：单独的一页（不属于哪个标签），列出每一版更新了什么，从题头右上角的版本号进，「返回」回到原来那一页。
/// 题头右上角一行小字：版本号和「更新内容」，GitHub 上有新版时显示「有新版本 X」，点了进这一页。不弹窗。
/// </summary>
public partial class StoryReviewPanel
{
	private bool _showChangelog;
	private Vector2 _changelogScroll;
	private bool _seenMarked;

	private static readonly string JadeHex = "#38735C";

	/// <summary>打开「更新内容」页；_tab 不动，返回时回到原来那一页。</summary>
	private void OpenChangelog()
	{
		_showChangelog = true;
		_changelogScroll = Vector2.zero;
	}

	private GUIStyle _hintStyle, _barStyle; // _barStyle：单行但要显示红字（Middle 出来的单行样式关了富文本）
	private float _hintScale;

	/// <summary>
	/// 题头关闭按钮左边一行小字「v2.4.2 · 更新内容」，点了进更新内容页。GitHub 上有新版时换成朱砂色的「v2.4.2 · 有新版本 2.4.3」；
	/// 刚更新过的这次启动是青色。返回占用后剩下的右边界。
	/// </summary>
	private float DrawUpdateHint(float top, float bandHeight, float right)
	{
		if (_hintStyle == null || _hintScale != _scale)
		{
			_hintScale = _scale;
			_barStyle = null;
			_hintStyle = new GUIStyle(Middle(_muted)) { alignment = TextAnchor.MiddleCenter };
		}
		string text = "v" + UpdateInfo.Current + " · " + (UpdateInfo.HasNewer ? "有新版本 " + UpdateInfo.Latest : "更新内容");
		Color color = UpdateInfo.HasNewer ? Cinnabar : UpdateInfo.JustUpdated ? Jade : InkMuted;
		if (UpdateInfo.JustUpdated && !_seenMarked)
		{
			// 记下这一版已经提示过，下次启动恢复成灰色
			_seenMarked = true;
			UpdateInfo.MarkSeen();
		}
		// 和题头的标签一样：悬停时字变深，正在看时加粗、下面画朱砂线
		_hintStyle.normal.textColor = _showChangelog && color == InkMuted ? Ink : color;
		_hintStyle.hover.textColor = color == InkMuted ? Ink : color;
		// 宽度按加粗量，选中前后位置不跳（标签也是这样定宽的）
		_hintStyle.fontStyle = FontStyle.Bold;
		float width = _hintStyle.CalcSize(new GUIContent(TextConverter.ForDisplay(text))).x;
		_hintStyle.fontStyle = _showChangelog ? FontStyle.Bold : FontStyle.Normal;
		var r = new Rect(right - width, top, width, bandHeight);
		if (GUI.Button(r, text, _hintStyle))
		{
			OpenChangelog();
		}
		if (_showChangelog)
		{
			Fill(new Rect(r.x - S(4), top + bandHeight, r.width + S(8), S(3)), Cinnabar);
		}
		return r.x - S(16);
	}

	private void DrawChangelog(Rect r)
	{
		float h = S(36);
		if (GUI.Button(new Rect(r.x, r.y, S(130), h), "← 返回", _button))
		{
			_showChangelog = false;
			return;
		}
		GUI.Label(new Rect(r.x + S(150), r.y, S(200), h), "更新内容", Middle(_heading));
		float y = r.y + h + S(10);
		if (UpdateInfo.HasNewer)
		{
			var bar = new Rect(r.x, y, r.width, S(44));
			Fill(bar, new Color(Cinnabar.r, Cinnabar.g, Cinnabar.b, 0.1f));
			float bw = S(150);
			GUI.Label(new Rect(bar.x + S(12), bar.y, bar.width - bw * 2 - S(40), bar.height),
				Red("有新版本 " + UpdateInfo.Latest) + "：退出游戏后运行新版的「咖啡伴侣安装器 Installer.exe」更新，存档和设置不受影响。", _barStyle ??= new GUIStyle(Middle(_label)) { richText = true });
			if (GUI.Button(new Rect(bar.xMax - bw * 2 - S(18), bar.y + S(4), bw, bar.height - S(8)), "GitHub 下载", _button))
			{
				Application.OpenURL(UpdateInfo.DownloadPage);
			}
			if (GUI.Button(new Rect(bar.xMax - bw - S(8), bar.y + S(4), bw, bar.height - S(8)), "夸克网盘下载", _button))
			{
				Application.OpenURL(UpdateInfo.QuarkPage);
			}
			y = bar.yMax + S(10);
		}
		var area = new Rect(r.x, y, r.width, r.yMax - y);
		DragScroll("changelog", area, ref _changelogScroll);
		GUILayout.BeginArea(area);
		_changelogScroll = GUILayout.BeginScrollView(_changelogScroll);
		float width = area.width - S(30);
		foreach (UpdateInfo.Entry entry in UpdateInfo.Entries)
		{
			GUILayout.BeginVertical(_card, GUILayout.Width(width));
			string tag = entry.Version > UpdateInfo.Current ? "　" + Red("新版本，还没装")
				: entry.Version == UpdateInfo.Current ? "　<color=" + JadeHex + ">当前版本</color>"
				: "";
			GUILayout.Label("<b>" + entry.Title + "</b>" + tag, _label);
			GUILayout.Space(S(4));
			GUILayout.Label(entry.Body, _rich);
			GUILayout.EndVertical();
		}
		GUILayout.EndScrollView();
		GUILayout.EndArea();
	}
}
