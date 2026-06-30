using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace WxKeyExtractor
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wx_key.dll");
                    using (NativeWxKey native = new NativeWxKey(dllPath))
                    {
                        return native.IsReady ? 0 : 2;
                    }
                }
                catch
                {
                    return 1;
                }
            }

            if (args.Length >= 2 && string.Equals(args[0], "--probe-init", StringComparison.OrdinalIgnoreCase))
            {
                string outputPath = args.Length >= 3 ? args[2] : null;
                try
                {
                    int processId = int.Parse(args[1]);
                    string dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wx_key.dll");
                    using (NativeWxKey native = new NativeWxKey(dllPath))
                    {
                        native.ProbeInitialize(processId);
                    }
                    WriteProbeResult(outputPath, "OK");
                    return 0;
                }
                catch (Exception exception)
                {
                    WriteProbeResult(outputPath, "ERROR=" + exception.Message);
                    return 3;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static void WriteProbeResult(string path, string value)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                File.WriteAllText(path, value, new UTF8Encoding(false));
            }
        }
    }
}
