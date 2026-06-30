param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$nativeDll = Join-Path $root "native\wx_key.dll"
$cipherDll = Join-Path $root "native\e_sqlcipher.dll"
$dist = Join-Path $root "dist"
$exe = Join-Path $dist "WxKeyExtractor.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "未找到 64 位 C# 编译器: $compiler"
}
if (-not (Test-Path -LiteralPath $nativeDll)) {
    throw "缺少原生组件: $nativeDll"
}
if (-not (Test-Path -LiteralPath $cipherDll)) {
    $knownCipherDll = "D:\Personal\Documents\WxExport-V20260525\e_sqlcipher.dll"
    if (-not (Test-Path -LiteralPath $knownCipherDll)) {
        throw "缺少 SQLCipher 组件: $cipherDll"
    }
    Copy-Item -LiteralPath $knownCipherDll -Destination $cipherDll -Force
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:x64",
    "/optimize+",
    "/debug:pdbonly",
    "/codepage:65001",
    "/utf8output",
    "/out:$exe",
    "/reference:$(Join-Path $framework 'System.dll')",
    "/reference:$(Join-Path $framework 'System.Core.dll')",
    "/reference:$(Join-Path $framework 'System.Drawing.dll')",
    "/reference:$(Join-Path $framework 'System.Windows.Forms.dll')",
    (Join-Path $root "src\Program.cs"),
    (Join-Path $root "src\MainForm.cs"),
    (Join-Path $root "src\NativeWxKey.cs"),
    (Join-Path $root "src\ProcessLocator.cs")
    (Join-Path $root "src\SqlCipherDatabase.cs")
    (Join-Path $root "src\WeChatDataExporter.cs")
    (Join-Path $root "src\WeChatDataLocator.cs")
)

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出码: $LASTEXITCODE"
}

Copy-Item -LiteralPath $nativeDll -Destination (Join-Path $dist "wx_key.dll") -Force
Copy-Item -LiteralPath $cipherDll -Destination (Join-Path $dist "e_sqlcipher.dll") -Force
$staleZstdDll = Join-Path $dist "libzstd.dll"
if (Test-Path -LiteralPath $staleZstdDll) {
    Remove-Item -LiteralPath $staleZstdDll -Force
}
Copy-Item -LiteralPath (Join-Path $root "README.md") -Destination (Join-Path $dist "README.md") -Force
Copy-Item -LiteralPath (Join-Path $root "App.config") -Destination (Join-Path $dist "WxKeyExtractor.exe.config") -Force

Write-Host "构建完成: $exe"
Get-FileHash -LiteralPath $exe -Algorithm SHA256
