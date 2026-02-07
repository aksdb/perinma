using perinma.Models;

namespace tests;

[TestFixture]
public class ExtensionValuesTests
{
    [Test]
    public void SetAndGet_TextExtension_ReturnsValue()
    {
        var values = new ExtensionValues();
        var description = "Meeting description";
        
        values.Set(Extensions.Description, description);
        
        Assert.That(values.Get(Extensions.Description), Is.EqualTo(description));
    }

    [Test]
    public void SetAndGet_MultipleExtensionsSameType_DoNotOverwriteEachOther()
    {
        var values = new ExtensionValues();
        var description = "Meeting description";
        var location = "Office Room 1";
        
        values.Set(Extensions.Description, description);
        values.Set(Extensions.Location, location);
        
        Assert.That(values.Get(Extensions.Description), Is.EqualTo(description), "Description should not be overwritten by Location");
        Assert.That(values.Get(Extensions.Location), Is.EqualTo(location), "Location should be correctly retrieved");
    }

    [Test]
    public void SetAndGet_ListExtension_ReturnsValue()
    {
        var values = new ExtensionValues();
        List<string> participants = [
            "Alice",
            "Bob"
        ];
        
        values.Set(Extensions.Participants, participants);
        
        Assert.That(values.Get(Extensions.Participants), Is.EqualTo(participants));
    }

    [Test]
    public void Get_MissingExtension_ReturnsNull()
    {
        var values = new ExtensionValues();
        
        Assert.That(values.Get(Extensions.Description), Is.Null);
    }
}
