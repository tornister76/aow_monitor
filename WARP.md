# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

KS-AOW Database Monitor is a C# Windows service that monitors database connectivity for the KS-AOW system. It periodically checks database access and sends status updates via webhook.

## Architecture

### Core Components

- **Program.cs**: Entry point with multiple execution modes (service, console, auto-config, FCL check)
- **MonitorService.cs**: Main background service that runs the 5-minute monitoring loop
- **ConfigurationManager.cs**: Handles configuration discovery from apman.ini, password encryption/decryption
- **DatabaseManager.cs**: Database connectivity layer supporting Oracle and Firebird with multiple auth methods
- **WebhookSender.cs**: HTTP client wrapper for sending JSON payloads via PUT requests

### Execution Modes

1. **Service Mode** (default): Runs as Windows service with 5-minute intervals
2. **Console Mode** (`--console`): One-time test execution with console output
3. **Auto Mode** (`--auto <webhook_url>`): Automated configuration for mass deployment
4. **FCL Check** (`--checkfcl <webhook_url> <idks>`): Password scheme verification

### Database Support

The system supports two database types with intelligent connection handling:
- **Oracle**: Uses Oracle.ManagedDataAccess.Core with TNS/direct connection strings
- **Firebird**: Uses FirebirdSql.Data.FirebirdClient with multiple auth methods (Srp256, Srp, Legacy_Auth)

Connection strings are abstracted with password placeholders (`{{PASSWORD}}`) for security.

### Configuration Discovery

Configuration is discovered automatically through:
1. Registry lookup: `SOFTWARE\WOW6432Node\KAMSOFT\KS-APW\sciezka`
2. Common paths: `C:\KS\APW\apman.ini`, etc.
3. Drive scanning for `\KS\APW\apman.ini`

Passwords are encrypted using Windows Data Protection API (LocalMachine scope).

## Development Commands

### Build and Publish
```cmd
# Clean build with self-contained executable
dotnet publish -c Release --self-contained true -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output bin/Release

# Quick build using provided script
build.bat
```

### Testing
```cmd
# Console mode test (one-time execution)
KsaowMonitor.exe --console

# Test connection only
test-console.bat
```

### Service Management
```cmd
# Install service (requires Administrator)
install.bat

# Uninstall service
uninstall.bat

# Manual service control
sc start "KS-AOW Database Monitor"
sc stop "KS-AOW Database Monitor"
sc query "KS-AOW Database Monitor"
```

### Configuration
```cmd
# Interactive configuration (prompts for password/webhook)
KsaowMonitor.exe

# Automated configuration for deployment
KsaowMonitor.exe --auto https://webhook.example.com/endpoint

# Check FCL password scheme
KsaowMonitor.exe --checkfcl https://webhook.example.com idks_value
```

## Key Development Patterns

### Database Connection Strategy
The DatabaseManager implements a fallback authentication mechanism for Firebird, trying multiple auth methods in sequence (Srp256 → Srp → Legacy_Auth → default). This ensures compatibility across different Firebird versions and configurations.

### Configuration Security
All passwords are encrypted using `System.Security.Cryptography.ProtectedData` with LocalMachine scope. The configuration file stores connection strings with `{{PASSWORD}}` placeholders that are replaced at runtime with decrypted values.

### Logging Strategy
Uses Serilog with file-based logging:
- Log files: `logs/ksaow-monitor-YYYY-MM-DD.log`
- 2-day retention policy
- Structured logging with parameters

### Webhook Communication
All status updates are sent as JSON via HTTP PUT to the configured webhook endpoint. Payload includes:
- ISO timestamp
- Success/error status  
- Database type
- Execution mode (production/test)
- License IDKS from XML file

## Special Files

- **FirebirdDiag/**: Standalone diagnostic tool for Firebird connection troubleshooting
- **versioned folders** (ksaow-monitor-v1.x.x/): Previous release builds with their own configs
- **Claude permissions**: `.claude/settings.local.json` contains development permissions for external tools

## License Integration

The service reads client ID from `licencja_aow.xml` files found via registry lookup or scanning common paths. This IDKS value is included in all webhook payloads for client identification.

## Troubleshooting

- For connection issues, use the FirebirdDiag tool for detailed database connectivity analysis
- Console mode provides immediate feedback without service installation
- Check logs in `logs/` directory for service execution details
- Verify apman.ini parsing if configuration fails during initialization
