@echo off
setlocal
cd /d "%~dp0"
set "TODO_CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%TODO_CSC%" set "TODO_CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%TODO_CSC%" (
  echo C# compiler not found. See README.md.
  pause
  exit /b 1
)
if not exist bin mkdir bin
"%TODO_CSC%" /nologo /noconfig /codepage:65001 /langversion:5 /target:exe /main:UiTests /out:bin\TinyTodo.UiTests.exe /win32manifest:app.manifest /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll src\AssemblyInfo.cs src\TaskControls.cs src\Presentation.cs src\Theme.cs src\Chrome.cs src\Widgets.cs src\MarkdownEditor.cs src\Model.cs src\Markdown.cs src\App.cs src\TaskViews.cs src\DataLocations.cs src\SettingsViews.cs src\TaskWindows.cs src\ProgramCatalog.cs tests\UiTests.cs
if errorlevel 1 (
  pause
  exit /b 1
)
bin\TinyTodo.UiTests.exe
set "TODO_UI_RESULT=%ERRORLEVEL%"
pause
exit /b %TODO_UI_RESULT%
