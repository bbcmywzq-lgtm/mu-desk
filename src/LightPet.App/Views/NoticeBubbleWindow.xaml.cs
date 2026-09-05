using System.Windows;
using System.Windows.Media.Animation;
using LightPet.App.Services;

namespace LightPet.App.Views;

public enum NoticeBubbleKind
{
    Casual,
    Notification,
}

public partial class NoticeBubbleWindow : Window
{
    private const double Gap = 4;
    private Window? _petWindow;

    public NoticeBubbleWindow()
    {
        InitializeComponent();
        ShowInTaskbar = string.Equals(
            Environment.GetEnvironmentVariable("LIGHTPET_QA_WINDOW"),
            "1",
            StringComparison.Ordinal);
        SourceInitialized += (_, _) => NativeWindowStyles.SetClickThrough(this, enabled: true);
    }

    public string PlacementName { get; private set; } = "above";

    public double BubbleWidth => ActualWidth;

    public double BubbleHeight => ActualHeight;

    public void ShowMessage(Window petWindow, string text, NoticeBubbleKind kind)
    {
        _petWindow = petWindow;
        if (Owner is null)
        {
            Owner = petWindow;
        }

        ConfigureKind(kind, text);
        MessageText.Text = text;
        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        UpdatePlacement(petWindow);
        BeginShowAnimation();
    }

    public void UpdatePlacement(Window petWindow)
    {
        if (!IsVisible)
        {
            return;
        }

        _petWindow = petWindow;
        var workArea = MonitorWorkArea.GetFor(petWindow);
        var width = Width;
        var height = Height;
        var petLeft = petWindow.Left;
        var petTop = petWindow.Top;
        var petRight = petLeft + petWindow.Width;
        var petBottom = petTop + petWindow.Height;
        var petCenterX = petLeft + petWindow.Width / 2;
        var petCenterY = petTop + petWindow.Height / 2;

        if (petTop - workArea.Top >= height + Gap)
        {
            PlacementName = "above";
            Left = Clamp(petCenterX - width / 2, workArea.Left, workArea.Right - width);
            Top = petTop - height - Gap;
        }
        else if (workArea.Right - petRight >= width + Gap)
        {
            PlacementName = "right";
            Left = petRight + Gap;
            Top = Clamp(petCenterY - height / 2, workArea.Top, workArea.Bottom - height);
        }
        else if (petLeft - workArea.Left >= width + Gap)
        {
            PlacementName = "left";
            Left = petLeft - width - Gap;
            Top = Clamp(petCenterY - height / 2, workArea.Top, workArea.Bottom - height);
        }
        else
        {
            PlacementName = "below";
            Left = Clamp(petCenterX - width / 2, workArea.Left, workArea.Right - width);
            Top = Clamp(petBottom + Gap, workArea.Top, workArea.Bottom - height);
        }

        ConfigureTail(petWindow);
    }

    public void BeginHideAnimation()
    {
        var duration = TimeSpan.FromMilliseconds(130);
        var easing = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        BeginAnimation(OpacityProperty, new DoubleAnimation(Opacity, 0, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        });
        BubbleScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(BubbleScale.ScaleX, 0.98, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd,
            });
        BubbleScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(BubbleScale.ScaleY, 0.98, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    public void HideNow()
    {
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    private void BeginShowAnimation()
    {
        BeginAnimation(OpacityProperty, null);
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
        BubbleScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
        BubbleOffset.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
        BubbleOffset.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

        var (startX, startY) = PlacementName switch
        {
            "right" => (-6d, 0d),
            "left" => (6d, 0d),
            "below" => (0d, -6d),
            _ => (0d, 6d),
        };
        Opacity = 0;
        BubbleScale.ScaleX = 0.97;
        BubbleScale.ScaleY = 0.97;
        BubbleOffset.X = startX;
        BubbleOffset.Y = startY;

        var duration = TimeSpan.FromMilliseconds(190);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd,
        });
        BubbleScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd,
            });
        BubbleScale.BeginAnimation(
            System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd,
            });
        BubbleOffset.BeginAnimation(
            System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(startX, 0, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd,
            });
        BubbleOffset.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(startY, 0, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    private void ConfigureKind(NoticeBubbleKind kind, string text)
    {
        var notification = kind == NoticeBubbleKind.Notification;
        var expandedCasual = !notification && text.Length > 15;
        Width = notification ? 232 : expandedCasual ? 218 : 198;
        Height = notification ? 88 : expandedCasual ? 78 : 64;
        HeaderPanel.Visibility = notification ? Visibility.Visible : Visibility.Collapsed;
        HeaderSpacer.Height = new GridLength(notification ? 5 : 0);
        Card.Padding = notification
            ? new Thickness(12, 8, 12, 9)
            : new Thickness(12, 7, 12, 8);
        Card.CornerRadius = notification ? new CornerRadius(12) : new CornerRadius(11);
        MessageText.FontSize = 12.5;
        MessageText.LineHeight = 18;
        MessageText.MaxHeight = notification || expandedCasual ? 38 : 22;
        if (Card.Effect is System.Windows.Media.Effects.DropShadowEffect shadow)
        {
            shadow.BlurRadius = notification ? 10 : 8;
            shadow.ShadowDepth = 1;
            shadow.Opacity = notification ? 0.15 : 0.13;
        }
    }

    private void ConfigureTail(Window petWindow)
    {
        Tail.HorizontalAlignment = HorizontalAlignment.Center;
        Tail.VerticalAlignment = VerticalAlignment.Center;
        Tail.Margin = new Thickness(0);
        Card.Margin = PlacementName switch
        {
            "below" => new Thickness(8, 11, 8, 5),
            "right" => new Thickness(11, 5, 5, 5),
            "left" => new Thickness(5, 5, 11, 5),
            _ => new Thickness(8, 5, 8, 11),
        };

        var petCenterX = petWindow.Left + petWindow.Width / 2;
        var petHeadY = petWindow.Top + petWindow.Height * 0.34;
        switch (PlacementName)
        {
            case "below":
                var belowTailX = Clamp(petCenterX - Left, 28, Width - 28);
                Tail.HorizontalAlignment = HorizontalAlignment.Left;
                Tail.VerticalAlignment = VerticalAlignment.Top;
                Tail.Margin = new Thickness(belowTailX - Tail.Width / 2, 5, 0, 0);
                SetTailAngle(180);
                break;
            case "right":
                var rightTailY = Clamp(petHeadY - Top, 24, Height - 24);
                Tail.HorizontalAlignment = HorizontalAlignment.Left;
                Tail.VerticalAlignment = VerticalAlignment.Top;
                Tail.Margin = new Thickness(5, rightTailY - Tail.Height / 2, 0, 0);
                SetTailAngle(90);
                break;
            case "left":
                var leftTailY = Clamp(petHeadY - Top, 24, Height - 24);
                Tail.HorizontalAlignment = HorizontalAlignment.Right;
                Tail.VerticalAlignment = VerticalAlignment.Top;
                Tail.Margin = new Thickness(0, leftTailY - Tail.Height / 2, 5, 0);
                SetTailAngle(-90);
                break;
            default:
                var aboveTailX = Clamp(petCenterX - Left, 28, Width - 28);
                Tail.HorizontalAlignment = HorizontalAlignment.Left;
                Tail.VerticalAlignment = VerticalAlignment.Bottom;
                Tail.Margin = new Thickness(aboveTailX - Tail.Width / 2, 0, 0, 5);
                SetTailAngle(0);
                break;
        }
    }

    private void SetTailAngle(double angle)
    {
        if (Tail.RenderTransform is System.Windows.Media.RotateTransform rotation)
        {
            rotation.Angle = angle;
        }
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        maximum <= minimum ? minimum : Math.Clamp(value, minimum, maximum);
}
