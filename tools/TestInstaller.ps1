param([string]$Version = '3.5.9')
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Push-Location $project
try {
    $reg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TinyTodo.InstallerValidation_is1'
    if (Test-Path $reg) { throw 'Existing install detected; test stopped.' }
    $folder = Join-Path $project ('artifacts/install-validation-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $validation = Join-Path $folder 'setup'
    New-Item -ItemType Directory -Path $validation -Force | Out-Null
    $script = Get-Content -LiteralPath packaging/TinyTodo.iss -Raw
    $script = $script.Replace('AppId=TinyTodo', 'AppId=TinyTodo.InstallerValidation')
    $script = $script.Replace('SetupIconFile=..\assets\app.ico', ('SetupIconFile=' + (Join-Path $project 'assets/app.ico')))
    $script = $script.Replace('{userstartup}', '{app}\test-shortcuts\Startup').Replace('{autodesktop}', '{app}\test-shortcuts\Desktop').Replace('{group}', '{app}\test-shortcuts\StartMenu')
    $script = '#define PackageRoot "' + (Join-Path $project ("dist/TinyTodo-Windows-$Version")) + '"' + "`n" + '#define OutputRoot "' + $validation + '"' + "`n" + $script
    $scriptPath = Join-Path $validation 'validation.iss'
    [IO.File]::WriteAllText($scriptPath, $script, [Text.UTF8Encoding]::new($true))
    Copy-Item -LiteralPath packaging/ChineseSimplified.isl,packaging/INSTALL.txt -Destination $validation
    & (Join-Path $project 'tools/InnoSetup/ISCC.exe') '/Qp' $scriptPath
    if ($LASTEXITCODE -ne 0) { throw 'Isolated installer compilation failed.' }
    $setup = Join-Path $validation ("TinyTodo-$Version-Setup.exe")
    $install = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/NOICONS','/TASKS=""',('/DIR="'+$folder+'"')) -WindowStyle Hidden -Wait -PassThru
    if ($install.ExitCode -ne 0) { throw 'Install failed.' }
    Add-Type @"
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class InstallerWindows {
 public delegate bool Callback(IntPtr hwnd, IntPtr data);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback,IntPtr data);
 [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,Callback callback,IntPtr data);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
 [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
 public static string Title(IntPtr hwnd) { var b=new StringBuilder(1024);GetWindowText(hwnd,b,b.Capacity);return b.ToString(); }
 public static IntPtr Find(string title) { IntPtr result=IntPtr.Zero;EnumWindows((h,p)=>{if(Title(h)==title)result=h;return true;},IntPtr.Zero);return result; }
 public static IntPtr Child(IntPtr parent,string title) {IntPtr result=IntPtr.Zero;EnumChildWindows(parent,(h,p)=>{if(Title(h).Contains(title))result=h;return true;},IntPtr.Zero);return result;}
}
"@
    try {
        $exe = Join-Path $folder 'bin/TinyTodo.exe'
        if ((Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath bin/TinyTodo.exe).Hash) { throw 'Installed executable mismatch.' }
        $uninstaller = Join-Path $folder 'unins000.exe'
        $interactive = Start-Process -FilePath $uninstaller -WindowStyle Hidden -PassThru
        $watch=[Diagnostics.Stopwatch]::StartNew();$dialog=[IntPtr]::Zero
        while($watch.ElapsedMilliseconds -lt 10000 -and $dialog -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 100; $dialog=[InstallerWindows]::Find('卸载 TinyTodo') }
        if($dialog -eq [IntPtr]::Zero) { throw 'Uninstall options dialog not found.' }
        $checkbox=[InstallerWindows]::Child($dialog,'同时清理任务文件和备份')
        if($checkbox -eq [IntPtr]::Zero) { throw 'Cleanup checkbox missing.' }
        if([InstallerWindows]::SendMessage($checkbox,0xF0,[IntPtr]::Zero,[IntPtr]::Zero).ToInt32() -ne 1) { throw 'Cleanup checkbox is not checked by default.' }
        Write-Output 'PASS: uninstall offers task cleanup, checked by default.'
        [void][InstallerWindows]::PostMessage($dialog,0x10,[IntPtr]::Zero,[IntPtr]::Zero)
        if(-not $interactive.WaitForExit(10000)) { throw 'Uninstall cancellation timed out.' }
        if(-not (Test-Path -LiteralPath $exe)) { throw 'Cancel removed the installed app.' }
        Write-Output 'PASS: cancel keeps installed files.'
        'Preserve unrelated user files' | Set-Content -LiteralPath (Join-Path $folder 'keep-me.txt')
    } finally {
        $silent = Start-Process -FilePath (Join-Path $folder 'unins000.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
        if($silent.ExitCode -ne 0) { throw 'Silent uninstall failed.' }
    }
    if(Test-Path $reg) { throw 'Uninstall registry entry remains.' }
    if(Test-Path -LiteralPath (Join-Path $folder 'bin/TinyTodo.exe')) { throw 'Installed executable remains.' }
    if(-not (Test-Path -LiteralPath (Join-Path $folder 'keep-me.txt'))) { throw 'Unrelated file removed.' }
    Write-Output 'PASS: silent uninstall removes application and registration, retaining data and unrelated files.'
} finally { Pop-Location }
