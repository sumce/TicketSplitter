# 生成可直接分发的自包含版本
#   用法:  powershell -ExecutionPolicy Bypass -File build-release.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

Write-Host '=== 1/5 Release 构建 ==='
dotnet build TicketSplitter.slnx -c Release -v q --nologo

Write-Host '=== 2/5 发布 GUI（自包含: .NET + Windows App SDK）==='
Remove-Item "$root\publish" -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src\TicketSplitter.App\TicketSplitter.App.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:DebugType=none -o publish\TicketSplitter --nologo -v q

Write-Host '=== 3/5 发布 CLI（自包含）==='
dotnet publish src\TicketSplitter.Cli\TicketSplitter.Cli.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none -o publish\TicketSplitter-CLI --nologo -v q

Write-Host '=== 4/5 打包 ==='
$dist = Join-Path $root 'dist'
Remove-Item $dist -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Compress-Archive -Path "$root\publish\TicketSplitter\*"     -DestinationPath "$dist\TicketSplitter-win-x64.zip"     -Force
Compress-Archive -Path "$root\publish\TicketSplitter-CLI\*" -DestinationPath "$dist\TicketSplitter-CLI-win-x64.zip" -Force
Copy-Item "$root\docs\使用说明.txt" $dist -Force

Write-Host '=== 5/5 结果 ==='
Get-ChildItem $dist | ForEach-Object { Write-Host ('  ' + $_.Name + '  ' + [math]::Round($_.Length / 1MB, 1) + ' MB') }
Write-Host '完成。'
