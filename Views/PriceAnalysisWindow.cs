using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Input;

namespace SmartphoneMonitor.Views
{
    public class PriceAnalysisWindow : Window
    {
        public PriceAnalysisWindow(string brand, string title, int storageGB, List<(DateTime date, decimal price, string title)> history)
        {
            this.Title = "📈 Анализ исторических цен";
            this.Width = 520;
            this.Height = 440;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            this.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)); // Slate-900
            this.BorderBrush = new SolidColorBrush(Color.FromRgb(51, 65, 85)); // Slate-700
            this.BorderThickness = new Thickness(1.5);
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = false;

            // Main layout
            var mainGrid = new Grid { Margin = new Thickness(24) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Stats grid
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer / Close Button

            // Header StackPanel
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            var titleBlock = new TextBlock
            {
                Text = "📈 АНАЛИТИКА И ИСТОРИЯ ЦЕН",
                Foreground = new SolidColorBrush(Color.FromRgb(99, 102, 241)), // Indigo-500
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var modelBlock = new TextBlock
            {
                Text = $"{brand} (Память: {(storageGB > 0 ? $"{storageGB} GB" : "Не указана")})",
                Foreground = Brushes.White,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var origTitleBlock = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // Slate-400
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            headerStack.Children.Add(titleBlock);
            headerStack.Children.Add(modelBlock);
            headerStack.Children.Add(origTitleBlock);
            Grid.SetRow(headerStack, 0);
            mainGrid.Children.Add(headerStack);

            // Calculation of statistics
            decimal minPrice = history.Count > 0 ? history.Min(h => h.price) : 0m;
            decimal maxPrice = history.Count > 0 ? history.Max(h => h.price) : 0m;
            decimal avgPrice = history.Count > 0 ? Math.Round(history.Average(h => h.price), 0) : 0m;

            // Stats row
            var statsGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            statsGrid.Children.Add(CreateStatBox("Мин. цена", minPrice, Color.FromRgb(34, 197, 94), 0)); // Green-500
            statsGrid.Children.Add(CreateStatBox("Средняя", avgPrice, Color.FromRgb(99, 102, 241), 1)); // Indigo-500
            statsGrid.Children.Add(CreateStatBox("Макс. цена", maxPrice, Color.FromRgb(239, 68, 68), 2)); // Red-500

            Grid.SetRow(statsGrid, 1);
            mainGrid.Children.Add(statsGrid);

            // History List
            var listBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate-800
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var listStack = new StackPanel();

            if (history.Count == 0)
            {
                listStack.Children.Add(new TextBlock
                {
                    Text = "Нет исторических данных в базе. Запустите парсинг большего количества страниц для сбора данных.",
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                });
            }
            else
            {
                // Display up to 100 historical records grouped by price
                var displayed = history.Take(100).ToList();
                for (int i = 0; i < displayed.Count; i++)
                {
                    var item = displayed[i];
                    var rowBorder = new Border
                    {
                        Padding = new Thickness(8, 6, 8, 6),
                        Background = i % 2 == 0 ? new SolidColorBrush(Color.FromRgb(30, 41, 59)) : new SolidColorBrush(Color.FromRgb(51, 65, 85)),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 0, 0, 4)
                    };

                    var rowGrid = new Grid();
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) }); // Date
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title
                    rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Price

                    var dateBlock = new TextBlock
                    {
                        Text = item.date.ToString("dd.MM HH:mm"),
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(dateBlock, 0);
                    rowGrid.Children.Add(dateBlock);

                    var nameBlock = new TextBlock
                    {
                        Text = item.title,
                        Foreground = Brushes.White,
                        FontSize = 11,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(4, 0, 4, 0)
                    };
                    Grid.SetColumn(nameBlock, 1);
                    rowGrid.Children.Add(nameBlock);

                    var priceBlock = new TextBlock
                    {
                        Text = $"{item.price:F0} MDL",
                        Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 11,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    Grid.SetColumn(priceBlock, 2);
                    rowGrid.Children.Add(priceBlock);

                    rowBorder.Child = rowGrid;
                    listStack.Children.Add(rowBorder);
                }
            }

            scroll.Content = listStack;
            listBorder.Child = scroll;
            Grid.SetRow(listBorder, 2);
            mainGrid.Children.Add(listBorder);

            // Footer / Close Button
            var closeButton = new Button
            {
                Content = "Закрыть аналитику",
                Height = 36,
                Background = new SolidColorBrush(Color.FromRgb(99, 102, 241)), // Indigo-500
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            closeButton.Click += (s, e) => this.Close();

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(presenterFactory);
            template.VisualTree = borderFactory;
            closeButton.Template = template;

            Grid.SetRow(closeButton, 3);
            mainGrid.Children.Add(closeButton);

            this.Content = mainGrid;

            // Make window draggable
            this.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left) this.DragMove();
            };
        }

        private Border CreateStatBox(string title, decimal value, Color valueColor, int column)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), // Slate-800
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(column == 0 ? 0 : 4, 0, column == 2 ? 0 : 4, 0)
            };

            var stack = new StackPanel();
            var titleBlock = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), // Slate-400
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var valBlock = new TextBlock
            {
                Text = value > 0 ? $"{value:F0} MDL" : "—",
                Foreground = new SolidColorBrush(valueColor),
                FontWeight = FontWeights.Bold,
                FontSize = 16
            };
            stack.Children.Add(titleBlock);
            stack.Children.Add(valBlock);
            border.Child = stack;

            Grid.SetColumn(border, column);
            return border;
        }
    }
}
