@echo off
rem Builds Breathing Room with the C# compiler that ships with Windows (.NET Framework 4.8).
rem Nothing to install.
rem
rem   build.cmd           run the tests and build bin\BreathingRoom.exe
rem   build.cmd package   the same, then put the release zip in dist\
rem   build.cmd icon      redraw assets\icon.ico from the cat in src\CatView.cs

setlocal EnableDelayedExpansion
cd /d "%~dp0"

set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%FW%\csc.exe" (
    echo Could not find the .NET Framework 4 C# compiler in %WINDIR%\Microsoft.NET.
    exit /b 1
)
set CSC="%FW%\csc.exe"
set WPF=%FW%\WPF
set XAML=%FW%
set SPEECH=%WINDIR%\Microsoft.NET\assembly\GAC_MSIL\System.Speech\v4.0_4.0.0.0__31bf3856ad364e35\System.Speech.dll
rem Some machines (e.g. build servers) keep these only in the reference assemblies folder.
set REFASM=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8
if not exist "%REFASM%" set REFASM=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2
if not exist "%WPF%\PresentationFramework.dll" set WPF=%REFASM%
if not exist "%XAML%\System.Xaml.dll" set XAML=%REFASM%
if not exist "%SPEECH%" set SPEECH=%REFASM%\System.Speech.dll
set REFS=/r:"%WPF%\PresentationFramework.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\WindowsBase.dll" /r:"%XAML%\System.Xaml.dll" /r:"%FW%\System.Web.Extensions.dll" /r:"%SPEECH%" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll
set OPTS=/nologo /optimize+ /warn:4 /warnaserror+ /codepage:65001

if not exist bin mkdir bin

if /i "%~1"=="icon" goto :icon
if not exist assets\icon.ico goto :icon
goto :tests

:icon
echo Drawing the icon...
%CSC% %OPTS% /target:exe /out:bin\MakeIcon.exe %REFS% tools\MakeIcon.cs src\CatView.cs src\Ui.cs || goto :fail
bin\MakeIcon.exe assets\icon.ico || goto :fail

:tests
echo Running tests...
%CSC% %OPTS% /target:exe /out:bin\Tests.exe %REFS% src\Store.cs src\Stats.cs src\Lines.cs src\DataTools.cs tests\Tests.cs || goto :fail
bin\Tests.exe || goto :fail

echo Building the app...
rem Sounds, their list and the icon are embedded, so the exe is the whole app.
set RES=/resource:assets\icon.ico,icon.ico /resource:assets\sounds\sounds.txt,sounds.sounds.txt
for %%f in (assets\sounds\*.wav) do set RES=!RES! /resource:%%f,sounds.%%~nxf
%CSC% %OPTS% /target:winexe /out:bin\BreathingRoom.exe /win32icon:assets\icon.ico %REFS% !RES! src\*.cs || goto :fail
echo Built %CD%\bin\BreathingRoom.exe

if /i "%~1"=="package" (
    powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1 || goto :fail
)
exit /b 0

:fail
echo.
echo Build failed.
exit /b 1
