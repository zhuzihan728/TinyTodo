@echo off
setlocal
cd /d "%~dp0"
if not exist bin\version.txt goto build
findstr /x /c:"3.5.2" bin\version.txt >nul
if errorlevel 1 goto build
if exist bin\TinyTodo.exe goto ready
:build
call build.cmd
if errorlevel 1 (
  pause
  exit /b 1
)
:ready
set "TODO_REMOVER=%TEMP%\TinyTodo-uninstall-%RANDOM%-%RANDOM%.exe"
if exist "%TODO_REMOVER%" (
  echo Temporary name is already in use. Please try again.
  pause
  exit /b 1
)
copy /y "bin\TinyTodo.exe" "%TODO_REMOVER%" >nul
if errorlevel 1 (
  echo Cannot prepare uninstaller.
  pause
  exit /b 1
)
set "TODO_APP_DIR=%~dp0."
cd /d "%TEMP%"
rem Parse this entire block before the uninstaller removes this batch file.
(
  start "" /wait "%TODO_REMOVER%" --uninstall "%TODO_APP_DIR%"
  if errorlevel 1 (
    del "%TODO_REMOVER%" >nul 2>&1
    echo Uninstall cancelled or incomplete. See the message above.
    pause
    exit /b 1
  )
  del "%TODO_REMOVER%" >nul 2>&1
  exit /b 0
)
