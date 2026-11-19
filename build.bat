@echo off
rem Rebuild RTTStamp.exe (WPF version) using the .NET Framework C# compiler built into Windows
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set GAC=C:\Windows\Microsoft.NET\assembly
"%CSC%" -nologo -target:winexe -out:RTTStamp.exe ^
  -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Core.dll ^
  -r:PresentationFramework.dll -r:PresentationCore.dll -r:WindowsBase.dll -r:System.Xaml.dll ^
  -lib:"%GAC%\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35" ^
  -lib:"%GAC%\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35" ^
  -lib:"%GAC%\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35" ^
  -lib:"%GAC%\GAC_MSIL\System.Xaml\v4.0_4.0.0.0__b77a5c561934e089" ^
  -win32icon:image/icon/app.ico ^
  -codepage:65001 RTTStamp.cs
if errorlevel 1 (
  echo BUILD FAILED
) else (
  echo BUILD OK: RTTStamp.exe
)
pause
