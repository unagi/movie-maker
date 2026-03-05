using MovieMaker.Infrastructure;

namespace MovieMaker.Tests.Infrastructure;

public class RelayCommandTests
{
    [Fact]
    public void Constructor_WithNullExecute_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
    }

    [Fact]
    public void CanExecute_WithoutPredicate_ReturnsTrue()
    {
        var command = new RelayCommand(_ => { });

        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_WithPredicate_ReturnsPredicateValue()
    {
        var command = new RelayCommand(_ => { }, parameter => parameter is string);

        Assert.True(command.CanExecute("ok"));
        Assert.False(command.CanExecute(1));
    }

    [Fact]
    public void Execute_PassesParameterToAction()
    {
        object? actual = null;
        var command = new RelayCommand(parameter => actual = parameter);

        command.Execute("value");

        Assert.Equal("value", actual);
    }

    [Fact]
    public void RaiseCanExecuteChanged_RaisesEvent()
    {
        var command = new RelayCommand(_ => { });
        var raised = false;
        command.CanExecuteChanged += (_, _) => raised = true;

        command.RaiseCanExecuteChanged();

        Assert.True(raised);
    }
}
