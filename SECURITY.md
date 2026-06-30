# WxKeyExtractor

一个本地 Windows 工具，用于在用户明确授权的前提下，从当前电脑上运行的
PC 微信进程获取其数据库解密 Key，并以只读方式导出联系人。

## 功能边界

- 只访问本机 `WeChat.exe` / `Weixin.exe` 进程；
- 不联网，不上传、不保存获取到的 Key；
- SQLCipher 数据库始终以只读模式打开，不修改微信原始文件；
- 联系人由用户主动选择目标文件后导出；
- CSV 使用带 BOM 的 UTF-8 编码，兼容 Excel 中文显示；
- 仅限处理本人数据或已经获得数据所有者明确授权的数据。

## 使用

1. 启动并登录 PC 微信；
2. 以普通用户运行 `WxKeyExtractor.exe`；
3. 点击“检测微信”，再点击“开始获取”；
4. 如果超时，勾选“自动重启微信后获取”再试一次；
5. 获取成功后可点击“复制 Key”；
6. 确认“数据目录”指向账号的 `db_storage`（也可选择账号目录自动识别）；
7. 点击“导出联系人 CSV”，选择保存位置。

默认勾选“自动跟随账号”。程序每 3 秒依据账号数据库的 WAL/SHM/DB 活动时间
识别当前登录账号；检测到账号切换后会同步更新 `db_storage`，并清除旧账号 Key，
提示重新获取，避免用旧 Key 打开新账号数据库。手动选择目录时会关闭自动跟随。

已有 64 位十六进制数据库 Key 时，也可以直接粘贴到 Key 输入框后导出。
联系人导出仅包含普通联系人（`local_type=1`、`verify_flag=0`），自动排除
公众号、群聊和非好友缓存，避免把历史会话对象误当作通讯录好友。

工具会等待微信主窗口、版本信息以及 `Weixin.dll` / `WeChatWin.dll` 连续稳定
3 秒后再初始化 Hook，避免误选启动阶段的短生命周期子进程。

如果仍提示初始化失败，请检查运行记录中的 PID 和 Version。微信与本工具的权限
级别必须一致；原生组件返回的 UTF-8 中文错误会被正确显示。构建脚本强制按
UTF-8 编译源码，导出文件也带 UTF-8 BOM，以避免中文乱码。

微信与本工具权限级别必须一致。如果微信以管理员身份运行，本工具也需要以
管理员身份运行。

## 构建

在 Windows PowerShell 中执行：

```powershell
.\build.ps1
```

构建结果位于 `dist`。项目面向 .NET Framework 4.8、x64，构建脚本使用系统
自带的 C# 编译器，不需要安装 Visual Studio。

## 原生组件说明

`native/wx_key.dll` 来自原 WxExport 发布包，SHA-256：

```text
9A2F2C6A09A3219AFA4748A5E7E513D99C607173C3112C5AFCC888996B98248C
```

当前仓库包含独立 GUI 和托管调用层的完整源码，但 `wx_key.dll` 的原始 C++
工程不在发布包中，因此它仍是二进制组件。该组件导出
`InitializeHook`、`PollKeyData`、`GetStatusMessage`、`CleanupHook` 和
`GetLastErrorMsg`。

`native/e_sqlcipher.dll` 用于只读打开微信 4.x SQLCipher 数据库。程序按每个
数据库文件的 16 字节盐，经 PBKDF2-HMAC-SHA512（256000 次）派生文件 Key，
不会把派生 Key 写入日志或磁盘。

发布前请确认你拥有原生组件的再分发权，并根据你的权利情况选择仓库许可证。
