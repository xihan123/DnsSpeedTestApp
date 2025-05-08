using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DNSSpeedTester.Models;

namespace DNSSpeedTester.Services;

public class DataPersistenceService
{
    private readonly string _customDnsFilePath;
    private readonly string _customDomainsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly string _logFilePath;

    public DataPersistenceService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DNSSpeedTester");

        try
        {
            if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
        }
        catch (Exception ex)
        {
            LogError($"Failed to create app data directory: {ex.Message}");
        }

        _customDnsFilePath = Path.Combine(appDataPath, "custom_dns.json");
        _customDomainsFilePath = Path.Combine(appDataPath, "custom_domains.json");
        _logFilePath = Path.Combine(appDataPath, "error_logs.txt");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new IpAddressConverter() }
        };
    }

    public void SaveCustomDnsServers(List<DnsServer>? customDnsServers)
    {
        if (customDnsServers == null)
        {
            LogError("Cannot save null DNS server list");
            return;
        }

        try
        {
            LogInfo($"Attempting to save {customDnsServers.Count} DNS servers");

            // Convert DnsServer objects to DTOs to avoid IPAddress serialization issues
            var dtoList = new List<DnsServerDto>();
            foreach (var server in customDnsServers) dtoList.Add(DnsServerDto.FromDnsServer(server));

            SaveToFile(_customDnsFilePath, dtoList);
            LogInfo($"Successfully saved {dtoList.Count} DNS servers");
        }
        catch (Exception ex)
        {
            LogError($"Exception in SaveCustomDnsServers: {ex}");
            // Re-throw to allow caller to handle or display error
            throw;
        }
    }

    public List<DnsServer> LoadCustomDnsServers()
    {
        try
        {
            var dtoList = LoadFromFile<List<DnsServerDto>>(_customDnsFilePath);

            if (dtoList == null || dtoList.Count == 0)
            {
                LogInfo("No custom DNS servers found to load");
                return new List<DnsServer>();
            }

            // Convert DTOs back to DnsServer objects
            var result = new List<DnsServer>();
            foreach (var dto in dtoList) result.Add(dto.ToDnsServer());

            LogInfo($"Loaded {result.Count} custom DNS servers");
            return result;
        }
        catch (Exception ex)
        {
            LogError($"Exception loading custom DNS servers: {ex.Message}");
            return new List<DnsServer>();
        }
    }

    public void SaveCustomTestDomains(List<TestDomain>? customDomains)
    {
        if (customDomains == null)
        {
            LogError("Cannot save null test domain list");
            return;
        }

        try
        {
            LogInfo($"Attempting to save {customDomains.Count} test domains");
            SaveToFile(_customDomainsFilePath, customDomains);
            LogInfo($"Successfully saved {customDomains.Count} test domains");
        }
        catch (Exception ex)
        {
            LogError($"Exception in SaveCustomTestDomains: {ex}");
            throw;
        }
    }

    public List<TestDomain> LoadCustomTestDomains()
    {
        try
        {
            var result = LoadFromFile<List<TestDomain>>(_customDomainsFilePath) ?? new List<TestDomain>();
            LogInfo($"Loaded {result.Count} custom test domains");
            return result;
        }
        catch (Exception ex)
        {
            LogError($"Exception loading custom test domains: {ex.Message}");
            return new List<TestDomain>();
        }
    }

    private void SaveToFile<T>(string filePath, T data)
    {
        try
        {
            // Create a backup of the existing file if it exists
            if (File.Exists(filePath))
            {
                var backupPath = $"{filePath}.bak";
                File.Copy(filePath, backupPath, true);
                LogInfo($"Created backup at {backupPath}");
            }

            // Serialize to a string first
            var json = JsonSerializer.Serialize(data, _jsonOptions);

            // Write to a temporary file first, then move it to the destination
            var tempPath = $"{filePath}.tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(filePath))
                File.Delete(filePath);

            File.Move(tempPath, filePath);
        }
        catch (Exception ex)
        {
            LogError($"Error saving to file '{filePath}': {ex}");
            throw;
        }
    }

    private T? LoadFromFile<T>(string filePath) where T : class
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            LogError($"Error loading from file '{filePath}': {ex.Message}");

            // Try to recover from backup
            try
            {
                var backupPath = $"{filePath}.bak";
                if (File.Exists(backupPath))
                {
                    LogInfo($"Attempting to restore from backup: {backupPath}");
                    var backupJson = File.ReadAllText(backupPath);
                    return JsonSerializer.Deserialize<T>(backupJson, _jsonOptions);
                }
            }
            catch (Exception backupEx)
            {
                LogError($"Failed to restore from backup: {backupEx.Message}");
            }

            throw;
        }
    }

    private void LogInfo(string message)
    {
        WriteLog("INFO", message);
    }

    private void LogError(string message)
    {
        WriteLog("ERROR", message);
    }

    private void WriteLog(string level, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var username = Environment.UserName;
        var logEntry = $"{timestamp} {username}{level}: {message}";

        Debug.WriteLine(logEntry);

        try
        {
            ManageLogFileSize();

            using (var fileStream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(fileStream, Encoding.UTF8))
            {
                writer.WriteLine(logEntry);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write to log file: {ex.Message}");
        }
    }

    private void ManageLogFileSize()
    {
        try
        {
            if (!File.Exists(_logFilePath))
                return;

            var fileInfo = new FileInfo(_logFilePath);
            const long maxSizeBytes = 5 * 1024 * 1024; // 5MB max log size

            if (fileInfo.Length > maxSizeBytes)
            {
                var oldLogPath = $"{_logFilePath}.old";
                if (File.Exists(oldLogPath))
                    File.Delete(oldLogPath);

                File.Move(_logFilePath, oldLogPath);
            }
        }
        catch
        {
            // Ignore errors in log rotation
        }
    }
}