using MediaBrowser.Controller.Playlists;
using Xunit;

namespace Jellyfin.Controller.Tests.Playlists;

public class SmartPlaylistRulesTests
{
    [Fact]
    public void Matches_ExactGenre_Selects()
    {
        var rule = new SmartPlaylistRule { Genres = ["Celtic"] };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Celtic"], [], null));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Celtic Rock"], [], null));
    }

    [Fact]
    public void Matches_ExactGenre_IgnoresCase()
    {
        var rule = new SmartPlaylistRule { Genres = ["heavy metal"] };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Heavy Metal"], [], null));
    }

    [Fact]
    public void Matches_SeveralGenres_AreOred()
    {
        var rule = new SmartPlaylistRule { Genres = ["Dance", "Dance-Pop"] };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Dance"], [], null));
        Assert.True(SmartPlaylistRules.Matches(rule, ["Dance-Pop"], [], null));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Hip Hop"], [], null));
    }

    [Fact]
    public void Matches_IncludesAcrossFields_AreOred()
    {
        var rule = new SmartPlaylistRule
        {
            GenreContains = ["Cinematic"],
            AlbumArtistContains = ["Howard Shore"],
        };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Cinematic Score"], [], null));
        Assert.True(SmartPlaylistRules.Matches(rule, ["Soundtrack"], ["Howard Shore"], null));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Soundtrack"], ["Hans Zimmer"], null));
    }

    [Fact]
    public void Matches_Exclusions_BeatIncludes()
    {
        var rule = new SmartPlaylistRule
        {
            Genres = ["Heavy Metal"],
            ExcludeGenres = ["Nu Metal"],
            ExcludeGenreContains = ["Progressive"],
        };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Heavy Metal"], [], null));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Heavy Metal", "Nu Metal"], [], null));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Heavy Metal", "Progressive Metal"], [], null));
    }

    [Fact]
    public void Matches_ExcludedAlbum_Rejects()
    {
        var rule = new SmartPlaylistRule
        {
            Genres = ["Electronic"],
            ExcludeAlbumContains = ["The Lord of the Rings"],
        };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Electronic"], [], "Selected Ambient Works"));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Electronic"], [], "The Lord of the Rings: The Two Towers"));
    }

    [Fact]
    public void Matches_NoIncludeTerms_AcceptsEverythingNotExcluded()
    {
        var rule = new SmartPlaylistRule { ExcludeGenres = ["Hip Hop"] };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Anything"], [], null));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Hip Hop"], [], null));
    }

    [Fact]
    public void Matches_MultiGenreTag_IsSplit()
    {
        var rule = new SmartPlaylistRule { Genres = ["Heavy Metal"] };

        Assert.True(SmartPlaylistRules.Matches(rule, ["Hard Rock;Heavy Metal;Metal;Rock"], [], null));
        Assert.True(SmartPlaylistRules.Matches(rule, ["Hard Rock; Heavy Metal; Rock"], [], null));
        Assert.False(SmartPlaylistRules.Matches(rule, ["Hard Rock;Metal;Rock"], [], null));
    }

    [Fact]
    public void Matches_MultiGenreTag_ExclusionSeesEveryPart()
    {
        var rule = new SmartPlaylistRule
        {
            Genres = ["Heavy Metal"],
            ExcludeGenres = ["Nu Metal"],
        };

        Assert.False(SmartPlaylistRules.Matches(rule, ["Heavy Metal;Nu Metal;Rock"], [], null));
        Assert.True(SmartPlaylistRules.Matches(rule, ["Heavy Metal;Thrash Metal"], [], null));
    }

    [Fact]
    public void Matches_MissingTags_DoNotThrow()
    {
        var rule = new SmartPlaylistRule { Genres = ["Celtic"], ExcludeAlbumContains = ["x"] };

        Assert.False(SmartPlaylistRules.Matches(rule, null, null, null));
    }

    [Fact]
    public void For_WithoutConfiguration_ReturnsNoRule()
    {
        Assert.Null(SmartPlaylistRules.For("Heavy Metal"));
        Assert.Null(SmartPlaylistRules.For("Some Ordinary Playlist"));
        Assert.Null(SmartPlaylistRules.For(string.Empty));
        Assert.Null(SmartPlaylistRules.For(null));
    }
}
