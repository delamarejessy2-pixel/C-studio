@echo off
setlocal
set "COMPILER=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%COMPILER%" set "COMPILER=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%COMPILER%" (
    echo Erreur : compilateur C# Windows introuvable. Ce projet a besoin du .NET Framework 4.0+.
    pause
    exit /b 1
)
"%COMPILER%" /nologo /target:winexe /optimize+ /out:"%~dp0CStudio.exe" /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0ProgrammeC.cs"
if errorlevel 1 (
    echo La compilation a echoue.
    pause
    exit /b 1
)
echo CStudio.exe a ete cree dans ce dossier.
exit /b 0
