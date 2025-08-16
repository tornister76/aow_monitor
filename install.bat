@echo off

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Uruchom jako Administrator
        exit /b 1
)

set SERVICE_NAME=KS-AOW Database Monitor
set EXE_PATH=%~dp0KsaowMonitor.exe

if not exist "%EXE_PATH%" (
    echo Brak pliku KsaowMonitor.exe
        exit /b 1
)

sc query "%SERVICE_NAME%" >nul 2>&1
if %errorlevel% equ 0 (
    sc stop "%SERVICE_NAME%" >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc delete "%SERVICE_NAME%" >nul 2>&1
    timeout /t 2 /nobreak >nul
)

echo Instalowanie uslugi...
sc create "%SERVICE_NAME%" binPath="%EXE_PATH%" start=auto

if %errorlevel% equ 0 (
    echo Usluga zainstalowana
    sc start "%SERVICE_NAME%"
    if %errorlevel% equ 0 (
        echo Usluga uruchomiona
    )
) else (
    echo Blad instalacji
)

pause