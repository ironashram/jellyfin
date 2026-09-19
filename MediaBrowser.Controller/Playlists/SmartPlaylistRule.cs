#nullable enable

namespace MediaBrowser.Controller.Playlists;

/// <summary>
/// One rule from smart-playlists.json, matched to a playlist by name.
/// </summary>
public sealed class SmartPlaylistRule
{
    /// <summary>
    /// Gets or sets the playlist name this rule drives.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets genres the item must carry exactly.
    /// </summary>
    public string[] Genres { get; set; } = [];

    /// <summary>
    /// Gets or sets substrings a genre must contain.
    /// </summary>
    public string[] GenreContains { get; set; } = [];

    /// <summary>
    /// Gets or sets substrings an album artist must contain.
    /// </summary>
    public string[] AlbumArtistContains { get; set; } = [];

    /// <summary>
    /// Gets or sets genres that reject the item outright.
    /// </summary>
    public string[] ExcludeGenres { get; set; } = [];

    /// <summary>
    /// Gets or sets genre substrings that reject the item outright.
    /// </summary>
    public string[] ExcludeGenreContains { get; set; } = [];

    /// <summary>
    /// Gets or sets album-name substrings that reject the item outright.
    /// </summary>
    public string[] ExcludeAlbumContains { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether only favorites are selected.
    /// </summary>
    public bool Favorites { get; set; }

    /// <summary>
    /// Gets or sets how recently the item must have been played, in days. Zero disables the check.
    /// </summary>
    public int PlayedWithinDays { get; set; }

    /// <summary>
    /// Gets or sets the sort field: album, datecreated, dateplayed, or name.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the sort is reversed.
    /// </summary>
    public bool Descending { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of items. Zero means no limit.
    /// </summary>
    public int Limit { get; set; }
}
