using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AuroraDesk.App.Helpers;

internal sealed class StorageOpacityController
{
    private enum Phase
    {
        SlowFade,
        HoverDelay,
        Darkening,
        QuickFadeDelay,
        QuickFade,
        Preview
    }

    private static readonly TimeSpan InitialFade = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan HoverDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Transition = TimeSpan.FromSeconds(3);

    private readonly AppController _controller;
    private readonly Border _hub;
    private readonly Border _drawer;
    private readonly SolidColorBrush _brush = new();
    private readonly DispatcherTimer _delay = new() { Interval = HoverDelay };
    private bool _enabled;
    private bool _hovering;
    private bool _hoverCheckQueued;
    private Phase _phase;
    private int _animationVersion;
    private (double Min, double Max) _range;

    public StorageOpacityController(AppController controller, Border hub, Border drawer)
    {
        _controller = controller;
        _hub = hub;
        _drawer = drawer;
        foreach (var surface in new[] { _hub, _drawer })
        {
            surface.MouseEnter += (_, _) => QueueHoverUpdate();
            surface.MouseLeave += (_, _) => QueueHoverUpdate();
            surface.IsVisibleChanged += (_, _) => QueueHoverUpdate();
            surface.IsHitTestVisibleChanged += (_, _) => QueueHoverUpdate();
        }

        _delay.Tick += (_, _) =>
        {
            _delay.Stop();
            if (!_enabled) return;
            UpdateHover();
            if (!_delay.IsEnabled) AdvancePhase();
        };
        _controller.ConfigChanged += () => Refresh(null);
        _controller.StorageAppearanceChanged += Refresh;
        Refresh(null);
    }

    private bool IsPointerOverStorage() =>
        (_hub.IsVisible && _hub.IsHitTestVisible && _hub.IsMouseOver) ||
        (_drawer.IsVisible && _drawer.IsHitTestVisible && _drawer.IsMouseOver);

    private void QueueHoverUpdate()
    {
        if (!_enabled || _hoverCheckQueued) return;
        _hoverCheckQueued = true;
        _ = _hub.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            _hoverCheckQueued = false;
            UpdateHover();
        }));
    }

    private void UpdateHover()
    {
        if (!_enabled) return;
        bool hovering = IsPointerOverStorage();
        if (hovering == _hovering) return;

        _hovering = hovering;
        if (_phase == Phase.Preview) return;

        if (hovering)
            WaitAtCurrentOpacity(Phase.HoverDelay);
        else if (_phase == Phase.Darkening)
            WaitAtCurrentOpacity(Phase.QuickFadeDelay);
        else if (_phase == Phase.HoverDelay)
            StartSlowFade();
    }

    private void WaitAtCurrentOpacity(Phase phase)
    {
        _phase = phase;
        SetOpacity(_brush.Opacity);
        _delay.Stop();
        _delay.Start();
    }

    private void AdvancePhase()
    {
        if (_phase == Phase.HoverDelay || (_phase == Phase.Preview && _hovering))
        {
            _phase = Phase.Darkening;
            AnimateTo(_range.Max, Transition, StartSlowFade);
        }
        else if (_phase == Phase.QuickFadeDelay)
        {
            _phase = Phase.QuickFade;
            AnimateTo(_range.Min, Transition);
        }
        else if (_phase == Phase.Preview)
        {
            StartSlowFade();
        }
    }

    private void StartSlowFade()
    {
        _delay.Stop();
        _phase = Phase.SlowFade;
        AnimateTo(_range.Min, InitialFade);
    }

    private void Refresh(double? previewOpacity)
    {
        var config = _controller.Config;
        if (!config.EnableAdaptiveStorageOpacity)
        {
            _enabled = false;
            _delay.Stop();
            SetOpacity(1);
            _hub.Background = WidgetWindowHelper.CreateGlassTint(config.GlassOpacity, config.AccentColorHex);
            _drawer.Background = WidgetWindowHelper.CreateGlassTint(Math.Min(0.85, config.GlassOpacity + 0.18), config.AccentColorHex);
            return;
        }

        var color = WidgetWindowHelper.CreateGlassTint(1, config.AccentColorHex).Color;
        color.A = byte.MaxValue;
        _brush.Color = color;

        var range = config.GetStorageOpacityRange();
        if (!_enabled)
        {
            _enabled = true;
            _range = range;
            _hovering = IsPointerOverStorage();
            SetOpacity(range.Max);
            _hub.Background = _brush;
            _drawer.Background = _brush;
            if (_hovering) WaitAtCurrentOpacity(Phase.HoverDelay);
            else StartSlowFade();
        }
        else if (previewOpacity is double preview)
        {
            _range = range;
            _hovering = IsPointerOverStorage();
            SetOpacity(Math.Clamp(preview, range.Min, range.Max));
            _phase = Phase.Preview;
            _delay.Stop();
            _delay.Start();
        }
        else if (range != _range)
        {
            _range = range;
            SetOpacity(Math.Clamp(_brush.Opacity, range.Min, range.Max));
            if (_hovering) WaitAtCurrentOpacity(Phase.HoverDelay);
            else StartSlowFade();
        }
    }

    private void SetOpacity(double opacity)
    {
        _animationVersion++;
        _brush.BeginAnimation(SolidColorBrush.OpacityProperty, null);
        _brush.Opacity = opacity;
    }

    private void AnimateTo(double target, TimeSpan duration, Action? completed = null)
    {
        double from = _brush.Opacity;
        SetOpacity(target);
        if (Math.Abs(from - target) < 0.001)
        {
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(from, target, duration) { FillBehavior = FillBehavior.Stop };
        if (duration == InitialFade) Timeline.SetDesiredFrameRate(animation, 4);
        else animation.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        if (completed != null)
        {
            int version = _animationVersion;
            animation.Completed += (_, _) =>
            {
                if (version == _animationVersion) completed();
            };
        }
        _brush.BeginAnimation(SolidColorBrush.OpacityProperty, animation);
    }
}
