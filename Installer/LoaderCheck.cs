using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace LOMAssistantInstaller;

/// <summary>
/// 诊断里的「加载器排查」：BepInEx 一次都没启动（没有 LogOutput.log）时，查游戏为什么没加载游戏目录里的 winhttp.dll。
/// 查的都是只读的东西：游戏目录里有哪些文件、正在运行的游戏实际加载了哪个 winhttp.dll、系统的已知 DLL 名单、
/// 针对 Mortal.exe 的映像劫持和漏洞防护设置、兼容模式、智能应用控制、代码完整性日志里的拦截记录、装了哪些杀毒软件。
/// 每一项单独 try，读不到就写原因，不影响别的项。
/// </summary>
internal static class LoaderCheck
{
	private static readonly string[] ProxyNames = { "winhttp.dll", "version.dll", "winmm.dll" };

	public static string Report(string game)
	{
		var sb = new StringBuilder();
		sb.AppendLine();
		sb.AppendLine("加载器排查：");
		Section(sb, "游戏目录里的文件", () => GameRoot(game));
		Section(sb, "BepInEx\\core 里的文件", () => CoreFiles(game));
		Section(sb, "正在运行的游戏加载了哪些代理 DLL", LoadedModules);
		Section(sb, "游戏是怎么启动的（命令行、由哪个程序启动）", LaunchedBy);
		Section(sb, "系统已知 DLL 名单（在名单里的名字，游戏只会加载系统自带的那份）", KnownDlls);
		Section(sb, "针对 Mortal.exe 的映像劫持、漏洞防护设置", Ifeo);
		Section(sb, "Mortal.exe 的兼容模式设置", CompatLayers);
		Section(sb, "DOORSTOP_ 开头的环境变量（DOORSTOP_DISABLE 有值时，没开 ignore_disable_switch 的加载器什么都不做）", DoorstopEnv);
		Section(sb, "智能应用控制", SmartAppControl);
		Section(sb, "代码完整性日志里和游戏有关的拦截记录（最近 14 天）", CodeIntegrity);
		Section(sb, "系统登记的杀毒软件", AntiVirus);
		return sb.ToString();
	}

	private static void Section(StringBuilder sb, string title, Func<IEnumerable<string>> lines)
	{
		sb.AppendLine("  " + title + "：");
		try
		{
			var list = lines().ToList();
			if (list.Count == 0)
			{
				sb.AppendLine("    （无）");
			}
			foreach (string line in list)
			{
				sb.AppendLine("    " + line);
			}
		}
		catch (Exception e)
		{
			sb.AppendLine("    读取失败：" + e.Message);
		}
	}

	private static IEnumerable<string> GameRoot(string game)
	{
		foreach (string file in Directory.GetFiles(game).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
		{
			var info = new FileInfo(file);
			yield return info.Name + "  " + info.Length + " 字节  " + info.LastWriteTime.ToString("yyyy-MM-dd HH:mm") + Attr(info);
		}
	}

	private static IEnumerable<string> CoreFiles(string game)
	{
		string core = Path.Combine(game, @"BepInEx\core");
		if (!Directory.Exists(core))
		{
			yield return "没有这个文件夹";
			yield break;
		}
		foreach (string file in Directory.GetFiles(core).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
		{
			var info = new FileInfo(file);
			yield return info.Name + "  " + info.Length + " 字节" + Attr(info);
		}
	}

	// 只读、隐藏、系统这些属性一般没关系，有的话写出来
	private static string Attr(FileInfo info)
	{
		FileAttributes odd = info.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Offline | FileAttributes.Encrypted);
		return odd != 0 ? "  [" + odd + "]" : "";
	}

	// 游戏开着时直接看进程加载的模块：winhttp.dll 从游戏目录加载、但 BepInEx 没启动，和压根加载的是系统那份，是两种不同的问题
	private static IEnumerable<string> LoadedModules()
	{
		Process[] games = Process.GetProcessesByName("Mortal");
		if (games.Length == 0)
		{
			yield return "游戏没在运行，看不到。把游戏开到标题画面，不要关，再导出一次诊断。";
			yield break;
		}
		foreach (Process p in games)
		{
			var found = new List<string>();
			string error = null;
			try
			{
				foreach (ProcessModule m in p.Modules)
				{
					string name = m.ModuleName.ToLowerInvariant();
					if (ProxyNames.Contains(name) || name.Contains("doorstop") || name.StartsWith("mono") || name.StartsWith("bepinex"))
					{
						found.Add(m.ModuleName + " ← " + m.FileName);
					}
				}
			}
			catch (Exception e)
			{
				error = "读不到进程 " + p.Id + " 的模块：" + e.Message;
			}
			if (error != null)
			{
				yield return error;
				continue;
			}
			yield return "进程 " + p.Id + "：" + (found.Count == 0 ? "没加载 winhttp/version/winmm、mono" : "");
			foreach (string line in found)
			{
				yield return "  " + line;
			}
		}
	}

	// 加载器按相对路径找 BepInEx：游戏不是由 Steam 直接启动（加速器、启动器、快捷方式的「起始位置」不对）时可能找不到
	private static IEnumerable<string> LaunchedBy()
	{
		var list = new List<string>();
		using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = 'Mortal.exe'"))
		{
			foreach (ManagementBaseObject p in searcher.Get())
			{
				list.Add("进程 " + p["ProcessId"] + " 命令行：" + (p["CommandLine"] ?? "（读不到）"));
				uint parent = Convert.ToUInt32(p["ParentProcessId"]);
				using (var parents = new ManagementObjectSearcher("SELECT Name, ExecutablePath FROM Win32_Process WHERE ProcessId = " + parent))
				{
					var found = parents.Get().Cast<ManagementBaseObject>().FirstOrDefault();
					list.Add("  由 " + (found != null ? found["Name"] + "（" + found["ExecutablePath"] + "）" : "进程 " + parent + "（已退出）") + " 启动");
				}
			}
		}
		if (list.Count == 0)
		{
			list.Add("游戏没在运行，看不到。");
		}
		return list;
	}

	private static IEnumerable<string> KnownDlls()
	{
		using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs"))
		{
			if (key == null)
			{
				yield return "读不到";
				yield break;
			}
			foreach (string value in key.GetValueNames())
			{
				string dll = Convert.ToString(key.GetValue(value)) ?? "";
				if (ProxyNames.Any(n => dll.Equals(n, StringComparison.OrdinalIgnoreCase)))
				{
					yield return value + " = " + dll + "（在名单里：游戏目录里同名的文件不会被加载）";
				}
			}
		}
	}

	private static IEnumerable<string> Ifeo()
	{
		foreach (string root in new[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Mortal.exe", @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\Mortal.exe" })
		{
			using (RegistryKey key = Registry.LocalMachine.OpenSubKey(root))
			{
				if (key == null)
				{
					continue;
				}
				foreach (string line in DumpKey(key, "HKLM\\" + root))
				{
					yield return line;
				}
			}
		}
	}

	private static IEnumerable<string> DumpKey(RegistryKey key, string path)
	{
		foreach (string value in key.GetValueNames())
		{
			object v = key.GetValue(value);
			string text = v is byte[] bytes ? BitConverter.ToString(bytes) : Convert.ToString(v);
			yield return path + "  " + value + " = " + text;
		}
		foreach (string sub in key.GetSubKeyNames())
		{
			using (RegistryKey child = key.OpenSubKey(sub))
			{
				if (child != null)
				{
					foreach (string line in DumpKey(child, path + "\\" + sub))
					{
						yield return line;
					}
				}
			}
		}
	}

	private static IEnumerable<string> CompatLayers()
	{
		foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
		{
			using (RegistryKey key = hive.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"))
			{
				if (key == null)
				{
					continue;
				}
				foreach (string value in key.GetValueNames())
				{
					if (value.IndexOf("Mortal", StringComparison.OrdinalIgnoreCase) >= 0 || value.IndexOf("LegendOfMortal", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						yield return (hive == Registry.CurrentUser ? "HKCU" : "HKLM") + "  " + value + " = " + key.GetValue(value);
					}
				}
			}
		}
	}

	// 别的用 Doorstop 的程序（Mod 管理器、开了 Mod 的其他游戏）启动子进程时会设 DOORSTOP_DISABLE；
	// 设进了用户或系统的环境变量、或 Steam 继承了它，从 Steam 开的游戏里加载器就会直接跳过
	private static IEnumerable<string> DoorstopEnv()
	{
		foreach (EnvironmentVariableTarget target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
		{
			foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables(target))
			{
				string name = Convert.ToString(e.Key);
				if (name.StartsWith("DOORSTOP", StringComparison.OrdinalIgnoreCase))
				{
					yield return (target == EnvironmentVariableTarget.Process ? "安装器继承的" : target == EnvironmentVariableTarget.User ? "用户" : "系统") + "  " + name + " = " + e.Value;
				}
			}
		}
	}

	private static IEnumerable<string> SmartAppControl()
	{
		using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy"))
		{
			object v = key?.GetValue("VerifiedAndReputablePolicyState");
			if (v == null)
			{
				yield return "没有这项设置（系统不支持或从没开过）";
				yield break;
			}
			int state = Convert.ToInt32(v);
			yield return state == 0 ? "关闭" : state == 1 ? "开启（会拦没有签名的 DLL）" : state == 2 ? "评估模式（只记录，不拦）" : "未知值 " + state;
		}
	}

	private static IEnumerable<string> CodeIntegrity()
	{
		var hits = new List<string>();
		var query = new EventLogQuery("Microsoft-Windows-CodeIntegrity/Operational", PathType.LogName, "*[System[TimeCreated[timediff(@SystemTime) <= 1209600000]]]") { ReverseDirection = true };
		using (var reader = new EventLogReader(query))
		{
			int scanned = 0;
			for (EventRecord e = reader.ReadEvent(); e != null && scanned < 5000 && hits.Count < 20; e = reader.ReadEvent(), scanned++)
			{
				using (e)
				{
					string text;
					try
					{
						text = e.FormatDescription() ?? "";
					}
					catch
					{
						text = string.Join(" ", e.Properties.Select(p => Convert.ToString(p.Value)).ToArray());
					}
					if (text.IndexOf("Mortal", StringComparison.OrdinalIgnoreCase) >= 0 || ProxyNames.Any(n => text.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 && text.IndexOf("LegendOfMortal", StringComparison.OrdinalIgnoreCase) >= 0))
					{
						string one = text.Replace("\r", " ").Replace("\n", " ");
						hits.Add(e.TimeCreated?.ToString("yyyy-MM-dd HH:mm") + "  事件 " + e.Id + "  " + (one.Length > 400 ? one.Substring(0, 400) + "…" : one));
					}
				}
			}
		}
		return hits;
	}

	private static IEnumerable<string> AntiVirus()
	{
		var list = new List<string>();
		using (var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct"))
		{
			foreach (ManagementBaseObject av in searcher.Get())
			{
				uint state = Convert.ToUInt32(av["productState"]);
				bool on = ((state >> 12) & 0xF) == 1;
				list.Add(av["displayName"] + "（" + (on ? "开着" : "关着") + "，状态码 0x" + state.ToString("X") + "）");
			}
		}
		return list;
	}
}
