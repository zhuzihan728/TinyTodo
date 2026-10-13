$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Push-Location $project
try {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    $sources = @(Get-ChildItem src -Filter '*.cs' | ForEach-Object { $_.FullName })
    New-Item -ItemType Directory -Path bin -Force | Out-Null
    & $compiler /nologo /noconfig /codepage:65001 /langversion:5 /target:winexe /main:Preview /out:bin\TinyTodo.Preview.exe /win32manifest:app.manifest /win32icon:assets\app.ico /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll $sources tools\Preview.cs
    if ($LASTEXITCODE -ne 0) { throw 'Preview compilation failed.' }
    Start-Process -FilePath (Join-Path $project 'bin/TinyTodo.Preview.exe') -WorkingDirectory $project
} finally { Pop-Location }
