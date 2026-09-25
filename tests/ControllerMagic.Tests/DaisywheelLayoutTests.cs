using ControllerMagic;
using Xunit;

namespace ControllerMagic.Tests;

public class DaisywheelLayoutTests
{
    // US layout: each printable character's virtual key and whether Shift is needed for it.
    private static readonly Dictionary<char, (ushort Vk, bool Shift)> UsLayout = BuildUsLayout();

    private static Dictionary<char, (ushort Vk, bool Shift)> BuildUsLayout()
    {
        var map = new Dictionary<char, (ushort, bool)>();
        for (char c = 'a'; c <= 'z'; c++)
            map[c] = ((ushort)char.ToUpperInvariant(c), false);
        for (char c = '0'; c <= '9'; c++)
            map[c] = (c, false);

        const string shiftedDigits = ")!@#$%^&*(";
        for (int i = 0; i < shiftedDigits.Length; i++)
            map[shiftedDigits[i]] = ((ushort)('0' + i), true);

        (ushort Vk, char Plain, char Shifted)[] oem =
        [
            (0xBA, ';', ':'), (0xBB, '=', '+'), (0xBC, ',', '<'), (0xBD, '-', '_'),
            (0xBE, '.', '>'), (0xBF, '/', '?'), (0xC0, '`', '~'), (0xDB, '[', '{'),
            (0xDC, '\\', '|'), (0xDD, ']', '}'), (0xDE, '\'', '"'),
        ];
        foreach (var (vk, plain, shifted) in oem)
        {
            map[plain] = (vk, false);
            map[shifted] = (vk, true);
        }

        return map;
    }

    public static TheoryData<int, int, int> Entries()
    {
        var data = new TheoryData<int, int, int>();
        var layout = ControllerPoller.KeyboardLayout;
        for (int layer = 0; layer < layout.GetLength(0); layer++)
        {
            for (int sector = 0; sector < layout.GetLength(1); sector++)
            {
                for (int slot = 0; slot < layout.GetLength(2); slot++)
                {
                    if (layout[layer, sector, slot].Vk != 0)
                        data.Add(layer, sector, slot);
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void Entry_TypesTheCharacterItShows_OnAUsLayout(int layer, int sector, int slot)
    {
        var entry = ControllerPoller.KeyboardLayout[layer, sector, slot];

        Assert.True(UsLayout.TryGetValue(entry.Display, out var expected), $"'{entry.Display}' is not on the US layout");
        Assert.Equal(expected.Vk, entry.Vk);
        Assert.Equal(expected.Shift, entry.HasMod);
    }
}
