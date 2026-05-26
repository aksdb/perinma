using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using perinma.Models;
using perinma.Views.Calendar.EventView;

namespace tests;

[TestFixture]
public class ParticipationViewModelTests
{
    [Test]
    public async Task AcceptCommand_NotifiesDerivedParticipationFlags()
    {
        var changedProperties = new List<string>();
        var viewModel = new ParticipationViewModel(new Participation
        {
            CurrentState = EventResponseStatus.NeedsAction,
            Actions = new ParticipationActions
            {
                Accept = () => Task.CompletedTask,
                Decline = () => Task.CompletedTask,
                Tentative = () => Task.CompletedTask
            }
        });

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != null)
                changedProperties.Add(args.PropertyName);
        };

        await viewModel.AcceptCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CurrentState, Is.EqualTo(EventResponseStatus.Accepted));
            Assert.That(viewModel.IsAccepted, Is.True);
            Assert.That(viewModel.IsDeclined, Is.False);
            Assert.That(viewModel.IsTentative, Is.False);
            Assert.That(viewModel.IsPending, Is.False);
            Assert.That(changedProperties, Does.Contain(nameof(ParticipationViewModel.CurrentState)));
            Assert.That(changedProperties, Does.Contain(nameof(ParticipationViewModel.IsAccepted)));
            Assert.That(changedProperties, Does.Contain(nameof(ParticipationViewModel.IsDeclined)));
            Assert.That(changedProperties, Does.Contain(nameof(ParticipationViewModel.IsTentative)));
            Assert.That(changedProperties, Does.Contain(nameof(ParticipationViewModel.IsPending)));
        });
    }
}
