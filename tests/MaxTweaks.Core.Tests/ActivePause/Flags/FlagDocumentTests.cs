using System;
using MaxTweaks.Core.ActivePause.Flags;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MaxTweaks.Core.Tests.ActivePause.Flags;

public class FlagDocumentTests
{
    private static FlagRecord Sample(string id = "20261025-193012-7f3a") => new()
    {
        Id = id,
        CreatedAtUtc = new DateTime(2026, 10, 25, 19, 30, 12, 345, DateTimeKind.Utc),
        Characteristic = "Standard",
        Difficulty = "Expert",
        SongTime = 42.317f,
        Beat = 96f,
        Speed = 1f,
        Labels = new[] { "too_dense" },
        Notes = new[] { new FlagNote(FlagNoteKind.Note, 96f, 42.317f, 1, 0, "red", "down") },
        Screenshot = "flags/x.png",
    };

    [Fact]
    public void Serialize_WritesSpecShape()
    {
        var document = FlagDocument.Empty("custom_level_ABC", "Song");
        document.Append(Sample());
        var json = document.Serialize();

        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"levelId\": \"custom_level_ABC\"", json);
        Assert.Contains("\"songName\": \"Song\"", json);
        Assert.Contains("\"kind\": \"note\"", json);
        Assert.Contains("\"cutDirection\": \"down\"", json);
        Assert.Contains("\"songTime\": 42.317", json);
        Assert.DoesNotContain("42.3170", json);
        Assert.Contains("\"beat\": 96.0", json);
        Assert.Contains("\"lineIndex\": 1", json);
        Assert.Contains("\"lineLayer\": 0", json);
        Assert.Contains("\"color\": \"red\"", json);
        Assert.Contains("\"createdAt\": \"2026-10-25T19:30:12Z\"", json);
        Assert.Contains("\"labels\": [", json);
        Assert.Contains("\"too_dense\"", json);
        Assert.Contains("\"screenshot\": \"flags/x.png\"", json);
        Assert.DoesNotContain("\r", json);
        Assert.True(json.IndexOf("\"version\"", StringComparison.Ordinal) < json.IndexOf("\"flags\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_KeepsUnknownKeys_AndAppends()
    {
        const string input = "{\"version\":1,\"levelId\":\"x\",\"songName\":\"s\",\"custom\":{\"a\":1},"
            + "\"flags\":[{\"id\":\"f1\",\"createdAt\":\"2026-01-02T03:04:05Z\",\"labels\":[],\"notes\":[],\"extra\":true},42]}";
        var document = FlagDocument.Parse(input);
        Assert.Equal("x", document.LevelId);
        Assert.Equal("s", document.SongName);
        Assert.Single(document.Flags);
        Assert.Equal("f1", document.Flags[0].Id);

        document.Append(Sample("f2"));
        var json = document.Serialize();
        var root = JObject.Parse(json);
        Assert.Equal(1, (int)root["custom"]!["a"]!);
        var flags = (JArray)root["flags"]!;
        Assert.Equal(3, flags.Count);
        Assert.True((bool)flags[0]["extra"]!);
        Assert.Equal(42, (int)flags[1]);
        Assert.Equal("f2", (string?)flags[2]["id"]);

        // The existing flag's date string is written back exactly as read.
        Assert.Contains("\"createdAt\": \"2026-01-02T03:04:05Z\"", json);

        var again = FlagDocument.Parse(json);
        Assert.Equal(2, again.Flags.Count);
    }

    [Fact]
    public void Parse_ReadsKnownFlagFields()
    {
        var document = FlagDocument.Empty("l", "s");
        document.Append(Sample());
        var parsed = FlagDocument.Parse(document.Serialize()).Flags[0];
        Assert.Equal("Expert", parsed.Difficulty);
        Assert.Equal(42.317f, parsed.SongTime, 3);
        Assert.Equal(new[] { "too_dense" }, parsed.Labels);
        Assert.Equal(new FlagNote(FlagNoteKind.Note, 96f, 42.317f, 1, 0, "red", "down"), Assert.Single(parsed.Notes));
        Assert.Equal(new DateTime(2026, 10, 25, 19, 30, 12, DateTimeKind.Utc), parsed.CreatedAtUtc);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("{\"flags\":{}}")]
    [InlineData("{} {}")]
    [InlineData("")]
    public void Parse_Malformed_Throws(string input)
    {
        Assert.Throws<FlagDocumentException>(() => FlagDocument.Parse(input));
    }

    [Fact]
    public void Parse_MissingFlags_IsEmpty()
    {
        var document = FlagDocument.Parse("{\"levelId\":\"x\"}");
        Assert.Empty(document.Flags);
        Assert.Equal(FlagDocument.CurrentVersion, document.Version);
    }

    [Fact]
    public void EmptyFlag_RoundTrips()
    {
        var document = FlagDocument.Empty("l", "s");
        document.Append(new FlagRecord
        {
            Id = "a",
            CreatedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Difficulty = "Expert",
            SongTime = 0f,
        });

        var json = document.Serialize();
        Assert.Contains("\"screenshot\": null", json);
        var flag = Assert.Single(FlagDocument.Parse(json).Flags);
        Assert.Null(flag.Screenshot);
        Assert.Empty(flag.Labels);
        Assert.Empty(flag.Notes);
        Assert.Equal(0f, flag.SongTime);
    }
}
