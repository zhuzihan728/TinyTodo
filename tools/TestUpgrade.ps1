param([string]$OldVersion = '3.5.5', [string]$NewVersion = '3.5.6')
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Push-Location $project
try {
    $work = Join-Path $project ('artifacts/upgrade-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $testId = 'TinyTodo.UpgradeValidation'
    $testReg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $testId + '_is1'
    if (Test-Path $testReg) { throw 'An earlier isolated test is still installed.' }
    $userReg = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TinyTodo_is1'
    $userRecord = if (Test-Path $userReg) { Get-ItemProperty -LiteralPath $userReg | ConvertTo-Json -Compress } else { '' }
    # Read only hashes of registered task files; never launch the real app or change its data.
    $metadata = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'TinyTodo'
    $locations = Join-Path $metadata 'data-locations.json'
    $dataDirectories = @($metadata)
    if (Test-Path -LiteralPath $locations) {
        $record = Get-Content -LiteralPath $locations -Raw | ConvertFrom-Json
        $dataDirectories += @($record.KnownDirectories)
    }
    $before = @{}
    foreach ($directory in ($dataDirectories | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $directory)) { throw 'Registered task directory unavailable; verification stopped.' }
        Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Name -match '^(tasks\.json|data-locations\.json|\.tinytodo-data)' } | ForEach-Object {
            $before[$_.FullName] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    }
    $compiler = Join-Path $project 'tools/InnoSetup/ISCC.exe'
    $setups = @{}
    foreach ($version in @($OldVersion, $NewVersion)) {
        $output = Join-Path $work $version
        New-Item -ItemType Directory -Path $output -Force | Out-Null
        $script = if ($version -eq $OldVersion) { (git show ("v${OldVersion}:packaging/TinyTodo.iss")) -join "`n" } else { Get-Content -LiteralPath packaging/TinyTodo.iss -Raw }
        if (-not $script.Contains('AppId=TinyTodo')) { throw 'Unexpected installer AppId.' }
        $script = $script.Replace('AppId=TinyTodo', ('AppId=' + $testId))
        $script = $script.Replace('SetupIconFile=..\assets\app.ico', ('SetupIconFile=' + (Join-Path $project 'assets/app.ico')))
        # Exercise the real checked defaults while keeping shortcuts inside this isolated install.
        $script = $script.Replace('{userstartup}', '{app}\test-shortcuts\Startup').Replace('{autodesktop}', '{app}\test-shortcuts\Desktop').Replace('{group}', '{app}\test-shortcuts\StartMenu')
        $sourcePackage = Join-Path $project ("dist/TinyTodo-Windows-$version")
        if (-not (Test-Path -LiteralPath (Join-Path $sourcePackage 'bin/TinyTodo.exe'))) { throw "Missing package for $version" }
        $script = '#define PackageRoot "' + $sourcePackage + '"' + "`n" + '#define OutputRoot "' + $output + '"' + "`n" + $script
        $scriptPath = Join-Path $output 'validation.iss'
        [IO.File]::WriteAllText($scriptPath, $script, [Text.UTF8Encoding]::new($true))
        Copy-Item -LiteralPath packaging/ChineseSimplified.isl,packaging/INSTALL.txt -Destination $output
        & $compiler '/Qp' $scriptPath
        if ($LASTEXITCODE -ne 0) { throw "Validation installer compile failed: $version" }
        $setups[$version] = Join-Path $output ("TinyTodo-$version-Setup.exe")
    }
    $installed = Join-Path $work 'application'
    try {
        foreach ($version in @($OldVersion, $NewVersion)) {
            $log = Join-Path $work ("install-$version.log")
            $process = Start-Process -FilePath $setups[$version] -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$installed+'"'),('/LOG="'+$log+'"')) -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -ne 0) { throw "Installation failed: $version" }
            $expected = Join-Path $project ("dist/TinyTodo-Windows-$version/bin/TinyTodo.exe")
            if ((Get-FileHash -LiteralPath (Join-Path $installed 'bin/TinyTodo.exe')).Hash -ne (Get-FileHash -LiteralPath $expected).Hash) { throw "Installed executable mismatch: $version" }
            $logText = Get-Content -LiteralPath $log -Raw
            if ($logText -notmatch 'TinyTodo selected tasks: startup,desktopicon') { throw 'Startup and desktop defaults not selected.' }
            foreach ($shortcut in @('Startup','Desktop')) {
                if (-not (Test-Path -LiteralPath (Join-Path $installed ("test-shortcuts/$shortcut/TinyTodo.lnk")))) { throw "Missing default shortcut: $shortcut" }
            }
            if ((Get-ItemProperty -LiteralPath $testReg).DisplayVersion -ne $version) { throw 'Installed version registry mismatch.' }
            foreach ($file in $before.Keys) {
                if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $before[$file]) { throw 'A registered task file changed during installation.' }
            }
            Write-Output "PASS: installed/upgraded $version; default startup and desktop shortcuts created; all registered task files unchanged."
        }
    } finally {
        $uninstaller = Join-Path $installed 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller) {
            $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART') -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -ne 0) { throw 'Validation uninstall failed.' }
        }
    }
    foreach ($file in $before.Keys) {
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $before[$file]) { throw 'A registered task file changed during cleanup.' }
    }
    $userAfter = if (Test-Path $userReg) { Get-ItemProperty -LiteralPath $userReg | ConvertTo-Json -Compress } else { '' }
    if ($userAfter -ne $userRecord) { throw 'Real installation registration changed.' }
    if (Test-Path $testReg) { throw 'Validation install registration was not removed.' }
    foreach ($shortcut in @('Startup','Desktop')) {
        if (Test-Path -LiteralPath (Join-Path $installed ("test-shortcuts/$shortcut/TinyTodo.lnk"))) { throw 'Validation shortcut was not removed.' }
    }
    Write-Output ('PASS: real installation untouched; ' + $before.Count + ' registered data files have identical SHA256 hashes; validation shortcuts and registration removed.')
} finally { Pop-Location }
