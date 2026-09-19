#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Controller.Playlists;

/// <summary>
/// Rule backed playlists, read from smart-playlists.json in the configuration directory.
///
/// A playlist whose name matches an entry resolves its contents from the rule every time it is
/// read, instead of from the tracks stored on it. Nothing is written: the playlist item itself is
/// created by hand once, and the server never creates, deletes or refreshes anything.
///
/// Every failure path returns no rule, which leaves the playlist on the stored-tracks path it
/// takes today. A missing file, malformed json or an unreadable path must never change how an
/// ordinary playlist behaves.
/// </summary>
public static class SmartPlaylistRules
{
    private const string FileName = "smart-playlists.json";

    private static readonly Lock _gate = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static DateTime _loadedWriteTime = DateTime.MinValue;
    private static SmartPlaylistRule[] _rules = [];

    /// <summary>
    /// Finds the rule for a playlist name, or null when the playlist is an ordinary one.
    /// </summary>
    /// <param name="name">The playlist name.</param>
    /// <returns>The matching rule, or null.</returns>
    public static SmartPlaylistRule? For(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (var rule in Load())
        {
            if (string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a rule into the items it currently selects.
    /// </summary>
    /// <param name="rule">The rule to resolve.</param>
    /// <param name="user">The user the playlist is being read for.</param>
    /// <returns>The selected items, ordered and limited by the rule.</returns>
    public static IReadOnlyList<BaseItem> Resolve(SmartPlaylistRule rule, User? user)
    {
        var query = new InternalItemsQuery(user)
        {
            IncludeItemTypes = [BaseItemKind.Audio],
            Recursive = true,
            IsFolder = false,
        };

        // Narrow in the database when the rule names exact genres, which all but one of ours do.
        // A rule matching on substrings has to see every track, so it falls through to the scan.
        if (rule.Genres.Length > 0)
        {
            query.Genres = rule.Genres;
        }

        if (rule.Favorites)
        {
            query.IsFavorite = true;
        }

        var items = BaseItem.LibraryManager.GetItemList(query);
        var selected = new List<BaseItem>(items.Count);
        foreach (var item in items)
        {
            if (Matches(rule, item.Genres, AlbumArtistsOf(item), AlbumOf(item)))
            {
                selected.Add(item);
            }
        }

        if (rule.PlayedWithinDays > 0 && user is not null)
        {
            selected = WithinPlayedWindow(selected, user, rule.PlayedWithinDays);
        }

        Sort(selected, rule, user);

        if (rule.Limit > 0 && selected.Count > rule.Limit)
        {
            selected.RemoveRange(rule.Limit, selected.Count - rule.Limit);
        }

        return selected;
    }

    /// <summary>
    /// Whether an item's tags satisfy a rule. Kept free of item types so the semantics can be
    /// tested directly.
    ///
    /// Include terms are OR'd across every include field: an item needs to match only one of them.
    /// A rule with no include terms accepts everything, leaving the exclusions to narrow it.
    /// Any exclusion match rejects the item outright.
    /// </summary>
    /// <param name="rule">The rule to apply.</param>
    /// <param name="genres">The item's genres.</param>
    /// <param name="albumArtists">The item's album artists.</param>
    /// <param name="album">The item's album name.</param>
    /// <returns>True when the item belongs in the playlist.</returns>
    public static bool Matches(SmartPlaylistRule rule, IReadOnlyList<string>? genres, IReadOnlyList<string>? albumArtists, string? album)
    {
        genres ??= [];
        albumArtists ??= [];

        if (AnyEquals(genres, rule.ExcludeGenres)
            || AnyContains(genres, rule.ExcludeGenreContains)
            || Contains(album, rule.ExcludeAlbumContains))
        {
            return false;
        }

        var hasIncludes = rule.Genres.Length > 0 || rule.GenreContains.Length > 0 || rule.AlbumArtistContains.Length > 0;
        if (!hasIncludes)
        {
            return true;
        }

        return AnyEquals(genres, rule.Genres)
            || AnyContains(genres, rule.GenreContains)
            || AnyContains(albumArtists, rule.AlbumArtistContains);
    }

    private static List<BaseItem> WithinPlayedWindow(List<BaseItem> items, User user, int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var userData = BaseItem.UserDataManager.GetUserDataBatch(items, user);
        var kept = new List<BaseItem>(items.Count);
        foreach (var item in items)
        {
            if (userData.TryGetValue(item.Id, out var data) && data.LastPlayedDate >= cutoff)
            {
                kept.Add(item);
            }
        }

        return kept;
    }

    private static void Sort(List<BaseItem> items, SmartPlaylistRule rule, User? user)
    {
        Comparison<BaseItem> comparison = rule.SortBy?.ToLowerInvariant() switch
        {
            "album" => (a, b) => string.Compare(AlbumOf(a), AlbumOf(b), StringComparison.OrdinalIgnoreCase),
            "datecreated" => (a, b) => a.DateCreated.CompareTo(b.DateCreated),
            "dateplayed" => (a, b) => LastPlayed(a, user).CompareTo(LastPlayed(b, user)),
            _ => (a, b) => string.Compare(a.SortName, b.SortName, StringComparison.OrdinalIgnoreCase),
        };

        items.Sort(comparison);
        if (rule.Descending)
        {
            items.Reverse();
        }
    }

    private static DateTime LastPlayed(BaseItem item, User? user)
    {
        if (user is null)
        {
            return DateTime.MinValue;
        }

        return BaseItem.UserDataManager.GetUserData(user, item)?.LastPlayedDate ?? DateTime.MinValue;
    }

    private static IReadOnlyList<string> AlbumArtistsOf(BaseItem item)
        => item is Audio audio ? audio.AlbumArtists : [];

    private static string? AlbumOf(BaseItem item)
        => item is Audio audio ? audio.Album : null;

    private static bool AnyEquals(IReadOnlyList<string> values, string[] terms)
    {
        foreach (var term in terms)
        {
            foreach (var value in values)
            {
                if (string.Equals(value, term, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AnyContains(IReadOnlyList<string> values, string[] terms)
    {
        foreach (var value in values)
        {
            if (Contains(value, terms))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(string? value, string[] terms)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var term in terms)
        {
            if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static SmartPlaylistRule[] Load()
    {
        var configuration = BaseItem.ConfigurationManager;
        if (configuration is null)
        {
            return [];
        }

        var path = Path.Combine(configuration.CommonApplicationPaths.ConfigurationDirectoryPath, FileName);

        lock (_gate)
        {
            DateTime writeTime;
            try
            {
                writeTime = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            }
            catch (IOException)
            {
                return _rules;
            }

            if (writeTime == _loadedWriteTime)
            {
                return _rules;
            }

            _loadedWriteTime = writeTime;
            _rules = [];
            if (writeTime == DateTime.MinValue)
            {
                return _rules;
            }

            try
            {
                using var stream = File.OpenRead(path);
                _rules = JsonSerializer.Deserialize<SmartPlaylistRulesFile>(stream, _jsonOptions)?.Playlists ?? [];
                BaseItem.Logger?.LogInformation("Loaded {Count} smart playlist rules from {Path}", _rules.Length, path);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                BaseItem.Logger?.LogError(ex, "Failed to read smart playlist rules from {Path}", path);
                _rules = [];
            }

            return _rules;
        }
    }

    /// <summary>
    /// The on-disk shape of smart-playlists.json.
    /// </summary>
    internal sealed class SmartPlaylistRulesFile
    {
        /// <summary>
        /// Gets or sets the rules, one per playlist.
        /// </summary>
        public SmartPlaylistRule[] Playlists { get; set; } = [];
    }
}
