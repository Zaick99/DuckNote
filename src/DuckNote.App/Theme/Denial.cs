using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DuckNote.App.Theme;

public static class Denial
{
    private const int BackspaceInterval = 32;
    private const int RedHold = 900;

    public sealed record Stage(
        PasswordBox[] Fields,
        TextBlock[] Labels,
        Shape[] Rings,
        RotateTransform[] Spins,
        TranslateTransform? Shake);

    public static Border? AlarmOf(PasswordBox field) =>
        field.Template?.FindName("alarm", field) as Border;

    public static void Play(Stage stage, string theme, Action? then = null)
    {
        Motion.Shake(stage.Shake);
        Redden(stage.Rings, stage.Spins, theme);

        Color red = ThemeTokens.Parse(ThemeTokens.Colour(theme, "Red"));
        foreach (TextBlock label in stage.Labels)
        {
            Blush(label, red);
        }

        foreach (PasswordBox field in stage.Fields)
        {
            if (AlarmOf(field) is { } alarm)
            {
                Flash(alarm);
            }
        }

        for (int i = 0; i < stage.Fields.Length; i++)
        {
            Erase(stage.Fields[i], i == 0 ? then : null);
        }
    }

    private static void Blush(TextBlock label, Color red)
    {
        if (label.Foreground is not SolidColorBrush { IsFrozen: false } brush)
        {
            Color resting = label.Foreground is SolidColorBrush existing ? existing.Color : Colors.Gray;
            brush = new SolidColorBrush(resting);
            label.Foreground = brush;
        }

        ColorAnimation blush = new(red, new Duration(TimeSpan.FromMilliseconds(140)))
        {
            AutoReverse = true,
            BeginTime = TimeSpan.Zero,
            EasingFunction = Motion.Ease()
        };

        brush.BeginAnimation(SolidColorBrush.ColorProperty, blush);
    }

    private static void Flash(Border alarm)
    {
        DoubleAnimationUsingKeyFrames pulse = new()
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(RedHold))
        };

        foreach ((int at, double value) in ((int, double)[])[(0, 0), (120, 1), (520, 1), (900, 0)])
        {
            pulse.KeyFrames.Add(new EasingDoubleKeyFrame(
                value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at)), Motion.Ease()));
        }

        alarm.BeginAnimation(UIElement.OpacityProperty, pulse);
    }

    private static void Redden(Shape[] rings, RotateTransform[] spins, string theme)
    {
        SolidColorBrush red = ThemeTokens.Brush(ThemeTokens.Colour(theme, "Red"));
        string[] resting = ["Accent", "Blue"];

        for (int i = 0; i < rings.Length; i++)
        {
            rings[i].Stroke = red;
            rings[i].Opacity = 0.95;
        }

        foreach (RotateTransform spin in spins)
        {
            spin.BeginAnimation(RotateTransform.AngleProperty, Motion.Spin(620));
        }

        DispatcherTimer back = new() { Interval = TimeSpan.FromMilliseconds(RedHold) };
        back.Tick += (_, _) =>
        {
            back.Stop();

            for (int i = 0; i < rings.Length; i++)
            {
                rings[i].Stroke = ThemeTokens.Brush(
                    ThemeTokens.Colour(theme, resting[Math.Min(i, resting.Length - 1)]));
                rings[i].Opacity = i == 0 ? 0.5 : 0.45;
            }

            foreach (RotateTransform spin in spins)
            {
                spin.BeginAnimation(RotateTransform.AngleProperty, null);
                spin.Angle = 0;
            }
        };
        back.Start();
    }

    private static void Erase(PasswordBox field, Action? then)
    {
        DispatcherTimer backspace = new() { Interval = TimeSpan.FromMilliseconds(BackspaceInterval) };
        backspace.Tick += (_, _) =>
        {
            string typed = field.Password;
            if (typed.Length == 0)
            {
                backspace.Stop();
                field.Focus();
                then?.Invoke();
                return;
            }

            field.Password = typed[..^1];
        };
        backspace.Start();
    }
}
