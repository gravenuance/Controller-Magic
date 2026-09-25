using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class ChipListTests
{
    private static List<Chip> ChipsOf(ChipList list) => list.Controls.OfType<Chip>().ToList();

    [Fact]
    public void AddItem_KeepsExistingChipsAndTheAddBoxInPlace()
    {
        using var list = new ChipList();
        list.SetItems(["vlc", "steam"]);
        var before = ChipsOf(list);
        var addBox = list.Controls.OfType<TextBox>().Single();

        list.AddItem("firefox");

        var after = ChipsOf(list);
        Assert.Same(before[0], after[0]);
        Assert.Same(before[1], after[1]);
        Assert.Equal("firefox", after[2].Text);
        Assert.Same(addBox, list.Controls[^1]);
        Assert.Equal(["vlc", "steam", "firefox"], list.Items);
    }

    [Fact]
    public void AddItem_Duplicate_IsIgnored()
    {
        using var list = new ChipList();
        list.SetItems(["vlc"]);
        int changes = 0;
        list.ItemsChanged += _ => changes++;

        list.AddItem("VLC");

        Assert.Equal(["vlc"], list.Items);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void RemoveRequested_RemovesOnlyThatChipAndReportsTheRest()
    {
        using var list = new ChipList();
        list.SetItems(["vlc", "steam", "chrome"]);
        var chips = ChipsOf(list);
        List<string>? reported = null;
        list.ItemsChanged += items => reported = items;

        chips[1].RequestRemove();

        Assert.Equal(["vlc", "chrome"], reported);
        Assert.Equal([chips[0], chips[2]], ChipsOf(list));
        Assert.True(chips[1].IsDisposed);
    }

    [Theory]
    [InlineData(Keys.Delete)]
    [InlineData(Keys.Back)]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Space)]
    public void Chip_RemoveKeys_AreRecognised(Keys key)
    {
        Assert.True(Chip.IsRemoveKey(key));
    }

    [Fact]
    public void Chip_IsKeyboardFocusableAndNamedForScreenReaders()
    {
        using var chip = new Chip("vlc");

        Assert.True(chip.TabStop);
        Assert.Equal("Remove vlc", chip.AccessibleName);
    }
}
