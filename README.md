# KS-AOW Database Monitor Service

Usługa Windows monitorująca dostęp do bazy danych KS-AOW i wysyłająca informacje o statusie do webhooka.

## Funkcjonalność

- Sprawdza dostęp do bazy danych co 5 minut
- Odczytuje ID z tabeli FIRM  
- Wysyła dane jako JSON do webhooka (PUT) z timestamp i statusem
- Obsługuje bazy Oracle (>=11) i Firebird
- Automatycznie tworzy konfigurację na podstawie apman.ini
- Szyfruje hasła w pliku konfiguracyjnym
- Lokalne logowanie z retencją 2 dni

## Instalacja

### Automatyczna instalacja
```cmd
install.bat
```
Uruchom jako Administrator. Automatycznie skompiluje i zainstaluje usługę.

### Ręczna instalacja
1. Skompiluj: `dotnet publish -c Release --self-contained false`
2. Skopiuj exe: `copy bin\Release\...\KsaowMonitor.exe .`
3. Zainstaluj: `install.bat`

## Tryby konfiguracji

### 1. Tryb interaktywny (domyślny)
```cmd
KsaowMonitor.exe
```
Program automatycznie:
- Znajdzie plik apman.ini w KS\APW\
- Odczyta konfigurację bazy z sekcji [PARAMETRY] → [ALIAS_BAZY]
- Poprosi o wprowadzenie hasła i webhook URL
- Utworzy config.ini z zaszyfrowanymi danymi

### 2. Tryb automatyczny (masowe wdrożenia)
```cmd
KsaowMonitor.exe --auto https://webhook.example.com/endpoint
```
**Wymaga:** DB_USER = "apw_user" w apman.ini

Automatycznie:
- Znajdzie apman.ini i licencja_aow.xml
- Wygeneruje hasło: `apw_user{IDKS}` (gdzie IDKS z pliku licencji)
- Utworzy config.ini bez interakcji użytkownika

### 3. Tryb testowy
```cmd
KsaowMonitor.exe --console
```
Wykonuje jednorazowy test połączenia i webhook.

## Obsługiwane bazy danych

### Oracle
```ini
[KS-APW]
DB_TYPE=ORACLE
DB_SERVER=192.168.1.5:1521/ORCL
DB_USER=APW_USER
```

### Firebird  
```ini
[KS-APW]
DB_TYPE=FB
DB_SERVER=localhost
DB_PATH=G:\KSBAZA\KS-APW\WAPTEKA.FDB
DB_USER=apw_user
```

## Plik konfiguracyjny

**config.ini** (tworzony automatycznie):
```ini
CONNECTION_STRING=fb://user:{{PASSWORD}}@localhost:3050/path/database.fdb
DATABASE_TYPE=FB
WEBHOOK_URL=https://webhook.example.com
ENCRYPTED_PASSWORD=$AQ...
```

## Logowanie

Usługa zapisuje logi w folderze `logs/`:
- `ksaow-monitor-YYYY-MM-DD.log`
- Retencja: 2 dni
- Format: timestamp, poziom, wiadomość

## Zarządzanie usługą

```cmd
# Instalacja
install.bat

# Deinstalacja  
uninstall.bat

# Test
test-console.bat

# Status usługi
sc query "KS-AOW Database Monitor"

# Start/Stop ręczny
sc start "KS-AOW Database Monitor" 
sc stop "KS-AOW Database Monitor"
```

## Format webhook JSON

**Zapytanie:** PUT z JSON w body

**Sukces:**
```json
{
  "timestamp": "2025-08-16T19:07:20.267Z",
  "status": "success", 
  "firm_ids": [883236],
  "database_type": "FB",
  "message": "Database access successful",
  "executionMode": "production"
}
```

**Błąd:**
```json
{
  "timestamp": "2025-08-16T19:07:20.267Z",
  "status": "error",
  "firm_ids": [],
  "database_type": "FB", 
  "message": "Connection failed: ...",
  "executionMode": "production"
}
```

## Masowe wdrożenie

Przykład skryptu dla wielu klientów:
```cmd
@echo off
for /f %%i in (webhook-urls.txt) do (
    echo Konfiguracja dla %%i
    KsaowMonitor.exe --auto %%i
    if %errorlevel% equ 0 (
        install.bat
        echo ✓ Zainstalowano dla %%i
    ) else (
        echo ✗ Błąd dla %%i
    )
)
```

## Rozwiązywanie problemów

**Usługa nie startuje:**
- Sprawdź logi w folderze `logs/`
- Upewnij się że plik apman.ini istnieje
- Sprawdź uprawnienia do bazy danych

**Webhook nie działa:**
- Sprawdź URL webhook w config.ini
- Upewnij się że endpoint akceptuje PUT
- Sprawdź logi połączeń sieciowych

**Błędy bazy danych:**
- Sprawdź connection string w config.ini
- Upewnij się że usługa bazy działa
- Sprawdź uprawnienia użytkownika DB_USER