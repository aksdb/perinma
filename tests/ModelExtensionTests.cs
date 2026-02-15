using perinma.Models;

namespace tests;

public class ModelExtensionTests
{
    [Test]
    public void SetAndGet_TextExtension_ReturnsValue()
    {
        var values = new ModelExtensions();
        var description = new RichText.SimpleText("Meeting description");
        
        values.Set(CalendarEventExtensions.Description, description);
        
        Assert.That(values.Get(CalendarEventExtensions.Description), Is.EqualTo(description));
    }

    [Test]
    public void SetAndGet_MultipleExtensionsSameType_DoNotOverwriteEachOther()
    {
        var values = new ModelExtensions();
        var description = new RichText.SimpleText("Meeting description");
        var location = "Office Room 1";
        
        values.Set(CalendarEventExtensions.Description, description);
        values.Set(CalendarEventExtensions.Location, location);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(values.Get(CalendarEventExtensions.Description), Is.EqualTo(description), "Description should not be overwritten by Location");
            Assert.That(values.Get(CalendarEventExtensions.Location), Is.EqualTo(location), "Location should be correctly retrieved");
        }
    }

    [Test]
    public void SetAndGet_ListExtension_ReturnsValue()
    {
        var values = new ModelExtensions();
        List<CalendarEventParticipant> participants = [
            new CalendarEventParticipant
            {
                Email = "alice@example.com",
                Name = "Alice"
            },
            new CalendarEventParticipant
            {
                Email = "bob@example.com",
                Name = "Bob"
            }
        ];

        values.Set(CalendarEventExtensions.Participants, participants);

        Assert.That(values.Get(CalendarEventExtensions.Participants), Is.EqualTo(participants));
    }

    [Test]
    public void Get_MissingExtension_ReturnsNull()
    {
        var values = new ModelExtensions();
        
        Assert.That(values.Get(CalendarEventExtensions.Description), Is.Null);
    }
    
    [Test]
    public void SetAndGet_Bool()
    {
        var values = new ModelExtensions();
        
        Assert.That(values.Get(CalendarEventExtensions.FullDay), Is.False);
        
        values.Set(CalendarEventExtensions.FullDay, true);
        
        Assert.That(values.Get(CalendarEventExtensions.FullDay), Is.EqualTo(true));
    }
}
