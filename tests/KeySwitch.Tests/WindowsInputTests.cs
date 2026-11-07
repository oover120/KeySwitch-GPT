using System.Runtime.InteropServices;
using KeySwitch.Hooks;
using Xunit;
namespace KeySwitch.Tests;

public sealed class WindowsInputTests
{
    [Fact]
    public void NativeInputMatchesWin64Abi()
    { Assert.Equal(8, IntPtr.Size); Assert.Equal(40, Marshal.SizeOf<Native.Input>()); Assert.Equal(24, Marshal.SizeOf<Native.KeyboardEvent>()); Assert.Equal(8, Marshal.OffsetOf<Native.Input>(nameof(Native.Input.Data)).ToInt32()); }
    [Fact]
    public void ReplacementIsOneMarkedBatchWithUnicodeAndNativeDelimiter()
    {
        var input = InputSender.BuildReplacement(3, "тест", "\n", []).ToArray();
        Assert.Equal(16, input.Length);
        Assert.All(input, i => { Assert.Equal(1u, i.Type); Assert.Equal(Native.Marker, i.Data.Key.Extra); });
        Assert.Equal((ushort)8, input[0].Data.Key.Vk);
        Assert.Equal((ushort)'т', input[6].Data.Key.Scan); Assert.Equal(4u, input[6].Data.Key.Flags);
        Assert.Equal((ushort)13, input[^2].Data.Key.Vk); Assert.Equal(2u, input[^1].Data.Key.Flags);
    }
    [Fact]
    public void HeldModifiersAreReleasedAndRestoredAroundReplacement()
    {
        var input = InputSender.BuildReplacement(0, "x", " ", [0xA0, 0xA2]).ToArray();
        Assert.Equal((ushort)0xA0, input[0].Data.Key.Vk); Assert.Equal(2u, input[0].Data.Key.Flags);
        Assert.Equal((ushort)0xA2, input[^1].Data.Key.Vk); Assert.Equal(0u, input[^1].Data.Key.Flags);
    }
    [Theory]
    [InlineData("ghbdtn, vbh!", "привет, мир!")]
    [InlineData("Привет, мир!", "Ghbdtn, vbh!")]
    public void MixedSelectionPreservesSeparators(string original, string expected) => Assert.Equal(expected, ClipboardConverter.ConvertMixed(original));
}
