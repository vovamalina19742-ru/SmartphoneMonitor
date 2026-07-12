using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SmartphoneMonitor.Models;

namespace SmartphoneMonitor.Views
{
    public class NotificationWindow : Window
    {
        private static readonly List<NotificationWindow> ActiveWindows = new List<NotificationWindow>();
        private static readonly object LockObj = new object();

        public NotificationWindow(HotDeal? deal, bool isSummary = false, int totalCount = 0)
        {
            this.Width = 360;
            this.Height = 110;
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = true;
            this.Background = Brushes.Transparent;
            this.Topmost = true;
            this.ShowInTaskbar = false;
            this.Title = isSummary ? "Новые предложения!" : "Новая сделка!";

            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new LinearGradientBrush(
                    Color.FromRgb(15, 23, 42),
                    Color.FromRgb(30, 41, 59),
                    45.0
                ),
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                BorderThickness = new Thickness(1.5),
                Padding = new Thickness(16),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 270,
                    ShadowDepth = 4,
                    Opacity = 0.4,
                    BlurRadius = 12
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });

            var emojiBlock = new TextBlock
            {
                Text = isSummary ? "🔔" : "🔥",
                FontSize = 28,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetColumn(emojiBlock, 0);
            grid.Children.Add(emojiBlock);

            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleBlock = new TextBlock
            {
                Text = isSummary ? "Найдено много сделок!" : "Горячее предложение!",
                Foreground = new SolidColorBrush(
                    isSummary ? Color.FromRgb(59, 130, 246) : Color.FromRgb(249, 115, 22)
                ),
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var detailBlock = new TextBlock
            {
                Text = isSummary 
                    ? $"Обнаружено {totalCount} выгодных предложений.\nПерейдите во вкладку «Результаты»."
                    : (deal != null 
                        ? $"{deal.Title.Replace("⭐ [Рекомендация: ", "").Replace("] ", "")}\nЦена: {deal.PriceValue:F0} lei (Скидка {deal.DiscountPercent:F0}%)"
                        : ""),
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            textStack.Children.Add(titleBlock);
            textStack.Children.Add(detailBlock);
            Grid.SetColumn(textStack, 1);
            grid.Children.Add(textStack);

            var closeBtn = new TextBlock
            {
                Text = "✕",
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            };
            closeBtn.MouseLeftButtonDown += (s, e) => this.Close();
            Grid.SetColumn(closeBtn, 2);
            grid.Children.Add(closeBtn);

            border.Child = grid;
            this.Content = border;

            border.Cursor = Cursors.Hand;
            border.MouseLeftButtonDown += (s, e) =>
            {
                if (e.OriginalSource == closeBtn) return;
                if (!isSummary && deal != null && !string.IsNullOrEmpty(deal.Url))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = deal.Url,
                            UseShellExecute = true
                        });
                    }
                    catch {}
                }
                this.Close();
            };

            this.Closed += (s, e) =>
            {
                lock (LockObj)
                {
                    ActiveWindows.Remove(this);
                }
                Application.Current.Dispatcher.InvokeAsync(RearrangeWindows);
            };

            this.Loaded += (s, e) =>
            {
                var screen = SystemParameters.WorkArea;
                int index = 0;
                lock (LockObj)
                {
                    index = ActiveWindows.Count;
                    ActiveWindows.Add(this);
                }

                double offset = index * (this.Height + 10);
                double targetLeft = screen.Right - this.Width - 20;
                double startTop = screen.Bottom;
                double targetTop = screen.Bottom - this.Height - 20 - offset;
                this.Left = targetLeft;
                this.Top = startTop;

                var animation = new DoubleAnimation
                {
                    From = startTop,
                    To = targetTop,
                    Duration = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                this.BeginAnimation(Window.TopProperty, animation);

                var fadeOut = new DoubleAnimation
                {
                    From = 1.0,
                    To = 0.0,
                    Duration = TimeSpan.FromMilliseconds(500)
                };
                fadeOut.Completed += (s2, e2) => this.Close();

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
                timer.Tick += (s2, e2) =>
                {
                    timer.Stop();
                    this.BeginAnimation(Window.OpacityProperty, fadeOut);
                };
                timer.Start();
            };
        }

        private static void RearrangeWindows()
        {
            var screen = SystemParameters.WorkArea;
            lock (LockObj)
            {
                for (int i = 0; i < ActiveWindows.Count; i++)
                {
                    var w = ActiveWindows[i];
                    double targetTop = screen.Bottom - w.Height - 20 - (i * (w.Height + 10));
                    var animation = new DoubleAnimation
                    {
                        To = targetTop,
                        Duration = TimeSpan.FromMilliseconds(300),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    w.BeginAnimation(Window.TopProperty, animation);
                }
            }
        }
    }
}
