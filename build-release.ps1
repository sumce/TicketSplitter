# 生成单文件可运行程序 TicketSplitter.exe
#
#   Windows App SDK 不支持 .NET 单文件部署，所以采用"自解压启动器"方案：
#   1) 正常发布主程序目录（自包含 .NET + Windows App SDK）
#   2) 压成 payload.zip 内嵌进启动器
#   3) 启动器自身用 PublishSingleFile 打成单文件
#   最终 dist\TicketSplitter.exe 一个文件即可分发。
#
#   用法:  powershell -ExecutionPolicy Bypass -File build-release.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

$buildDir = Join-Path $root 'build'
$appOut   = Join-Path $buildDir 'app'
$payload  = Join-Path $root 'src\TicketSplitter.Launcher\payload.zip'
$launcher = Join-Path $root 'src\TicketSplitter.Launcher'
$dist     = Join-Path $root 'dist'

Write-Host '=== 1/5 Release 构建 ==='
dotnet build TicketSplitter.slnx -c Release -v q --nologo

Write-Host '=== 2/5 发布主程序 ==='
Remove-Item $buildDir -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\TicketSplitter.App\TicketSplitter.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:DebugType=none -o build\app --nologo -v q

Write-Host '=== 3/5 打包内嵌负载 ==='
Add-Type -AssemblyName System.IO.Compression.FileSystem
Remove-Item $payload -Force -ErrorAction SilentlyContinue
[System.IO.Compression.ZipFile]::CreateFromDirectory($appOut, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host ('  payload.zip  ' + [math]::Round((Get-Item $payload).Length / 1MB, 1) + ' MB')

Write-Host '=== 4/5 发布单文件启动器 ==='
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $launcher 'obj') -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\TicketSplitter.Launcher\TicketSplitter.Launcher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o dist --nologo -v q

Write-Host '=== 5/5 结果 ==='
Get-ChildItem $dist -Recurse -File | ForEach-Object { Write-Host ('  ' + $_.Name + '  ' + [math]::Round($_.Length / 1MB, 1) + ' MB') }
Copy-Item "$root\docs\使用说明.txt" $dist -Force
Write-Host '完成。'
