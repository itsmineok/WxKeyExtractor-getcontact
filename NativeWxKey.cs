using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WxKeyExtractor
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox _processText;
        private readonly TextBox _keyText;
        private readonly TextBox _logText;
        private readonly Button _detectButton;
        private readonly Button _startButton;
        private readonly Button _stopButton;
        private readonly Button _copyButton;
        private readonly TextBox _dataPathText;
        private readonly Button _browseButton;
        private readonly CheckBox _followAccountCheck;
        private readonly Button _contactsButton;
        private readonly CheckBox _restartCheck;
        private readonly NumericUpDown _timeoutInput;
        private readonly ToolStripStatusLabel _statusLabel;
        private CancellationTokenSource _cancellation;
        private Process _process;
        private readonly System.Windows.Forms.Timer _accountTimer;
        private string _currentStorage;

        internal MainForm()
        {
            Text = "微信数据库 Key 获取工具";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(760, 620);
            Size = new Size(900, 700);
            Font = new Font("Microsoft YaHei UI", 9F);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 2;
            layout.RowCount = 11;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label notice = new Label();
            notice.Text = "仅用于本人或已获明确授权的本机微信数据。Key 不会联网、上传或自动保存。";
            notice.ForeColor = Color.DarkRed;
            notice.Dock = DockStyle.Fill;
            notice.TextAlign = ContentAlignment.MiddleLeft;
            notice.AutoEllipsis = true;
            layout.Controls.Add(notice, 0, 0);
            layout.SetColumnSpan(notice, 2);

            layout.Controls.Add(CreateLabel("微信进程"), 0, 1);
            FlowLayoutPanel processRow = CreateFlowRow();
            _processText = new TextBox();
            _processText.ReadOnly = true;
            _processText.Width = 470;
            _detectButton = new Button();
            _detectButton.Text = "检测微信";
            _detectButton.AutoSize = true;
            _detectButton.Click += DetectButton_Click;
            processRow.Controls.Add(_processText);
            processRow.Controls.Add(_detectButton);
            layout.Controls.Add(processRow, 1, 1);

            layout.Controls.Add(CreateLabel("获取选项"), 0, 2);
            FlowLayoutPanel optionRow = CreateFlowRow();
            _restartCheck = new CheckBox();
            _restartCheck.Text = "自动重启微信后获取";
            _restartCheck.AutoSize = true;
            _restartCheck.Margin = new Padding(3, 7, 16, 3);
            Label timeoutLabel = new Label();
            timeoutLabel.Text = "超时（秒）";
            timeoutLabel.AutoSize = true;
            timeoutLabel.Margin = new Padding(3, 8, 3, 3);
            _timeoutInput = new NumericUpDown();
            _timeoutInput.Minimum = 30;
            _timeoutInput.Maximum = 300;
            _timeoutInput.Value = 120;
            _timeoutInput.Width = 70;
            optionRow.Controls.Add(_restartCheck);
            optionRow.Controls.Add(timeoutLabel);
            optionRow.Controls.Add(_timeoutInput);
            layout.Controls.Add(optionRow, 1, 2);

            layout.Controls.Add(CreateLabel("操作"), 0, 3);
            FlowLayoutPanel actionRow = CreateFlowRow();
            _startButton = new Button();
            _startButton.Text = "开始获取";
            _startButton.AutoSize = true;
            _startButton.Click += StartButton_Click;
            _stopButton = new Button();
            _stopButton.Text = "停止";
            _stopButton.Enabled = false;
            _stopButton.AutoSize = true;
            _stopButton.Click += StopButton_Click;
            actionRow.Controls.Add(_startButton);
            actionRow.Controls.Add(_stopButton);
            layout.Controls.Add(actionRow, 1, 3);

            layout.Controls.Add(CreateLabel("数据库 Key"), 0, 4);
            _keyText = new TextBox();
            _keyText.ReadOnly = false;
            _keyText.Dock = DockStyle.Fill;
            _keyText.Font = new Font("Consolas", 10F);
            layout.Controls.Add(_keyText, 1, 4);

            layout.Controls.Add(new Label(), 0, 5);
            _copyButton = new Button();
            _copyButton.Text = "复制 Key";
            _copyButton.Enabled = false;
            _copyButton.AutoSize = true;
            _copyButton.Click += CopyButton_Click;
            FlowLayoutPanel copyRow = CreateFlowRow();
            copyRow.Controls.Add(_copyButton);
            layout.Controls.Add(copyRow, 1, 5);

            layout.Controls.Add(CreateLabel("数据目录"), 0, 6);
            FlowLayoutPanel dataRow = CreateFlowRow();
            _dataPathText = new TextBox();
            _dataPathText.Width = 455;
            _browseButton = new Button();
            _browseButton.Text = "选择目录";
            _browseButton.AutoSize = true;
            _browseButton.Click += BrowseButton_Click;
            _followAccountCheck = new CheckBox();
            _followAccountCheck.Text = "自动跟随账号";
            _followAccountCheck.Checked = true;
            _followAccountCheck.AutoSize = true;
            _followAccountCheck.Margin = new Padding(8, 7, 3, 3);
            _followAccountCheck.CheckedChanged += FollowAccountCheck_CheckedChanged;
            dataRow.Controls.Add(_dataPathText);
            dataRow.Controls.Add(_browseButton);
            dataRow.Controls.Add(_followAccountCheck);
            layout.Controls.Add(dataRow, 1, 6);

            layout.Controls.Add(CreateLabel("数据提取"), 0, 7);
            FlowLayoutPanel exportRow = CreateFlowRow();
            _contactsButton = new Button();
            _contactsButton.Text = "导出联系人 CSV";
            _contactsButton.AutoSize = true;
            _contactsButton.Click += ContactsButton_Click;
            exportRow.Controls.Add(_contactsButton);
            layout.Controls.Add(exportRow, 1, 7);

            Label exportNotice = new Label();
            exportNotice.Text = "数据库仅以只读方式访问；CSV 使用 UTF-8 BOM，Excel 打开中文不会乱码。";
            exportNotice.ForeColor = Color.DimGray;
            exportNotice.Dock = DockStyle.Fill;
            exportNotice.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(exportNotice, 0, 8);
            layout.SetColumnSpan(exportNotice, 2);

            Label logLabel = new Label();
            logLabel.Text = "运行记录（不会记录 Key）";
            logLabel.Dock = DockStyle.Fill;
            logLabel.TextAlign = ContentAlignment.BottomLeft;
            layout.Controls.Add(logLabel, 0, 9);
            layout.SetColumnSpan(logLabel, 2);

            _logText = new TextBox();
            _logText.Multiline = true;
            _logText.ReadOnly = true;
            _logText.ScrollBars = ScrollBars.Vertical;
            _logText.Dock = DockStyle.Fill;
            _logText.Font = new Font("Consolas", 9F);
            layout.Controls.Add(_logText, 0, 10);
            layout.SetColumnSpan(_logText, 2);

            StatusStrip status = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("就绪");
            status.Items.Add(_statusLabel);

            Controls.Add(layout);
            Controls.Add(status);
            _accountTimer = new System.Windows.Forms.Timer();
            _accountTimer.Interval = 3000;
            _accountTimer.Tick += AccountTimer_Tick;
            FormClosing += MainForm_FormClosing;
            Shown += delegate
            {
                DetectWeChat();
                SyncActiveStorage(false);
                _accountTimer.Start();
            };
        }

        private static Label CreateLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static FlowLayoutPanel CreateFlowRow()
        {
            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.WrapContents = false;
            panel.FlowDirection = FlowDirection.LeftToRight;
            return panel;
        }

        private void DetectButton_Click(object sender, EventArgs e)
        {
            DetectWeChat();
        }

        private void DetectWeChat()
        {
            _process = ProcessLocator.FindWeChatProcess();
            if (_process == null)
            {
                _processText.Text = "未检测到；请先启动并登录 PC 微信";
                SetStatus("未检测到微信进程");
                return;
            }

            _processText.Text = string.Format("{0}.exe  PID={1}", _process.ProcessName, _process.Id);
            SetStatus("已检测到微信进程");
        }

        private async void StartButton_Click(object sender, EventArgs e)
        {
            string nativePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wx_key.dll");
            if (!File.Exists(nativePath))
            {
                MessageBox.Show(this, "程序目录缺少 wx_key.dll。", "无法开始", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DetectWeChat();
            if (_process == null)
            {
                MessageBox.Show(this, "请先启动并登录 PC 微信。", "未检测到微信", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _keyText.Clear();
            _copyButton.Enabled = false;
            _cancellation = new CancellationTokenSource();
            SetBusy(true);

            try
            {
                if (_restartCheck.Checked)
                {
                    DialogResult answer = MessageBox.Show(
                        this,
                        "工具将关闭并重新启动当前微信。请确认微信中没有尚未完成的操作。是否继续？",
                        "确认重启微信",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes)
                    {
                        return;
                    }

                    string executablePath = ProcessLocator.GetExecutablePath(_process);
                    if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                    {
                        throw new FileNotFoundException("无法确定微信安装路径，不能自动重启");
                    }
                    AppendLog("正在重启微信...");
                    _process = await ProcessLocator.RestartAndWaitAsync(_process, executablePath, _cancellation.Token);
                    _processText.Text = string.Format("{0}.exe  PID={1}", _process.ProcessName, _process.Id);
                    AppendLog("已重新检测到微信进程，请按微信提示完成登录。");
                }

                AppendLog("正在等待微信主窗口、版本信息和核心模块就绪...");
                _process = await ProcessLocator.WaitUntilReadyAsync(_process, TimeSpan.FromSeconds(30), _cancellation.Token);
                _processText.Text = string.Format("{0}.exe  PID={1}", _process.ProcessName, _process.Id);
                string weChatVersion = _process.MainModule.FileVersionInfo.FileVersion;
                AppendLog(string.Format("微信主进程已稳定：PID={0}，Version={1}，准备初始化 Hook。", _process.Id, weChatVersion));

                int processId = _process.Id;
                TimeSpan timeout = TimeSpan.FromSeconds((double)_timeoutInput.Value);
                Progress<string> progress = new Progress<string>(delegate(string message)
                {
                    SetStatus(message);
                    AppendLog(message);
                });

                string key = await Task.Run(delegate
                {
                    using (NativeWxKey native = new NativeWxKey(nativePath))
                    {
                        return native.Capture(processId, timeout, progress, _cancellation.Token);
                    }
                }, _cancellation.Token);

                _keyText.Text = key;
                _copyButton.Enabled = true;
                AppendLog("数据库 Key 获取成功（Key 未写入日志）。");
                SetStatus("获取成功");
            }
            catch (OperationCanceledException)
            {
                AppendLog("操作已停止。");
                SetStatus("已停止");
            }
            catch (Exception exception)
            {
                AppendLog("失败：" + exception.Message);
                SetStatus("获取失败");
                MessageBox.Show(this, exception.Message, "获取失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                if (_cancellation != null)
                {
                    _cancellation.Dispose();
                    _cancellation = null;
                }
            }
        }

        private void StopButton_Click(object sender, EventArgs e)
        {
            if (_cancellation != null)
            {
                _cancellation.Cancel();
            }
        }

        private void CopyButton_Click(object sender, EventArgs e)
        {
            if (_keyText.TextLength > 0)
            {
                Clipboard.SetText(_keyText.Text);
                SetStatus("Key 已复制到剪贴板");
            }
        }

        private void BrowseButton_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "选择账号目录、db_storage 目录或 xwechat_files 根目录";
                dialog.SelectedPath = Directory.Exists(_dataPathText.Text) ? _dataPathText.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    string storage = WeChatDataLocator.NormalizeStorage(dialog.SelectedPath);
                    _dataPathText.Text = string.IsNullOrEmpty(storage) ? dialog.SelectedPath : storage;
                    _currentStorage = string.IsNullOrEmpty(storage) ? string.Empty : storage;
                    _followAccountCheck.Checked = false;
                }
            }
        }

        private void FollowAccountCheck_CheckedChanged(object sender, EventArgs e)
        {
            if (_followAccountCheck.Checked) SyncActiveStorage(true);
        }

        private void AccountTimer_Tick(object sender, EventArgs e)
        {
            if (_followAccountCheck.Checked) SyncActiveStorage(true);
        }

        private void SyncActiveStorage(bool announce)
        {
            string storage = WeChatDataLocator.FindActiveStorage();
            if (string.IsNullOrEmpty(storage)) return;
            if (string.Equals(storage, _currentStorage, StringComparison.OrdinalIgnoreCase)) return;

            bool switched = !string.IsNullOrEmpty(_currentStorage);
            _currentStorage = storage;
            _dataPathText.Text = storage;
            if (switched)
            {
                _keyText.Clear();
                _copyButton.Enabled = false;
                string account = new DirectoryInfo(Path.GetDirectoryName(storage)).Name;
                AppendLog("检测到微信账号切换，数据库已同步到 " + account + "；请重新获取 Key。");
                SetStatus("账号已切换，请重新获取 Key");
            }
            else if (announce)
            {
                AppendLog("已同步当前账号数据库目录。");
            }
        }

        private async void ContactsButton_Click(object sender, EventArgs e)
        {
            string storage;
            string key;
            if (!ValidateExportInput(out storage, out key)) return;
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件 (*.csv)|*.csv";
                dialog.FileName = "微信联系人_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                await RunExportAsync("联系人", delegate(CancellationToken token)
                {
                    return WeChatDataExporter.ExportContacts(storage, key, dialog.FileName, token);
                });
            }
        }

        private bool ValidateExportInput(out string storage, out string key)
        {
            storage = WeChatDataLocator.NormalizeStorage(_dataPathText.Text);
            key = _keyText.Text.Trim();
            try
            {
                byte[] parsed = SqlCipherDatabase.ParseHexKey(key);
                Array.Clear(parsed, 0, parsed.Length);
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Key 无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrEmpty(storage))
            {
                MessageBox.Show(this, "请选择包含 contact/contact.db 的 db_storage 目录。", "数据目录无效", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _dataPathText.Text = storage;
            return true;
        }

        private async Task RunExportAsync(string kind, Func<CancellationToken, int> operation)
        {
            _cancellation = new CancellationTokenSource();
            SetBusy(true);
            try
            {
                AppendLog("开始导出" + kind + "（只读）...");
                int count = await Task.Run(delegate { return operation(_cancellation.Token); }, _cancellation.Token);
                AppendLog(string.Format("{0}导出完成，共 {1} 条。", kind, count));
                SetStatus(kind + "导出完成");
                MessageBox.Show(this, string.Format("导出完成，共 {0} 条。", count), "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (OperationCanceledException)
            {
                AppendLog("导出已停止；目标文件可能不完整。");
                SetStatus("已停止");
            }
            catch (Exception exception)
            {
                AppendLog("导出失败：" + exception.Message);
                SetStatus("导出失败");
                MessageBox.Show(this, exception.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetBusy(false);
                _cancellation.Dispose();
                _cancellation = null;
            }
        }

        private void SetBusy(bool busy)
        {
            _startButton.Enabled = !busy;
            _detectButton.Enabled = !busy;
            _restartCheck.Enabled = !busy;
            _timeoutInput.Enabled = !busy;
            _keyText.Enabled = !busy;
            _dataPathText.Enabled = !busy;
            _browseButton.Enabled = !busy;
            _followAccountCheck.Enabled = !busy;
            _contactsButton.Enabled = !busy;
            _stopButton.Enabled = busy;
        }

        private void AppendLog(string message)
        {
            _logText.AppendText(string.Format("[{0:HH:mm:ss}] {1}{2}", DateTime.Now, message, Environment.NewLine));
        }

        private void SetStatus(string message)
        {
            _statusLabel.Text = message;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _accountTimer.Stop();
            if (_cancellation != null)
            {
                _cancellation.Cancel();
            }
        }
    }
}
