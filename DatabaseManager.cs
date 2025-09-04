using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using FirebirdSql.Data.FirebirdClient;
using System.Data;

namespace KsaowMonitor;

public class DatabaseManager
{
    private readonly ILogger _logger;

    public DatabaseManager(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<List<object>> GetFirmIdsAsync(string connectionString, string databaseType, string decryptedPassword)
    {
        var actualConnectionString = connectionString.Replace("{{PASSWORD}}", decryptedPassword);
        
        // Najpierw weryfikujemy połączenie
        await TestConnectionAsync(actualConnectionString, databaseType);
        
        return databaseType.ToUpper() switch
        {
            "ORACLE" => await GetFirmIdsFromOracle(actualConnectionString),
            "FB" => await GetFirmIdsFromFirebird(actualConnectionString),
            _ => throw new NotSupportedException($"Unsupported database type: {databaseType}")
        };
    }

    public async Task TestConnectionAsync(string connectionString, string databaseType)
    {
        try
        {
            _logger.LogDebug("Testing connection with database type: {Type}, connection string: {ConnStr}", 
                databaseType, connectionString.Replace("{{PASSWORD}}", "***HIDDEN***"));
                
            switch (databaseType.ToUpper())
            {
                case "FB":
                    await TestFirebirdConnection(connectionString);
                    break;
                case "ORACLE":
                    await TestOracleConnection(connectionString);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported database type: {databaseType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Connection test failed: {Error}", ex.Message);
            throw;
        }
    }

    private async Task TestFirebirdConnection(string connectionString)
    {
        var parsedConnection = ParseFirebirdConnectionString(connectionString);
        
        var authMethods = new[] { "Srp256", "Srp", "Legacy_Auth", "" };
        
        foreach (var authMethod in authMethods)
        {
            try
            {
                var connStr = parsedConnection;
                
                if (!string.IsNullOrEmpty(authMethod))
                {
                    connStr += $";auth_plugin_name={authMethod}";
                }
                
                using var connection = new FbConnection(connStr);
                await connection.OpenAsync();
                
                // Test simple query
                using var command = new FbCommand("SELECT 1 FROM RDB$DATABASE", connection);
                await command.ExecuteScalarAsync();
                
                _logger.LogInformation("Connection test successful with auth method: {Method}", 
                    string.IsNullOrEmpty(authMethod) ? "default" : authMethod);
                return;
            }
            catch (FbException ex) when (ex.Message.Contains("Not supported plugin") || ex.Message.Contains("authentication"))
            {
                _logger.LogDebug("Auth method {Method} failed during test: {Error}", 
                    string.IsNullOrEmpty(authMethod) ? "default" : authMethod, ex.Message);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Connection test attempt with {Method} failed: {Error}", 
                    string.IsNullOrEmpty(authMethod) ? "default" : authMethod, ex.Message);
                continue;
            }
        }
        
        throw new Exception("All authentication methods failed during connection test");
    }

    private async Task TestOracleConnection(string connectionString)
    {
        var parsedConnection = ParseOracleConnectionString(connectionString);
        
        try
        {
            using var connection = new OracleConnection(parsedConnection);
            await connection.OpenAsync();
            
            // Test simple query
            using var command = new OracleCommand("SELECT 1 FROM DUAL", connection);
            await command.ExecuteScalarAsync();
        }
        catch (OracleException ex) when (ex.Message.Contains("ORA-04088") && ex.Message.Contains("TSPY_CONN_APW_USER"))
        {
            _logger.LogError("BŁĄD ORACLE: Wykryto wyzwalacz TSPY_CONN_APW_USER blokujący aplikację KSAOWMONITOR.EXE");
            _logger.LogError("INSTRUKCJA NAPRAWY:");
            _logger.LogError("1. Uruchom SQL*Plus jako administrator bazy danych");
            _logger.LogError("2. Wykonaj polecenie: UPDATE apw_user.aapp SET trust=1 WHERE UPPER(nazwa) LIKE '%KSAOWMONITOR.EXE%';");
            _logger.LogError("3. Wykonaj COMMIT;");
            _logger.LogError("4. Uruchom ponownie aplikację KsaowMonitor");
            _logger.LogError("SZCZEGÓŁY BŁĘDU: {ErrorMessage}", ex.Message);
            
            throw new Exception($"Oracle trigger TSPY_CONN_APW_USER blokuje aplikację. Wymagana ręczna naprawa przez administratora bazy. Szczegóły: {ex.Message}", ex);
        }
    }

    private async Task<List<object>> GetFirmIdsFromOracle(string connectionString)
    {
        var ids = new List<object>();
        var parsedConnection = ParseOracleConnectionString(connectionString);

        try
        {
            using var connection = new OracleConnection(parsedConnection);
            await connection.OpenAsync();

            using var command = new OracleCommand("SELECT ID FROM FIRM", connection);
            using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                ids.Add(reader["ID"]);
            }

            return ids;
        }
        catch (OracleException ex) when (ex.Message.Contains("ORA-04088") && ex.Message.Contains("TSPY_CONN_APW_USER"))
        {
            _logger.LogError("BŁĄD ORACLE: Wykryto wyzwalacz TSPY_CONN_APW_USER blokujący zapytanie do tabeli FIRM");
            _logger.LogError("INSTRUKCJA NAPRAWY:");
            _logger.LogError("1. Uruchom SQL*Plus jako administrator bazy danych");
            _logger.LogError("2. Wykonaj polecenie: UPDATE apw_user.aapp SET trust=1 WHERE UPPER(nazwa) LIKE '%KSAOWMONITOR.EXE%';");
            _logger.LogError("3. Wykonaj COMMIT;");
            _logger.LogError("4. Uruchom ponownie aplikację KsaowMonitor");
            _logger.LogError("SZCZEGÓŁY BŁĘDU: {ErrorMessage}", ex.Message);
            
            throw new Exception($"Oracle trigger TSPY_CONN_APW_USER blokuje zapytanie do tabeli FIRM. Wymagana ręczna naprawa przez administratora bazy. Szczegóły: {ex.Message}", ex);
        }
    }

    private async Task<List<object>> GetFirmIdsFromFirebird(string connectionString)
    {
        var ids = new List<object>();
        var parsedConnection = ParseFirebirdConnectionString(connectionString);

        var authMethods = new[] { "Srp256", "Srp", "Legacy_Auth", "" };
        
        foreach (var authMethod in authMethods)
        {
            try
            {
                var connStr = parsedConnection;
                
                // Dodaj parametr autoryzacji
                if (!string.IsNullOrEmpty(authMethod))
                {
                    connStr += $";auth_plugin_name={authMethod}";
                }
                
                _logger.LogInformation("Trying to connect with auth method: {Method}", 
                    string.IsNullOrEmpty(authMethod) ? "default" : authMethod);

                using var connection = new FbConnection(connStr);
                await connection.OpenAsync();

                using var command = new FbCommand("SELECT ID FROM FIRM", connection);
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    ids.Add(reader["ID"]);
                }

                _logger.LogInformation("Successfully connected using auth method: {Method}", 
                    string.IsNullOrEmpty(authMethod) ? "default" : authMethod);
                return ids;
            }
            catch (FbException ex) when (ex.Message.Contains("Not supported plugin") || ex.Message.Contains("authentication"))
            {
                _logger.LogWarning("Auth method {Method} failed: {Error}, trying next", 
                    string.IsNullOrEmpty(authMethod) ? "default" : authMethod, ex.Message);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Connection attempt with {Method} failed: {Error}", 
                    string.IsNullOrEmpty(authMethod) ? "default" : authMethod, ex.Message);
                continue;
            }
        }

        throw new NotSupportedException("All authentication methods failed for Firebird connection");
    }

    private string ParseOracleConnectionString(string connectionString)
    {
        if (connectionString.StartsWith("oracle://"))
        {
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo.Split(':');
            var user = userInfo[0];
            var password = userInfo[1];
            var dataSource = $"{uri.Host}:{uri.Port}{uri.AbsolutePath}";

            return $"Data Source={dataSource};User Id={user};Password={password};";
        }

        return connectionString;
    }

    private string ParseFirebirdConnectionString(string connectionString)
    {
        if (connectionString.StartsWith("fb://"))
        {
            var uri = new Uri(connectionString);
            var userInfo = uri.UserInfo.Split(':');
            var user = userInfo[0];
            // Zdekoduj hasło, bo Uri.UserInfo automatycznie zakodowuje znaki specjalne
            var password = Uri.UnescapeDataString(userInfo[1]);
            var server = uri.Host;
            var port = uri.Port != -1 ? uri.Port : 3050;
            
            // Poprawne parsowanie ścieżki z dyskiem
            var pathAndQuery = uri.PathAndQuery.TrimStart('/');
            var databasePath = pathAndQuery.Split('?')[0]; // Pobierz ścieżkę przed parametrami
            
            // Obsługa ścieżki z dyskiem (np. D:/KSBAZA/KS-APW/WAPTEKA.FDB)
            databasePath = databasePath.Replace("/", "\\");
            
            // Format dla Firebird: server:ścieżka_do_pliku
            var fullDatabasePath = $"{server}:{databasePath}";
            
            // Buduj podstawowy string połączenia
            var fbConnectionString = $"User={user};Password={password};Database={fullDatabasePath};Port={port};Dialect=3;Charset=UTF8;";
            
            // Sprawdź, czy w query string są dodatkowe parametry
            if (!string.IsNullOrEmpty(uri.Query) && uri.Query.Length > 1)
            {
                var queryString = uri.Query.Substring(1); // Usuń znak '?'
                var queryParams = queryString.Split('&');
                
                foreach (var param in queryParams)
                {
                    var parts = param.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Equals("auth_plugin_name", StringComparison.OrdinalIgnoreCase))
                    {
                        fbConnectionString += $";auth_plugin_name={parts[1]}";
                        break;
                    }
                }
            }
            
            _logger.LogDebug("Parsed Firebird connection string: {ConnectionString}", 
                fbConnectionString.Replace(password, "***HIDDEN***"));
            
            return fbConnectionString;
        }

        return connectionString;
    }
}