using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using perinma.Views.Settings;
using perinma.Views.Settings.AddAccountWizard;

namespace tests;

[TestFixture]
public class SettingsPagesTests
{
    [AvaloniaTest]
    public void GeneralSettingsView_UsesAtomInputControls()
    {
        var view = new GeneralSettingsView();

        AssertAtomControl(view, "AutoSyncIntervalInput", "NumericUpDown");
    }

    [AvaloniaTest]
    public void CalendarSettingsView_UsesAtomInputControls()
    {
        var view = new CalendarSettingsView();

        AssertAtomControl(view, "MondayCheckBox", "CheckBox");
        AssertAtomControl(view, "WorkingHoursStartPicker", "TimePicker");
        AssertAtomControl(view, "WorkingHoursEndPicker", "TimePicker");
    }

    [AvaloniaTest]
    public void AccountListView_UsesAtomButtons()
    {
        var view = new AccountListView();

        AssertAtomControl(view, "AddAccountButton", "Button");
    }

    [AvaloniaTest]
    public void AccountWizardSteps_UseAtomInputs()
    {
        AssertAtomControl(new AccountDetailsStepView(), "AccountNameInput", "LineEdit");
        AssertAtomControl(new AccountDetailsStepView(), "AccountTypeComboBox", "ComboBox");
        AssertAtomControl(new CalDavConnectionStepView(), "ServerUrlInput", "LineEdit");
        AssertAtomControl(new CalDavConnectionStepView(), "UsernameInput", "LineEdit");
        AssertAtomControl(new CalDavConnectionStepView(), "PasswordInput", "LineEdit");
        AssertAtomControl(new CardDavConnectionStepView(), "ServerUrlInput", "LineEdit");
        AssertAtomControl(new CardDavConnectionStepView(), "UsernameInput", "LineEdit");
        AssertAtomControl(new CardDavConnectionStepView(), "PasswordInput", "LineEdit");
        AssertAtomControl(new CardDavConnectionStepView(), "TestConnectionButton", "Button");
        AssertAtomControl(new CardDavConnectionStepView(), "ConnectionProgressBar", "ProgressBar");
        AssertAtomControl(new GoogleConnectionStepView(), "ConnectButton", "Button");
        AssertAtomControl(new GoogleConnectionStepView(), "ConnectProgressBar", "ProgressBar");
    }

    [AvaloniaTest]
    public void AddAccountWindow_UsesAtomStepsAndButtons()
    {
        var window = new AddAccountWindow();

        Assert.That(window, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());
        Assert.That(window.SizeToContent, Is.EqualTo(SizeToContent.Height));
        AssertAtomControl(window, "AddAccountSteps", "Steps");
        AssertAtomControl(window, "CancelButton", "Button");
        AssertAtomControl(window, "BackButton", "Button");
        AssertAtomControl(window, "NextButton", "Button");
        AssertAtomControl(window, "FinishButton", "Button");
    }

    [AvaloniaTest]
    public void ReauthenticateAccountWindow_UsesAtomButtons()
    {
        var window = new ReauthenticateAccountWindow();

        Assert.That(window, Is.InstanceOf<AtomUI.Desktop.Controls.Window>());
        AssertAtomControl(window, "ReauthenticateButton", "Button");
        AssertAtomControl(window, "CancelButton", "Button");
    }

    private static void AssertAtomControl(Control root, string name, string typeName)
    {
        var control = root.FindControl<Control>(name);
        Assert.That(control, Is.Not.Null, $"Missing control '{name}'.");
        Assert.That(control!.GetType().Name, Is.EqualTo(typeName), $"Control '{name}' should use AtomUI {typeName}.");
        Assert.That(control.GetType().Namespace, Does.StartWith("AtomUI."));
    }
}
