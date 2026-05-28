using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Services;

namespace Meridian.Auth;

public class OAuthToken
{
    [JsonPropertyName("access_token")]   public string AccessToken  { get; set; } = "";
    [JsonPropertyName("refresh_token")]  public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_at_utc")] public DateTime ExpiresAtUtc { get; set; }

    [JsonIgnore]
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc.AddSeconds(-60);
}

[JsonSerializable(typeof(OAuthToken))]
internal partial class TokenJsonContext : JsonSerializerContext { }

// Provider-agnostic token store. Each provider gets its own subdirectory:
//   %AppData%\Meridian\tokens\<provider>_<safe-email>\token.json
public static class TokenStore
{
    private static string Root => AppPaths.Tokens;

    // Returns all AccountIds found on disk for the given provider name.
    public static IEnumerable<AccountId> GetSavedAccounts(string providerName)
    {
        if (!Directory.Exists(Root)) return [];
        var prefix = providerName + "_";
        return Directory.GetDirectories(Root)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(d => d.StartsWith(prefix, StringComparison.Ordinal))
            .Select(d => TryParseDir(d))
            .OfType<AccountId>();
    }

    public static OAuthToken? Load(AccountId id)
    {
        var path = TokenPath(id);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), TokenJsonContext.Default.OAuthToken);
        }
        catch { return null; }
    }

    public static void Save(AccountId id, OAuthToken token)
    {
        var dir = AccountDir(id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "account.txt"), id.ToString());
        File.WriteAllText(TokenPath(id),
            JsonSerializer.Serialize(token, TokenJsonContext.Default.OAuthToken));
    }

    public static void Delete(AccountId id)
    {
        var dir = AccountDir(id);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    public static string AccountDir(AccountId id) =>
        Path.Combine(Root, id.ToDirectoryName());

    private static string TokenPath(AccountId id) =>
        Path.Combine(AccountDir(id), "token.json");

    // "google_user_gmail.com" -> AccountId("google", "user@gmail.com")
    // Stored as provider_email where @ is replaced with _ (see AccountId.ToDirectoryName)
    private static AccountId? TryParseDir(string dirName)
    {
        var sep = dirName.IndexOf('_');
        if (sep < 1) return null;
        var provider = dirName[..sep];
        // Reverse ToDirectoryName: we can't recover @ unambiguously from _, so store
        // the original AccountId string in a metadata file instead.
        var metaPath = Path.Combine(Root, dirName, "account.txt");
        if (!File.Exists(metaPath)) return null;
        try { return AccountId.Parse(File.ReadAllText(metaPath).Trim()); }
        catch { return null; }
    }

}
