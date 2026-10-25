using System.Linq;
using System.Text.RegularExpressions;
using MaxTweaks.Core.ActivePause.Flags;
using Xunit;

namespace MaxTweaks.Core.Tests.ActivePause.Flags;

public class FlagLabelTests
{
    [Fact]
    public void All_HasSeventeenLabels_InSpecOrder()
    {
        Assert.Equal(17, FlagLabel.All.Count);
        Assert.Equal("good_pattern", FlagLabel.All[0].Id);
        Assert.Equal("check_other_difficulties", FlagLabel.All[16].Id);
        Assert.Equal(FlagGroup.Bad, FlagLabel.Find("too_dense")!.Group);
        Assert.Equal("Doesn't match the music", FlagLabel.Find("mismatches_music")!.Text);
        Assert.Null(FlagLabel.Find("nope"));
        Assert.Null(FlagLabel.Find(null));
    }

    [Fact]
    public void Groups_HaveSpecCounts()
    {
        Assert.Equal(5, FlagLabel.All.Count(l => l.Group == FlagGroup.Good));
        Assert.Equal(8, FlagLabel.All.Count(l => l.Group == FlagGroup.Bad));
        Assert.Equal(4, FlagLabel.All.Count(l => l.Group == FlagGroup.Note));
    }

    [Fact]
    public void Ids_AreUnique_SnakeCase()
    {
        foreach (var label in FlagLabel.All)
        {
            Assert.Matches(new Regex("^[a-z_]+$"), label.Id);
        }

        Assert.Equal(FlagLabel.All.Count, FlagLabel.All.Select(l => l.Id).Distinct().Count());
    }
}
