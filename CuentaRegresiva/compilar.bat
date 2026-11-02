@echo off
rem Compila CuentaRegresiva con el compilador de C# que ya viene en Windows (.NET Framework 4.8).
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /win32icon:app.ico /out:CuentaRegresiva.exe CuentaRegresiva.cs ..\comun\Comun.cs
if errorlevel 1 (echo. & echo Hubo errores al compilar.) else (echo Listo: CuentaRegresiva.exe)
pause
