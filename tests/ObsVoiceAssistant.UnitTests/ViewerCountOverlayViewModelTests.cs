using ObsVoiceAssistant.Overlay;

namespace ObsVoiceAssistant.UnitTests;

public sealed class ViewerCountOverlayViewModelTests
{
    [Fact]
    public void Live_ShowsFormattedCount()
    {
        var viewModel = new ViewerCountOverlayViewModel();

        viewModel.Apply(ViewerCountResult.Live(1234));

        Assert.True(viewModel.IsVisible);
        Assert.Equal("1 234", viewModel.CountText);
        Assert.Equal("зрителя", viewModel.LabelText);
        Assert.Equal(1, viewModel.Opacity);
    }

    [Fact]
    public void Offline_HidesWindow()
    {
        var viewModel = new ViewerCountOverlayViewModel();
        viewModel.Apply(ViewerCountResult.Live(10));

        viewModel.Apply(ViewerCountResult.Offline);

        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public void NotConnected_HidesWindow()
    {
        var viewModel = new ViewerCountOverlayViewModel();
        viewModel.Apply(ViewerCountResult.Live(10));

        viewModel.Apply(ViewerCountResult.NotConnected);

        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public void Unavailable_KeepsLastCountButDimsIt()
    {
        var viewModel = new ViewerCountOverlayViewModel();
        viewModel.Apply(ViewerCountResult.Live(10));

        viewModel.Apply(ViewerCountResult.Unavailable);

        Assert.True(viewModel.IsVisible);
        Assert.Equal("10", viewModel.CountText);
        Assert.Equal(0.45, viewModel.Opacity);
    }

    [Fact]
    public void UnavailableWithoutData_DoesNotShowZeroAsFact()
    {
        var viewModel = new ViewerCountOverlayViewModel();

        viewModel.Apply(ViewerCountResult.Unavailable);

        Assert.False(viewModel.IsVisible);
    }

    [Fact]
    public void OfflineThenUnavailable_DoesNotShowStaleCount()
    {
        var viewModel = new ViewerCountOverlayViewModel();
        viewModel.Apply(ViewerCountResult.Live(10));
        viewModel.Apply(ViewerCountResult.Offline);

        viewModel.Apply(ViewerCountResult.Unavailable);

        Assert.False(viewModel.IsVisible);
    }
}
