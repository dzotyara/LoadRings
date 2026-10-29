using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace LoadRings;

public partial class MainWindow : Window
{
    private readonly SystemMonitor _monitor = new();
    private readonly Settings _settings = Settings.Load();
    private readonly Dictionary<MetricKind, RingGauge> _rings = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    public MainWindow()
    {
        InitializeComponent();

        foreach (var kind in Enum.GetValues<MetricKind>())
        {
            var ring = new RingGauge(kind) { Margin = new Thickness(7), IconStyle = _settings.IconStyle };
            _rings[kind] = ring;
            Rings.Children.Add(ring);
        }

        ContextMenu = BuildMenu();
        PlaceWindow();
        SourceInitialized += (_, _) => DesktopPinning.Attach(this);
        MouseLeftButtonDown += OnDrag;

        _timer.Tick += (_, _) => Refresh();
        Refresh();
        _timer.Start();
    }

    private void Refresh()
    {
        foreach (var metric in _monitor.Sample())
        {
            var ring = _rings[metric.Kind];
            ring.Update(metric.Value);
            ring.ToolTip = metric.Detail;
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (_settings.Locked) return;
        DragMove();
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    /// Restores the saved position if it's still on a screen; otherwise goes top-right.
    private void PlaceWindow()
    {
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                               SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (_settings.Left is double left && _settings.Top is double top
            && screens.Contains(new Point(left + Width / 2, top + Height / 2)))
        {
            Left = left;
            Top = top;
            return;
        }
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 24;
        Top = work.Top + 24;
    }

    private ContextMenu BuildMenu()
    {
        var styleMenu = new MenuItem { Header = "Icon Style" };
        foreach (var style in Enum.GetValues<IconStyle>())
        {
            var item = new MenuItem { Header = style.Title(), IsCheckable = true, IsChecked = style == _settings.IconStyle };
            item.Click += (_, _) =>
            {
                _settings.IconStyle = style;
                _settings.Save();
                foreach (var ring in _rings.Values) ring.IconStyle = style;
                foreach (MenuItem other in styleMenu.Items) other.IsChecked = other == item;
            };
            styleMenu.Items.Add(item);
        }

        var lockItem = new MenuItem { Header = "Lock Position", IsCheckable = true, IsChecked = _settings.Locked };
        lockItem.Click += (_, _) =>
        {
            _settings.Locked = lockItem.IsChecked;
            _settings.Save();
        };

        var loginItem = new MenuItem { Header = "Launch at Login", IsCheckable = true, IsChecked = LoginItem.IsEnabled };
        loginItem.Click += (_, _) => loginItem.IsChecked = LoginItem.Set(loginItem.IsChecked);

        var quitItem = new MenuItem { Header = "Quit LoadRings" };
        quitItem.Click += (_, _) => Application.Current.Shutdown();

        var menu = new ContextMenu();
        menu.Items.Add(styleMenu);
        menu.Items.Add(lockItem);
        menu.Items.Add(loginItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(quitItem);
        return menu;
    }
}
