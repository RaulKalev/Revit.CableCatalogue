using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KaabliKataloog.Services;

namespace KaabliKataloog
{
    /// <summary>
    /// Minimal, explanatory motion for surfaces that appear (sheets, feedback line) – never for list rows.
    /// An element fades in and settles from a small offset. Each animation starts from the element's current
    /// (presentation) value with HandoffBehavior.SnapshotAndReplace, so an interrupted reveal never jumps.
    /// Reduced motion ("Show animations in Windows" off): opacity only, no movement.
    /// WPF has no spring animation; a short ease-out that settles without overshoot stands in for a critically
    /// damped spring.
    /// </summary>
    public static class Motion
    {
        public static void Reveal(FrameworkElement el, double fromY = 6)
        {
            var reduced = ThemeManager.ReducedMotion;
            var duration = TimeSpan.FromMilliseconds(reduced ? 100 : 180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation { From = el.Opacity < 0.99 ? (double?)null : 0.0, To = 1.0, Duration = duration, EasingFunction = ease };
            el.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

            if (reduced) return;
            var t = el.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen)
            {
                t = new TranslateTransform();
                el.RenderTransform = t;
            }
            // Interrupted mid-slide: continue from the current value instead of jumping back to the start.
            var inFlight = Math.Abs(t.Y) > 0.01;
            var slide = new DoubleAnimation { From = inFlight ? (double?)null : fromY, To = 0, Duration = duration, EasingFunction = ease };
            t.BeginAnimation(TranslateTransform.YProperty, slide, HandoffBehavior.SnapshotAndReplace);
        }

        /// <summary>Fades out along the same path it came in, then runs <paramref name="completed"/>.</summary>
        public static void Dismiss(FrameworkElement el, Action completed, double toY = 6)
        {
            var reduced = ThemeManager.ReducedMotion;
            var duration = TimeSpan.FromMilliseconds(reduced ? 80 : 140);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            var fade = new DoubleAnimation { To = 0.0, Duration = duration, EasingFunction = ease };
            fade.Completed += (s, e) => completed?.Invoke();
            el.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

            if (reduced) return;
            var t = el.RenderTransform as TranslateTransform;
            if (t == null || t.IsFrozen)
            {
                t = new TranslateTransform();
                el.RenderTransform = t;
            }
            t.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation { To = toY, Duration = duration, EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        }
    }
}
