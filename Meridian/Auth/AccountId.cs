namespace Meridian.Auth;

// Stable identity for an account across providers. Stored as "provider:email" on disk.
// Example: "google:user@gmail.com", "microsoft:user@outlook.com"
public readonly record struct AccountId(string Provider, string Email)
{
    public static AccountId Parse(string value)
    {
        var sep = value.IndexOf(':');
        if (sep < 1) throw new FormatException($"Invalid AccountId: '{value}'");
        return new(value[..sep], value[(sep + 1)..]);
    }

    public override string ToString() => $"{Provider}:{Email}";

    // Safe directory name: replace @ and . that are fine on disk but be explicit
    public string ToDirectoryName() => ToString().Replace(':', '_').Replace('@', '_');
}
