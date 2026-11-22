namespace Browser
{
    /// <summary>Criteria for <see cref="RadioBrowser.AdvancedSearch"/>.</summary>
    /// <param name="Order">votes (default), popular, name or bitrate.</param>
    /// <param name="HttpOnly">Only stations with a plain http:// stream (older AVRs can't play HTTPS).</param>
    public record StationSearchFilter(
        string? Name,
        string? CountryCode,
        string? Language,
        string? Tag,
        string? Codec,
        int BitrateMin,
        bool HttpOnly,
        string? Order,
        int Offset,
        int Limit);

    /// <summary>A selectable filter value: Value is sent to the API, Label is shown, Count = number of stations.</summary>
    public record FilterOption(string Value, string Label, int Count);
}
