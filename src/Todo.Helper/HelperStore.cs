using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Todo.Helper;

/// <summary>
/// The helper's settings on disk, in %APPDATA%\TodoHelper\helper.json unless told otherwise. The two
/// tokens and the helper's key are encrypted with DPAPI for the current Windows user, so the file is
/// unreadable to another user and on another machine; the URLs are not secret and stay readable.
/// Read once and kept in memory, and every change writes the whole file again.
/// </summary>
public sealed class HelperStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;
    private readonly object _gate = new();
    private HelperSettings _current;

    public HelperStore(string path)
    {
        _path = path;
        _current = Load(path);
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TodoHelper", "helper.json");

    /// <summary>Raised after every change, so the tunnel can say hello again with what changed.</summary>
    public event Action<HelperSettings>? Changed;

    public HelperSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(Func<HelperSettings, HelperSettings> change)
    {
        HelperSettings next;

        lock (_gate)
        {
            next = change(_current);
            Save(_path, next);
            _current = next;
        }

        Changed?.Invoke(next);
    }

    private static HelperSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new HelperSettings();
        }

        var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(path), Json) ?? new StoredSettings();

        return new HelperSettings
        {
            ServerUrl = stored.ServerUrl,
            HelperKey = Unprotect(stored.HelperKey),
            Jira = new SourceSettings { BaseUrl = stored.JiraBaseUrl, Token = Unprotect(stored.JiraToken) },
            Ado = new SourceSettings { BaseUrl = stored.AdoBaseUrl, Token = Unprotect(stored.AdoToken) },
            OllamaUrl = stored.OllamaUrl ?? HelperSettings.DefaultOllamaUrl,
        };
    }

    private static void Save(string path, HelperSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var stored = new StoredSettings
        {
            ServerUrl = settings.ServerUrl,
            HelperKey = Protect(settings.HelperKey),
            JiraBaseUrl = settings.Jira.BaseUrl,
            JiraToken = Protect(settings.Jira.Token),
            AdoBaseUrl = settings.Ado.BaseUrl,
            AdoToken = Protect(settings.Ado.Token),
            OllamaUrl = settings.OllamaUrl,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(stored, Json));
    }

    private static string? Protect(string? secret) => secret is null
        ? null
        : Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret), optionalEntropy: null, DataProtectionScope.CurrentUser));

    private static string? Unprotect(string? stored) => stored is null
        ? null
        : Encoding.UTF8.GetString(ProtectedData.Unprotect(
            Convert.FromBase64String(stored), optionalEntropy: null, DataProtectionScope.CurrentUser));

    /// <summary>The file's shape: the secrets as DPAPI blobs in base64, never as text.</summary>
    private sealed record StoredSettings
    {
        public string? ServerUrl { get; init; }

        public string? HelperKey { get; init; }

        public string? JiraBaseUrl { get; init; }

        public string? JiraToken { get; init; }

        public string? AdoBaseUrl { get; init; }

        public string? AdoToken { get; init; }

        public string? OllamaUrl { get; init; }
    }
}
