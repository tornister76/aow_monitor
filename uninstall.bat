@echo off

set SERVICE_NAME=KS-AOW Database Monitor

sc stop "%SERVICE_NAME%" >nul 2>&1
sc delete "%SERVICE_NAME%" >nul 2>&1

if %errorlevel% equ 0 (
    echo Usluga usunieta
) else (
    echo Blad usuwania
)

