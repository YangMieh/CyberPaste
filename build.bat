@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%~dp0build\CyberPaste.exe

echo [build] compiling...
"%CSC%" /nologo /target:winexe /optimize+ /out:"%OUT%" /win32manifest:"%~dp0src\app.manifest" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "%~dp0src\*.cs"

if errorlevel 1 (
  echo [build] FAILED.
  exit /b 1
)
echo [build] done -^> %OUT%
endlocal
