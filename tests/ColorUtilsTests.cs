using NUnit.Framework;
using perinma.Utils;

namespace tests;

[TestFixture]
public class ColorUtilsTests
{
    [Test]
    public void NormalizeHexColor_WithAlpha_StripsAlphaChannel()
    {
        Assert.That(ColorUtils.NormalizeHexColor("#FF5733FF"), Is.EqualTo("#FF5733"));
        Assert.That(ColorUtils.NormalizeHexColor("#ff5733aa"), Is.EqualTo("#FF5733"));
        Assert.That(ColorUtils.NormalizeHexColor("#00000000"), Is.EqualTo("#000000"));
        Assert.That(ColorUtils.NormalizeHexColor("#FFFFFFFF"), Is.EqualTo("#FFFFFF"));
    }

    [Test]
    public void NormalizeHexColor_WithoutAlpha_ReturnsUppercase()
    {
        Assert.That(ColorUtils.NormalizeHexColor("#FF5733"), Is.EqualTo("#FF5733"));
        Assert.That(ColorUtils.NormalizeHexColor("#ff5733"), Is.EqualTo("#FF5733"));
        Assert.That(ColorUtils.NormalizeHexColor("#AbCdEf"), Is.EqualTo("#ABCDEF"));
    }

    [Test]
    public void NormalizeHexColor_ShortFormat_ExpandsToFullFormat()
    {
        Assert.That(ColorUtils.NormalizeHexColor("#F53"), Is.EqualTo("#FF5533"));
        Assert.That(ColorUtils.NormalizeHexColor("#abc"), Is.EqualTo("#AABBCC"));
        Assert.That(ColorUtils.NormalizeHexColor("#000"), Is.EqualTo("#000000"));
        Assert.That(ColorUtils.NormalizeHexColor("#FFF"), Is.EqualTo("#FFFFFF"));
    }

    [Test]
    public void NormalizeHexColor_WithoutHash_AddsHash()
    {
        Assert.That(ColorUtils.NormalizeHexColor("FF5733"), Is.EqualTo("#FF5733"));
        Assert.That(ColorUtils.NormalizeHexColor("ff5733"), Is.EqualTo("#FF5733"));
        Assert.That(ColorUtils.NormalizeHexColor("FF5733FF"), Is.EqualTo("#FF5733"));
    }

    [Test]
    public void NormalizeHexColor_WithWhitespace_TrimsWhitespace()
    {
        Assert.That(ColorUtils.NormalizeHexColor("  #FF5733  "), Is.EqualTo("#FF5733"));
        Assert.That(ColorUtils.NormalizeHexColor("\t#FF5733\n"), Is.EqualTo("#FF5733"));
    }

    [Test]
    public void NormalizeHexColor_InvalidFormats_ReturnsNull()
    {
        Assert.That(ColorUtils.NormalizeHexColor(null), Is.Null);
        Assert.That(ColorUtils.NormalizeHexColor(""), Is.Null);
        Assert.That(ColorUtils.NormalizeHexColor("   "), Is.Null);
        Assert.That(ColorUtils.NormalizeHexColor("red"), Is.Null);
        Assert.That(ColorUtils.NormalizeHexColor("rgb(255,87,51)"), Is.Null);
        Assert.That(ColorUtils.NormalizeHexColor("#GGG"), Is.Null);
        Assert.That(ColorUtils.NormalizeHexColor("#12345"), Is.Null); // 5 chars - invalid
        Assert.That(ColorUtils.NormalizeHexColor("#1234567"), Is.Null); // 7 chars - invalid
    }
}
