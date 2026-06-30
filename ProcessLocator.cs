using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace WxKeyExtractor
{
    internal sealed class NativeWxKey : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool InitializeHookDelegate(uint processId);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool PollKeyDataDelegate(IntPtr buffer, int bufferSize);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool GetStatusMessageDelegate(IntPtr buffer, int bufferSize, ref int statusCode);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void CleanupHookDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetLastErrorMsgDelegate();

        private IntPtr _module;
        private readonly InitializeHookDelegate _initializeHook;
        private readonly PollKeyDataDelegate _pollKeyData;
        private readonly GetStatusMessageDelegate _getStatusMessage;
        private readonly CleanupHookDelegate _cleanupHook;
        private readonly GetLastErrorMsgDelegate _getLastError;

        internal bool IsReady { get { return _module != IntPtr.Zero; } }

        internal NativeWxKey(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("缺少 wx_key.dll", path);
            }

            _module = LoadLibrary(path);
            if (_module == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法加载 wx_key.dll；请确认程序为 x64 且已安装 VC++ 2015-2022 x64 运行库");
            }

            try
            {
                _initializeHook = GetFunction<InitializeHookDelegate>("InitializeHook");
                _pollKeyData = GetFunction<PollKeyDataDelegate>("PollKeyData");
                _getStatusMessage = GetFunction<GetStatusMessageDelegate>("GetStatusMessage");
                _cleanupHook = GetFunction<CleanupHookDelegate>("CleanupHook");
                _getLastError = GetFunction<GetLastErrorMsgDelegate>("GetLastErrorMsg");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal string Capture(int processId, TimeSpan timeout, IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            IntPtr keyBuffer = Marshal.AllocHGlobal(128);
            try
            {
                ClearBuffer(keyBuffer, 128);
                Report(progress, "正在初始化微信进程 Hook...");

                if (!_initializeHook((uint)processId))
                {
                    throw new InvalidOperationException("初始化 Hook 失败：" + ReadLastError());
                }

                DateTime deadline = DateTime.UtcNow.Add(timeout);
                string lastStatus = string.Empty;
                Report(progress, "Hook 已初始化，正在等待数据库 Key...");

                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ClearBuffer(keyBuffer, 128);

                    if (_pollKeyData(keyBuffer, 128))
                    {
                        string value = Marshal.PtrToStringAnsi(keyBuffer) ?? string.Empty;
                        value = value.Trim().Trim('\0');
                        Match match = Regex.Match(value, "(?i)(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])");
                        if (match.Success)
                        {
                            return match.Value.ToLowerInvariant();
                        }
                        if (value.Length > 0)
                        {
                            throw new InvalidOperationException("原生组件返回了无法识别的 Key 格式");
                        }
                    }

                    string status = ReadStatus();
                    if (status.Length > 0 && !string.Equals(status, lastStatus, StringComparison.Ordinal))
                    {
                        lastStatus = status;
                        Report(progress, status);
                    }

                    if (cancellationToken.WaitHandle.WaitOne(500))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }

                throw new TimeoutException("获取 Key 超时。请保持微信已登录，或勾选“自动重启微信后获取”重试。");
            }
            finally
            {
                try { _cleanupHook(); }
                catch { }
                Marshal.FreeHGlobal(keyBuffer);
            }
        }

        internal void ProbeInitialize(int processId)
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException("processId");
            }

            try
            {
                if (!_initializeHook((uint)processId))
                {
                    throw new InvalidOperationException("初始化 Hook 失败：" + ReadLastError());
                }
            }
            finally
            {
                try { _cleanupHook(); }
                catch { }
            }
        }

        private string ReadStatus()
        {
            IntPtr buffer = Marshal.AllocHGlobal(512);
            try
            {
                ClearBuffer(buffer, 512);
                int statusCode = 0;
                bool ok = _getStatusMessage(buffer, 512, ref statusCode);
                string message = PtrToUtf8String(buffer, 511);
                if (message.Length > 0)
                {
                    return message.Trim();
                }
                return ok ? string.Empty : ReadLastError();
            }
            catch
            {
                return string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private string ReadLastError()
        {
            try
            {
                IntPtr value = _getLastError();
                string message = value == IntPtr.Zero ? null : PtrToUtf8String(value, 4096);
                return string.IsNullOrWhiteSpace(message) ? "未知错误" : message.Trim();
            }
            catch
            {
                return "未知错误";
            }
        }

        private T GetFunction<T>(string name) where T : class
        {
            IntPtr address = GetProcAddress(_module, name);
            if (address == IntPtr.Zero)
            {
                throw new MissingMethodException("wx_key.dll 缺少导出函数：" + name);
            }
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        private static void ClearBuffer(IntPtr buffer, int size)
        {
            for (int i = 0; i < size; i++)
            {
                Marshal.WriteByte(buffer, i, 0);
            }
        }

        private static string PtrToUtf8String(IntPtr value, int maximumBytes)
        {
            if (value == IntPtr.Zero)
            {
                return string.Empty;
            }

            int length = 0;
            while (length < maximumBytes && Marshal.ReadByte(value, length) != 0)
            {
                length++;
            }
            if (length == 0)
            {
                return string.Empty;
            }

            byte[] bytes = new byte[length];
            Marshal.Copy(value, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static void Report(IProgress<string> progress, string message)
        {
            if (progress != null)
            {
                progress.Report(message);
            }
        }

        public void Dispose()
        {
            if (_module != IntPtr.Zero)
            {
                FreeLibrary(_module);
                _module = IntPtr.Zero;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string functionName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr module);
    }
}
