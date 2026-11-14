using MaxTweaks.Core.ObsRecording;
using Xunit;

namespace MaxTweaks.Core.Tests.ObsRecording;

public class ObsServerSettingsTests
{
    private const string ObsFile = """
        {
          "alerts_enabled": false,
          "auth_required": true,
          "first_load": false,
          "server_enabled": true,
          "server_password": "from-obs-file",
          "server_port": 4460
        }
        """;

    [Fact]
    public void Parse_ReadsObsFile()
    {
        var settings = ObsServerSettings.Parse(ObsFile)!;
        Assert.True(settings.ServerEnabled);
        Assert.True(settings.AuthRequired);
        Assert.Equal(4460, settings.Port);
        Assert.Equal("from-obs-file", settings.Password);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("nope")]
    [InlineData("[]")]
    public void Parse_NotAnObject_IsNull(string? json)
    {
        Assert.Null(ObsServerSettings.Parse(json));
    }

    [Fact]
    public void Parse_MissingOrOddFields_StayNull()
    {
        var settings = ObsServerSettings.Parse("{\"server_port\":\"4455\",\"auth_required\":1,\"server_password\":5,\"server_enabled\":null}")!;
        Assert.Null(settings.ServerEnabled);
        Assert.Null(settings.AuthRequired);
        Assert.Null(settings.Port);
        Assert.Null(settings.Password);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(99999999999)]
    public void Parse_PortOutOfRange_IsNull(long port)
    {
        Assert.Null(ObsServerSettings.Parse("{\"server_port\":" + port + "}")!.Port);
    }

    [Fact]
    public void Resolve_NothingConfigured_NoObsFile_UsesDefaults()
    {
        var endpoint = ObsEndpoint.Resolve(null, 0, null, null);
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.Equal(4455, endpoint.Port);
        Assert.Equal(string.Empty, endpoint.Password);
        Assert.False(endpoint.ServerKnownOff);
        Assert.Equal("ws://127.0.0.1:4455/", endpoint.Uri.ToString());
        Assert.Equal("127.0.0.1:4455", endpoint.Address);
    }

    [Fact]
    public void Resolve_NothingConfigured_TakesPortAndPasswordFromObs()
    {
        var endpoint = ObsEndpoint.Resolve("  ", 0, string.Empty, ObsServerSettings.Parse(ObsFile));
        Assert.Equal("127.0.0.1", endpoint.Host);
        Assert.Equal(4460, endpoint.Port);
        Assert.Equal("from-obs-file", endpoint.Password);
        Assert.False(endpoint.ServerKnownOff);
    }

    [Fact]
    public void Resolve_ConfiguredValuesWin()
    {
        var endpoint = ObsEndpoint.Resolve(" studio-pc ", 4470, "configured", ObsServerSettings.Parse(ObsFile));
        Assert.Equal("studio-pc", endpoint.Host);
        Assert.Equal(4470, endpoint.Port);
        Assert.Equal("configured", endpoint.Password);
        Assert.Equal("ws://studio-pc:4470/", endpoint.Uri.ToString());
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(70000)]
    public void Resolve_InvalidConfiguredPort_FallsBack(int port)
    {
        Assert.Equal(4460, ObsEndpoint.Resolve(null, port, null, ObsServerSettings.Parse(ObsFile)).Port);
        Assert.Equal(4455, ObsEndpoint.Resolve(null, port, null, null).Port);
    }

    [Fact]
    public void Resolve_ObsSaysNoAuth_IgnoresItsStoredPassword()
    {
        var obs = new ObsServerSettings(true, 4455, false, "stale");
        Assert.Equal(string.Empty, ObsEndpoint.Resolve(null, 0, null, obs).Password);
    }

    [Fact]
    public void Resolve_ObsHasNoPassword_IsEmpty()
    {
        var obs = new ObsServerSettings(true, null, true, null);
        var endpoint = ObsEndpoint.Resolve(null, 0, null, obs);
        Assert.Equal(string.Empty, endpoint.Password);
        Assert.Equal(4455, endpoint.Port);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST", true)]
    [InlineData("::1", true)]
    [InlineData("[::1]", true)]
    [InlineData("studio-pc", false)]
    public void Resolve_ServerKnownOff_OnlyForThisPc(string host, bool expected)
    {
        var obs = new ObsServerSettings(false, 4455, true, "x");
        Assert.Equal(expected, ObsEndpoint.Resolve(host, 0, null, obs).ServerKnownOff);
    }
}
