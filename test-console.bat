@echo off

set EXE_PATH=%~dp0KsaowMonitor.exe

if not exist "%EXE_PATH%" (
    echo Brak pliku KsaowMonitor.exe
        exit /b 1
)

echo Uruchamianie w trybie konsoli...
echo Nacisnij Ctrl+C aby zatrzymac.

"%EXE_PATH%" --console

pause