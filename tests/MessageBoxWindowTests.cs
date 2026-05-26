using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using perinma.Views.MessageBox;

namespace tests;

[TestFixture]
public class MessageBoxWindowTests
{
    [AvaloniaTest]
    public void MessageBoxWindow_UsesAtomWindowAndControls()
    {
        var window = new MessageBoxWindow();

        Assert.That(window, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());
        AssertAtomControl(window, "TypeIconPresenter", "IconPresenter");
        AssertAtomControl(window, "OkButton", "Button");
        AssertAtomControl(window, "CancelButton", "Button");
        AssertAtomControl(window, "YesButton", "Button");
        AssertAtomControl(window, "NoButton", "Button");
    }

    [AvaloniaTest]
    public void MessageBoxWindow_ConfigureType_UsesAtomIconPresenterPalette()
    {
        var window = new MessageBoxWindow();
        InvokePrivate(window, "ConfigureType", MessageBoxType.Warning);

        var iconHost = window.FindControl<Border>("IconHost");
        var iconPresenter = window.FindControl<Control>("TypeIconPresenter");
        var iconPresenterType = iconPresenter!.GetType();
        var iconBrush = iconPresenterType.GetProperty("IconBrush")!.GetValue(iconPresenter);
        var icon = iconPresenterType.GetProperty("Icon")!.GetValue(iconPresenter) as PathIcon;

        Assert.Multiple(() =>
        {
            Assert.That(iconHost, Is.Not.Null);
            Assert.That(GetSolidColor(iconHost!.Background), Is.EqualTo(Color.FromRgb(255, 185, 0)));
            Assert.That(GetSolidColor(iconBrush), Is.EqualTo(Colors.Black));
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon!.Data, Is.Not.Null);
        });
    }

    [AvaloniaTest]
    public void MessageBoxWindow_ConfigureButtons_UsesPrimaryAffirmativeButtons()
    {
        var window = new MessageBoxWindow();
        InvokePrivate(window, "ConfigureButtons", MessageBoxButtons.YesNo);

        var yesButton = window.FindControl<AtomUI.Desktop.Controls.Button>("YesButton");
        var noButton = window.FindControl<AtomUI.Desktop.Controls.Button>("NoButton");
        var okButton = window.FindControl<AtomUI.Desktop.Controls.Button>("OkButton");
        var cancelButton = window.FindControl<AtomUI.Desktop.Controls.Button>("CancelButton");

        Assert.Multiple(() =>
        {
            Assert.That(yesButton, Is.Not.Null);
            Assert.That(noButton, Is.Not.Null);
            Assert.That(okButton, Is.Not.Null);
            Assert.That(cancelButton, Is.Not.Null);
            Assert.That(yesButton!.IsVisible, Is.True);
            Assert.That(noButton!.IsVisible, Is.True);
            Assert.That(okButton!.IsVisible, Is.False);
            Assert.That(cancelButton!.IsVisible, Is.False);
            Assert.That(yesButton.IsDefault, Is.True);
            Assert.That(noButton.IsCancel, Is.True);
            Assert.That(yesButton.ButtonType, Is.EqualTo(AtomUI.Desktop.Controls.ButtonType.Primary));
            Assert.That(noButton.ButtonType, Is.EqualTo(AtomUI.Desktop.Controls.ButtonType.Default));
        });
    }

    private static void InvokePrivate(MessageBoxWindow window, string methodName, object argument)
    {
        var method = typeof(MessageBoxWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing private method '{methodName}'.");
        method!.Invoke(window, [argument]);
    }

    private static Color? GetSolidColor(object? brush)
    {
        return (brush as ISolidColorBrush)?.Color;
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}
