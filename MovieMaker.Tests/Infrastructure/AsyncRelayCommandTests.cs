using MovieMaker.Infrastructure;

namespace MovieMaker.Tests.Infrastructure;

public class AsyncRelayCommandTests
{
    [Fact]
    public void Constructor_WithNullExecute_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AsyncRelayCommand(null!));
    }

    [Fact]
    public void CanExecute_WithoutPredicate_ReturnsTrue()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask);

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_DuringRun_TogglesCanExecute()
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(async () =>
        {
            started.SetResult(true);
            await gate.Task;
        });

        Assert.True(command.CanExecute(null));

        command.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(command.CanExecute(null));

        gate.SetResult(true);
        await Task.Delay(50);

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_WhenCanExecuteIsFalse_DoesNotInvokeAction()
    {
        var executed = false;
        var command = new AsyncRelayCommand(() =>
        {
            executed = true;
            return Task.CompletedTask;
        }, () => false);

        command.Execute(null);
        await Task.Delay(50);

        Assert.False(executed);
    }

    [Fact]
    public void RaiseCanExecuteChanged_RaisesEvent()
    {
        var command = new AsyncRelayCommand(() => Task.CompletedTask);
        var raised = false;
        command.CanExecuteChanged += (_, _) => raised = true;

        command.RaiseCanExecuteChanged();

        Assert.True(raised);
    }
}
