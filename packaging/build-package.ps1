param([string]$Compiler = "")
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    if (-not $Compiler) { $Compiler = Join-Path $projectRoot 'tools/InnoSetup/ISCC.exe' }
    if (-not (Test-Path -LiteralPath $Compiler)) { throw 'Set -Compiler to your Inno Setup 6 ISCC.exe path.' }
    & .\build.cmd
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    $package = Join-Path $projectRoot 'dist/TinyTodo-Windows-3.5.7'
    New-Item -ItemType Directory -Path $package,(Join-Path $package 'bin'),(Join-Path $package 'assets') -Force | Out-Null
    Copy-Item -LiteralPath bin/TinyTodo.exe,bin/version.txt -Destination (Join-Path $package 'bin') -Force
    $assetNames = @('app.ico','app-icon.png','floating-icon.png','floating-icon.gif','cat-toggle.png','WenYuanRoundedSC-Regular.ttf','WenYuanRoundedSC-Bold.ttf','Font-coverage.txt','Font-license.txt','Font-source-notice.txt')
    foreach ($name in $assetNames) { Copy-Item -LiteralPath (Join-Path 'assets' $name) -Destination (Join-Path $package 'assets') -Force }
    Copy-Item -LiteralPath packaging/INSTALL.txt -Destination $package -Force
    @('@echo off','start "" "%~dp0bin\TinyTodo.exe"') | Set-Content -LiteralPath (Join-Path $package 'TinyTodo.cmd') -Encoding ASCII
    & $Compiler '/Qp' (Join-Path $PSScriptRoot 'TinyTodo.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $zipPath = Join-Path $projectRoot 'dist/TinyTodo-3.5.7-Portable.zip'
    Compress-Archive -LiteralPath $package -DestinationPath $zipPath -Force
    $outputs = @((Join-Path $projectRoot 'dist/TinyTodo-3.5.7-Setup.exe'),$zipPath)
    $hashLines = foreach ($file in $outputs) { (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($file) }
    $hashLines | Set-Content -LiteralPath dist/SHA256SUMS.txt -Encoding ASCII
    Write-Output 'Ready: dist/TinyTodo-3.5.7-Setup.exe and dist/TinyTodo-3.5.7-Portable.zip'
} finally { Pop-Location }
