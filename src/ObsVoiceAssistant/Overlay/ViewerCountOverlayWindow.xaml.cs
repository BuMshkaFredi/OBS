using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ObsVoiceAssistant.Overlay;

/// <summary>
/// Окно поверх всех окон. Не забирает фокус (WS_EX_NOACTIVATE), не показывается
/// на панели задач и не попадает в Alt+Tab (WS_EX_TOOLWINDOW). Позиция
/// сохраняется после перетаскивания.
/// </summary>
public partial class ViewerCountOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const double ScreenMargin = 24;

    private readonly double? savedLeft;
    private readonly double? savedTop;
    private readonly Action<double, double>? savePosition;
    private bool positionRestored;

    public ViewerCountOverlayWindow(
        ViewerCountOverlayViewModel viewModel,
        double? savedLeft = null,
        double? savedTop = null,
        Action<double, double>? savePosition = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        this.savedLeft = savedLeft;
        this.savedTop = savedTop;
        this.savePosition = savePosition;

        Loaded += OnLoaded;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate | WsExToolWindow);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (positionRestored)
        {
            return;
        }

        positionRestored = true;
        if (savedLeft is { } left &&
            savedTop is { } top &&
            IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }

        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenMargin;
        Top = area.Top + ScreenMargin;
    }

    private void OnDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Курсор отпустили до начала перетаскивания — это не ошибка.
            return;
        }

        savePosition?.Invoke(Left, Top);
    }

    /// <summary>Проверяет, что окно не осталось на отключённом мониторе.</summary>
    private static bool IsOnScreen(double left, double top)
    {
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

        return left + 40 > screenLeft &&
               left < screenRight - 40 &&
               top + 40 > screenTop &&
               top < screenBottom - 40;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong(IntPtr handle, int index, int value);
}
