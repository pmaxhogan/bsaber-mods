using System;
using System.Threading;
using System.Threading.Tasks;
using MaxTweaks.Core.ObsClips;
using MaxTweaks.Core.ObsRecording;
using MaxTweaks.Core.Tests.ObsRecording;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsClips;

public class ClipSmallPartsTests
{
    [Fact]
    public void Cut_CopiesFromTheEndOfTheReplay()
    {
        var arguments = ClipCut.Arguments(
            @"C:\Videos\beat saber\Replay 2026-01-02 10-00-00.mp4",
            @"C:\Videos\beat saber\Replay 2026-01-02 10-00-00 - Song - Expert+ - 1m23s notes.mp4",
            43.25,
            11);

        Assert.Equal(
            "-hide_banner -loglevel error -nostdin -y -sseof -43.25"
            + @" -i ""C:\Videos\beat saber\Replay 2026-01-02 10-00-00.mp4"""
            + " -t 11 -map 0:v? -map 0:a? -c copy -avoid_negative_ts make_zero -movflags +faststart"
            + @" ""C:\Videos\beat saber\Replay 2026-01-02 10-00-00 - Song - Expert+ - 1m23s notes.mp4""",
            arguments);
    }

    [Theory]
    [InlineData("C:/v/in.mkv", "C:/v/out.mkv", false)]
    [InlineData("C:/v/in.MP4", "C:/v/out.MP4", true)]
    [InlineData("C:/v/in.mov", "C:/v/out.mov", true)]
    [InlineData("C:/v/in.ts", "C:/v/out.ts", false)]
    public void Cut_FastStartOnlyForMp4Family(string source, string target, bool fastStart)
    {
        Assert.Equal(fastStart, ClipCut.Arguments(source, target, 20, 11).Contains("-movflags +faststart", StringComparison.Ordinal));
    }

    [Fact]
    public void Cut_NumbersAreCultureFree_AndNeverZeroOrNegative()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var arguments = ClipCut.Arguments("C:/v/in.mp4", "C:/v/out.mp4", 12.3456, 10.5);
            Assert.Contains("-sseof -12.346 ", arguments, StringComparison.Ordinal);
            Assert.Contains(" -t 10.5 ", arguments, StringComparison.Ordinal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }

        var clamped = ClipCut.Arguments("C:/v/in.mp4", "C:/v/out.mp4", -3, 0);
        Assert.Contains("-sseof -0.1 ", clamped, StringComparison.Ordinal);
        Assert.Contains(" -t 0.1 ", clamped, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "0m00s")]
    [InlineData(5.9, "0m05s")]
    [InlineData(83.2, "1m23s")]
    [InlineData(720, "12m00s")]
    [InlineData(-4, "0m00s")]
    [InlineData(double.NaN, "0m00s")]
    [InlineData(1e12, "5999m59s")]
    public void SongTime_Text(double seconds, string expected)
    {
        Assert.Equal(expected, SongTimeTrail.Text(seconds));
    }

    [Fact]
    public void Trail_AnswersWhereTheSongWas()
    {
        var trail = new SongTimeTrail(keepSeconds: 100);
        Assert.False(trail.TryAt(5, out _));

        // Clock 10..40 plays song 0..30; a pause; then clock 60.. continues from 30.
        for (var clock = 10.0; clock <= 40.0; clock += 0.1)
        {
            trail.Add(clock, clock - 10.0);
        }

        for (var clock = 60.0; clock <= 80.0; clock += 0.1)
        {
            trail.Add(clock, clock - 30.0);
        }

        Assert.True(trail.TryAt(25.2, out var early));
        Assert.InRange(early, 14.6, 15.2);
        Assert.True(trail.TryAt(50, out var paused));
        Assert.InRange(paused, 29.4, 30.0);
        Assert.True(trail.TryAt(70, out var late));
        Assert.InRange(late, 39.4, 40.0);

        // Before the first point: the oldest one stands in.
        Assert.True(trail.TryAt(1, out var first));
        Assert.Equal(0.0, first, 6);

        // Old points are dropped.
        trail.Add(200, 150);
        Assert.True(trail.TryAt(120, out var afterDrop));
        Assert.Equal(150.0, afterDrop, 6);

        trail.Clear();
        Assert.False(trail.TryAt(200, out _));
    }

    [Fact]
    public void FileName_TakesAnExtraPart()
    {
        static bool Nothing(string path) => false;

        Assert.Equal(
            "C:/v/Replay 1 - Song - Expert+ - 1m23s notes.mp4",
            RecordingFileName.Build("C:/v/Replay 1.mp4", "Song", "Expert+", Nothing, "1m23s notes"));
        Assert.Equal("C:/v/Replay 1 - 0m05s heart rate.mp4", RecordingFileName.Build("C:/v/Replay 1.mp4", null, null, Nothing, "0m05s heart rate"));
        Assert.Equal("C:/v/Replay 1 - Song - a b.mp4", RecordingFileName.Build("C:/v/Replay 1.mp4", "Song", string.Empty, Nothing, "a:b"));
        Assert.Equal("C:/v/Replay 1 - Song - Easy.mp4", RecordingFileName.Build("C:/v/Replay 1.mp4", "Song", "Easy", Nothing, "  "));
        Assert.Equal(
            "C:/v/Replay 1 - Song - Easy - x (2).mp4",
            RecordingFileName.Build("C:/v/Replay 1.mp4", "Song", "Easy", path => path == "C:/v/Replay 1 - Song - Easy - x.mp4", "x"));
    }

    [Fact]
    public void Protocol_RequestCarriesItsData()
    {
        var root = JObject.Parse(ObsProtocol.Request("GetProfileParameter", "3", ObsProtocol.ProfileParameter("AdvOut", "RecRBTime")));

        Assert.Equal(6, (int)root["op"]!);
        Assert.Equal("GetProfileParameter", (string?)root["d"]!["requestType"]);
        Assert.Equal("AdvOut", (string?)root["d"]!["requestData"]!["parameterCategory"]);
        Assert.Equal("RecRBTime", (string?)root["d"]!["requestData"]!["parameterName"]);
        Assert.Null(JObject.Parse(ObsProtocol.Request("SaveReplayBuffer", "4"))["d"]!["requestData"]);
    }

    [Fact]
    public async Task Connection_SendsTheRequestData()
    {
        var socket = new FakeSocket();
        socket.OnSend = sent =>
        {
            var d = JObject.Parse(sent)["d"]!;
            return new[]
            {
                FakeSocket.Response(
                    (string)d["requestId"]!,
                    (string)d["requestType"]!,
                    data: new JObject { ["parameterValue"] = (string?)d["requestData"]?["parameterName"] == "RecRBTime" ? "60" : null }),
            };
        };
        using var connection = new ObsConnection(socket, new ObsHello("31.0.0", "5.5.4", 1, null, null), TimeSpan.FromSeconds(5), isThisPc: true);
        connection.Start();

        var response = await connection.RequestAsync("GetProfileParameter", ObsProtocol.ProfileParameter("AdvOut", "RecRBTime"), CancellationToken.None);

        Assert.Equal("60", response.GetString("parameterValue"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("", "\"\"")]
    public void CommandLine_Quote(string input, string expected)
    {
        Assert.Equal(expected, CommandLine.Quote(input));
    }
}
