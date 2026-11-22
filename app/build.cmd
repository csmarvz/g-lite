@echo off
setlocal
cd /d "%~dp0"
set STARTED=%TIME%
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%LOCALAPPDATA%\GLite
if not exist build mkdir build
if not exist "%OUT%" mkdir "%OUT%"
set RES=
for %%f in (models\*.json) do call set "RES=%%RES%% /resource:%%f,%%~nxf"
set REFS=%RES% /r:System.Windows.Forms.dll /r:System.Drawing.dll
echo [1/5] Compiling the self-test (build\selftest.exe)
"%CSC%" /nologo /optimize /target:exe /nowarn:649 /main:SelfTest /out:build\selftest.exe %REFS% SelfTest.cs Traces.cs @sources.rsp || (echo Build failed: the self-test didn't compile & exit /b 1)
echo [2/5] Running the self-test: checks and traces, and writing build\GLite.ico
build\selftest.exe build\GLite.ico || (echo Build failed: the self-test failed & exit /b 1)
rem Compiled under another name first: G-Lite may be running from build\GLite.exe, and it's only stopped once this worked.
echo [3/5] Compiling G-Lite (build\GLite.new.exe)
"%CSC%" /nologo /optimize /target:winexe /nowarn:649 /win32icon:build\GLite.ico /out:build\GLite.new.exe %REFS% @sources.rsp || (echo Build failed: G-Lite didn't compile & exit /b 1)
tasklist /fi "imagename eq GLite.exe" 2>nul | find /i "GLite.exe" >nul
if errorlevel 1 (
    echo [4/5] G-Lite isn't running
) else (
    echo [4/5] Stopping the running G-Lite
    taskkill /im GLite.exe /f >nul 2>&1
    ping -n 2 127.0.0.1 >nul
)
echo [5/5] Installing as build\GLite.exe and in %OUT%
move /y build\GLite.new.exe build\GLite.exe >nul || (echo Build failed: couldn't replace build\GLite.exe & exit /b 1)
copy /y build\GLite.exe "%OUT%\" >nul || (echo Build failed: couldn't copy GLite.exe & exit /b 1)
for %%f in ("%OUT%\GLite.exe") do echo Done: %%~ff, %%~zf bytes (started %STARTED%, finished %TIME%). G-Lite isn't restarted.
