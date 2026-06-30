using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WxKeyExtractor
{
    internal static class ProcessLocator
    {
        internal static Process FindWeChatProcess()
        {
            List<Process> candidates = new List<Process>();
            candidates.AddRange(Process.GetProcessesByName("Weixin"));
            candidates.AddRange(Process.GetProcessesByName("WeChat"));

            Process fallback = null;
            Process windowProcess = null;
            foreach (Process process in candidates)
            {
                try
                {
                    if (fallback == null)
                    {
                        fallback = process;
                    }
                    string reason = "未检测到微信进程";
                    if (IsReadyForHook(process, out reason))
                    {
                        return process;
                    }
                    if (windowProcess == null && process.MainWindowHandle != IntPtr.Zero)
                    {
                        windowProcess = process;
                    }
                }
                catch
                {
                }
            }
            return windowProcess ?? fallback;
        }

        internal static bool IsReadyForHook(Process process, out string reason)
        {
            reason = string.Empty;
            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    reason = "进程已退出";
                    return false;
                }
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    reason = "主窗口尚未创建";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(process.MainModule.FileName) ||
                    string.IsNullOrWhiteSpace(process.MainModule.FileVersionInfo.FileVersion))
                {
                    reason = "微信版本信息尚未就绪";
                    return false;
                }

                bool moduleLoaded = false;
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, "Weixin.dll", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(module.ModuleName, "WeChatWin.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        moduleLoaded = true;
                        break;
                    }
                }
                if (!moduleLoaded)
                {
                    reason = "微信核心模块尚未加载";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                reason = "无法读取微信进程信息：" + exception.Message;
                return false;
            }
        }

        internal static Task<Process> WaitUntilReadyAsync(Process preferred, TimeSpan timeout, CancellationToken cancellationToken)
        {
            return Task.Run(delegate
            {
                DateTime deadline = DateTime.UtcNow.Add(timeout);
                int stableProcessId = 0;
                int stableChecks = 0;
                string lastReason = string.Empty;

                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Process candidate = null;
                    try
                    {
                        if (preferred != null && !preferred.HasExited)
                        {
                            candidate = preferred;
                        }
                    }
                    catch
                    {
                    }
                    if (candidate == null)
                    {
                        candidate = FindWeChatProcess();
                    }

                    string reason = "未检测到微信进程";
                    if (candidate != null && IsReadyForHook(candidate, out reason))
                    {
                        if (candidate.Id == stableProcessId)
                        {
                            stableChecks++;
                        }
                        else
                        {
                            stableProcessId = candidate.Id;
                            stableChecks = 1;
                        }
                        if (stableChecks >= 6)
                        {
                            return candidate;
                        }
                    }
                    else
                    {
                        stableProcessId = 0;
                        stableChecks = 0;
                        if (!string.IsNullOrWhiteSpace(reason))
                        {
                            lastReason = reason;
                        }
                    }
                    Thread.Sleep(500);
                }
                throw new TimeoutException("等待微信主进程就绪超时" + (lastReason.Length == 0 ? string.Empty : "：" + lastReason));
            }, cancellationToken);
        }

        internal static string GetExecutablePath(Process process)
        {
            try
            {
                return process.MainModule.FileName;
            }
            catch
            {
                return FindInstalledExecutable();
            }
        }

        internal static string FindInstalledExecutable()
        {
            string[] registryKeys =
            {
                @"SOFTWARE\Tencent\WeChat",
                @"SOFTWARE\WOW6432Node\Tencent\WeChat",
                @"SOFTWARE\Tencent\Weixin"
            };

            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                foreach (string keyName in registryKeys)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                        using (RegistryKey key = baseKey.OpenSubKey(keyName))
                        {
                            string value = key == null ? null : key.GetValue("InstallPath") as string;
                            string found = FindExecutableUnder(value);
                            if (found != null)
                            {
                                return found;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            string[] knownPaths =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Tencent\WeChat\WeChat.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Tencent\WeChat\WeChat.exe"),
                @"D:\Program Files\Tencent\WeChat\WeChat.exe",
                @"D:\Program Files (x86)\Tencent\WeChat\WeChat.exe"
            };

            foreach (string path in knownPaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }

        internal static Task<Process> RestartAndWaitAsync(Process existing, string executablePath, CancellationToken cancellationToken)
        {
            return Task.Run(delegate
            {
                if (existing != null)
                {
                    try
                    {
                        existing.CloseMainWindow();
                        if (!existing.WaitForExit(5000))
                        {
                            existing.Kill();
                            existing.WaitForExit(5000);
                        }
                    }
                    catch
                    {
                        try { existing.Kill(); }
                        catch { }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });

                Process started = null;
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline && started == null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    started = FindWeChatProcess();
                    if (started == null)
                    {
                        Thread.Sleep(500);
                    }
                }
                if (started == null)
                {
                    throw new TimeoutException("重新启动微信后未检测到微信进程");
                }
                return WaitUntilReadyAsync(started, TimeSpan.FromSeconds(45), cancellationToken).GetAwaiter().GetResult();
            }, cancellationToken);
        }

        private static string FindExecutableUnder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }
            if (File.Exists(path))
            {
                return path;
            }
            foreach (string name in new[] { "Weixin.exe", "WeChat.exe" })
            {
                string candidate = Path.Combine(path, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
