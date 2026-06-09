using System.Text.Json.Serialization;

namespace Meridian.Services;

// ── People API: searchDirectoryPeople response DTOs ─────────────────────────────
//
// Subset of https://people.googleapis.com/v1/people:searchDirectoryPeople we
// actually read. We request readMask=names,emailAddresses,photos, so only those
// sub-objects come back. Everything is nullable — the directory may omit any
// field, and an external/unknown query returns an empty `people` array.

internal class DirectorySearchResponse
{
    [JsonPropertyName("people")] public List<DirectoryPersonDto>? People { get; set; }
}

internal class DirectoryPersonDto
{
    [JsonPropertyName("resourceName")]  public string? ResourceName { get; set; }
    [JsonPropertyName("names")]         public List<DirectoryNameDto>? Names { get; set; }
    [JsonPropertyName("emailAddresses")] public List<DirectoryEmailDto>? EmailAddresses { get; set; }
    [JsonPropertyName("photos")]        public List<DirectoryPhotoDto>? Photos { get; set; }
}

internal class DirectoryNameDto
{
    [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
}

internal class DirectoryEmailDto
{
    [JsonPropertyName("value")] public string? Value { get; set; }
}

internal class DirectoryPhotoDto
{
    [JsonPropertyName("url")] public string? Url { get; set; }
    // Google sets this true for the auto-generated silhouette/monogram avatar.
    // We treat a default photo as "no photo" so the UI keeps its own colored
    // initial bubble instead of a generic grey silhouette.
    [JsonPropertyName("default")] public bool? Default { get; set; }
}

[JsonSerializable(typeof(DirectorySearchResponse))]
internal partial class DirectoryApiJsonContext : JsonSerializerContext { }
