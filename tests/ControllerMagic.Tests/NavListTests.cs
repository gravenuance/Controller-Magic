using System.Windows.Forms;
using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class NavListTests
{
    private static NavList CreateList(int selected = 0)
    {
        var list = new NavList { AccessibleName = "Pages" };
        list.SetItems([new("General", ""), new("Mouse", ""), new("Controller", "")]);
        list.SelectedIndex = selected;
        return list;
    }

    [Theory]
    [InlineData(1, Keys.Down, 3, 2)]
    [InlineData(2, Keys.Down, 3, 2)]
    [InlineData(1, Keys.Up, 3, 0)]
    [InlineData(0, Keys.Up, 3, 0)]
    [InlineData(1, Keys.Home, 3, 0)]
    [InlineData(1, Keys.End, 3, 2)]
    [InlineData(1, Keys.Left, 3, 1)]
    [InlineData(0, Keys.Down, 0, -1)]
    public void Navigate_MovesWithinTheListWithoutWrapping(int current, Keys key, int count, int expected) =>
        Assert.Equal(expected, NavList.Navigate(current, key, count));

    [Theory]
    [InlineData(Keys.Up)]
    [InlineData(Keys.Down)]
    [InlineData(Keys.Home)]
    [InlineData(Keys.End)]
    public void NavigationKeys_AreUpDownHomeEnd(Keys key) => Assert.True(NavList.IsNavigationKey(key));

    [Theory]
    [InlineData(Keys.Tab)]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Down | Keys.Shift)]
    public void OtherKeys_AreNotNavigation(Keys key) => Assert.False(NavList.IsNavigationKey(key));

    [Fact]
    public void SelectedIndex_ChangeRaisesTheEventOnce_SameValueDoesNot()
    {
        using var list = CreateList();
        int raised = 0;
        list.SelectedIndexChanged += (_, _) => raised++;

        list.SelectedIndex = 2;
        list.SelectedIndex = 2;

        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SelectedIndex_IsClampedToTheItems()
    {
        using var list = CreateList();

        list.SelectedIndex = 9;

        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void IsOneTabStop()
    {
        using var list = CreateList();

        Assert.True(list.TabStop);
    }

    [Fact]
    public void EachItemExposesItsNameAndAListItemRole()
    {
        using var list = CreateList(selected: 1);
        var root = list.AccessibilityObject;

        Assert.Equal(AccessibleRole.List, root.Role);
        Assert.Equal("Pages", root.Name);
        Assert.Equal(3, root.GetChildCount());
        Assert.Equal("General", root.GetChild(0)!.Name);
        Assert.Equal("Controller", root.GetChild(2)!.Name);
        Assert.Equal(AccessibleRole.ListItem, root.GetChild(1)!.Role);
    }

    [Fact]
    public void OnlyTheSelectedItemReportsSelected()
    {
        using var list = CreateList(selected: 1);
        var root = list.AccessibilityObject;

        Assert.False(root.GetChild(0)!.State.HasFlag(AccessibleStates.Selected));
        Assert.True(root.GetChild(1)!.State.HasFlag(AccessibleStates.Selected));
    }

    [Fact]
    public void ActivatingAnItemAccessibly_SelectsIt()
    {
        using var list = CreateList();

        list.AccessibilityObject.GetChild(2)!.DoDefaultAction();

        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void ContentHeight_FitsEveryItemPlusPaddingOnBothEnds() =>
        Assert.Equal(6 * 36 + 2 * 8, NavList.ContentHeight(6));
}
