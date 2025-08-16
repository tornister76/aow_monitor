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
        
        return databaseType.ToUpper() switch
        {
            "ORACLE" => await GetFirmIdsFromOracle(actualConnectionString),
            "FB" => await GetFirmIdsFromFirebird(actualConnectionString),
            _ => throw new NotSupportedException($"Unsupported database type: {databaseType}")
        };
    }

    private async Task<List<object>> GetFirmIdsFromOracle(string connectionString)
    {
        var ids = new List<object>();
        var parsedConnection = ParseOracleConnectionString(connectionString);

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

    private async Task<List<object>> GetFirmIdsFromFirebird(string connectionString)
    {
        var ids = new List<object>();
        var parsedConnection = ParseFirebirdConnectionString(connectionString);

        using var connection = new FbConnection(parsedConnection);
        await connection.OpenAsync();

        using var command = new FbCommand("SELECT ID FROM FIRM", connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            ids.Add(reader["ID"]);
        }

        return ids;
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
            var password = userInfo[1];
            var server = uri.Host;
            var port = uri.Port != -1 ? uri.Port : 3050;
            var database = uri.AbsolutePath.TrimStart('/');

            return $"User={user};Password={password};Database={database};DataSource={server};Port={port};Dialect=3;Charset=UTF8;";
        }

        return connectionString;
    }
}