@echo off
setlocal
cd /d "%~dp0"
if not exist bin\TinyTodo.exe goto build
if not exist bin\version.txt goto build
findstr /x /c:"3.5.2" bin\version.txt >nul
if not errorlevel 1 goto launch
:build
call build.cmd
if errorlevel 1 (
  pause
  exit /b 1
)
:launch
start "" "%~dp0bin\TinyTodo.exe"
exit /b 0
