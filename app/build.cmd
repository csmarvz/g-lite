@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%LOCALAPPDATA%\GLite
if not exist build mkdir build
if not exist "%OUT%" mkdir "%OUT%"
set RES=
for %%f in (models\*.json) do call set "RES=%%RES%% /resource:%%f,%%~nxf"
set REFS=%RES% /r:System.Runtime.Serialization.dll /r:System.Management.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll
"%CSC%" /nologo /optimize /target:exe /out:build\selftest.exe %REFS% SelfTest.cs Keyboard.cs Fake.cs Game.cs || exit /b 1
build\selftest.exe || exit /b 1
"%CSC%" /nologo /optimize /target:winexe /out:build\GLite.exe %REFS% Program.cs Keyboard.cs Fake.cs Game.cs || exit /b 1
taskkill /im GLite.exe /f >nul 2>&1
ping -n 2 127.0.0.1 >nul
copy /y build\GLite.exe "%OUT%\" >nul || exit /b 1
for %%f in ("%OUT%\GLite.exe") do echo %%~ff %%~zf bytes
