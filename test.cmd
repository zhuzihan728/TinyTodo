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
"%TODO_CSC%" /nologo /noconfig /codepage:65001 /langversion:5 /target:exe /out:bin\TinyTodo.Tests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll src\Model.cs src\Markdown.cs src\DataLocations.cs tests\CoreTests.cs
if errorlevel 1 (
  pause
  exit /b 1
)
bin\TinyTodo.Tests.exe
set "TODO_TEST_RESULT=%ERRORLEVEL%"
pause
exit /b %TODO_TEST_RESULT%
