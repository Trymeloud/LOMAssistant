using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace LOMAssistant;

/// <summary>
/// 导出诊断：把排查问题需要的文件打成一个压缩包放到桌面（桌面写不进去就依次试「文档」、游戏目录、游戏存档文件夹、临时文件夹），
/// 路径复制到剪贴板，并在资源管理器里选中它。
/// 包含：本 Mod 最近 3 个日志、BepInEx 日志、游戏日志（Player.log）、本 Mod 配置、plugins 插件清单和版本信息。
/// 不包含存档。
/// </summary>
public static class DiagnosticsExport
{
	public static string Export()
	{
		string name = "咖啡伴侣诊断_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip";
		FileStream stream = CreateWritable(name, out string zipPath);
		using (stream)
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
		{
			AddText(zip, "说明.txt", Summary());
			string logs = Path.Combine(Paths.PluginPath, "LOMAssistant", "logs");
			if (Directory.Exists(logs))
			{
				foreach (string log in Directory.GetFiles(logs, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(3))
				{
					AddFile(zip, log, "Mod日志/" + Path.GetFileName(log));
				}
			}
			// AI 调试记录：玩家在 AI 设置里打开过才有（里面是发给 AI 的内容，没有 API Key）
			AddFile(zip, Path.Combine(logs, "ai_debug.txt"), "Mod日志/ai_debug.txt");
			AddFile(zip, Path.Combine(Paths.BepInExRootPath, "LogOutput.log"), "BepInEx/LogOutput.log");
			AddConfig(zip, Path.Combine(Paths.ConfigPath, "LOMAssistant.cfg"), "BepInEx/LOMAssistant.cfg");
			AddFile(zip, Path.Combine(Application.persistentDataPath, "Player.log"), "游戏日志/Player.log");
			AddFile(zip, Path.Combine(Application.persistentDataPath, "Player-prev.log"), "游戏日志/Player-prev.log");
		}
		FileLogger.Log("导出诊断：" + zipPath);
		try
		{
			GUIUtility.systemCopyBuffer = zipPath;
		}
		catch
		{
		}
		try
		{
			System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + zipPath + "\"");
		}
		catch
		{
		}
		return zipPath;
	}

	// 有的电脑桌面没有写入权限（公司电脑、权限被改过、杀毒软件拦着），按顺序找第一个写得进去的地方
	private static FileStream CreateWritable(string name, out string path)
	{
		var dirs = new[]
		{
			Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
			Paths.GameRootPath,
			Application.persistentDataPath,
			Path.GetTempPath(),
		};
		Exception last = null;
		foreach (string dir in dirs)
		{
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
			{
				continue;
			}
			try
			{
				path = Path.Combine(dir, name);
				return File.Create(path);
			}
			catch (Exception e)
			{
				last = e;
				FileLogger.Log("导出诊断：" + dir + " 写不进去（" + e.Message + "），换下一个位置");
			}
		}
		throw last ?? new IOException("没有可以写入的文件夹");
	}

	private static string Summary()
	{
		var sb = new StringBuilder();
		sb.AppendLine("咖啡伴侣 诊断信息  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		sb.AppendLine("Mod 版本：" + Assembly.GetExecutingAssembly().GetName().Version);
		sb.AppendLine("游戏版本：" + Application.version + "，Unity " + Application.unityVersion);
		sb.AppendLine("系统：" + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? "，64 位" : "，32 位"));
		sb.AppendLine("游戏目录：" + Paths.GameRootPath);
		sb.AppendLine();
		sb.AppendLine("BepInEx\\plugins 里的插件：");
		try
		{
			foreach (string dll in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
			{
				string version = "";
				try
				{
					version = AssemblyName.GetAssemblyName(dll).Version.ToString();
				}
				catch
				{
				}
				sb.AppendLine("  " + dll.Substring(Paths.PluginPath.Length).TrimStart('\\', '/') + "  " + version);
			}
		}
		catch (Exception e)
		{
			sb.AppendLine("  （读取失败：" + e.Message + "）");
		}
		return sb.ToString();
	}

	private static void AddText(ZipArchive zip, string name, string text)
	{
		using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(true)))
		{
			writer.Write(text);
		}
	}

	// 配置文件里的 AI API Key 不进诊断包（玩家会把诊断包发到 GitHub、群里）
	private static void AddConfig(ZipArchive zip, string path, string name)
	{
		try
		{
			if (!File.Exists(path))
			{
				return;
			}
			string text;
			using (var input = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)))
			{
				text = input.ReadToEnd();
			}
			text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^(\s*AiApiKey\s*=)[^\r\n]*", "$1 （已隐藏）");
			AddText(zip, name, text);
		}
		catch (Exception e)
		{
			AddText(zip, name + ".读取失败.txt", e.Message);
		}
	}

	// 日志文件可能正被游戏写着，用共享读取的方式复制
	private static void AddFile(ZipArchive zip, string path, string name)
	{
		try
		{
			if (!File.Exists(path))
			{
				return;
			}
			using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
			using (Stream output = zip.CreateEntry(name).Open())
			{
				input.CopyTo(output);
			}
		}
		catch (Exception e)
		{
			AddText(zip, name + ".读取失败.txt", e.Message);
		}
	}
}
