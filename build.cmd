@echo off
setlocal
cd /d "%~dp0"
set "TODO_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%TODO_CSC%" set "TODO_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%TODO_CSC%" (
  echo C# compiler not found. See README.md for .NET Framework setup.
  exit /b 1
)
if not exist bin mkdir bin
"%TODO_CSC%" /nologo /noconfig /codepage:65001 /langversion:5 /optimize+ /target:winexe /platform:anycpu /out:bin\TinyTodo.exe /win32manifest:app.manifest /win32icon:assets\app.ico /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll src\TaskControls.cs src\Presentation.cs src\Theme.cs src\Chrome.cs src\Widgets.cs src\MarkdownEditor.cs src\Model.cs src\Markdown.cs src\App.cs src\TaskViews.cs src\DataLocations.cs src\SettingsViews.cs
if errorlevel 1 (
  echo Build failed. If TinyTodo is running, exit it from the tray and try again.
  exit /b 1
)
>bin\version.txt echo 3.5.2
echo Built: bin\TinyTodo.exe
exit /b 0
