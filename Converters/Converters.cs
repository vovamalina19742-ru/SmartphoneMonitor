using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SmartphoneMonitor.Converters
{
    public class PercentWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
            {
                return 0.0;
            }
            if (!double.TryParse(values[0]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
            {
                return 0.0;
            }
            if (!double.TryParse(values[1]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result2))
            {
                return 0.0;
            }
            return Math.Max(0.0, result2 * result / 100.0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool flag = false;
            if (v is bool b)
            {
                flag = b;
            }
            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            return v is Visibility visibility && visibility == Visibility.Visible;
        }
    }

    public class BoolToHiddenConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool flag = false;
            if (v is bool b)
            {
                flag = b;
            }
            return flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            return v is Visibility visibility && visibility != Visibility.Visible;
        }
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool flag = false;
            if (v is bool b)
            {
                flag = b;
            }
            if (!flag)
            {
                return new SolidColorBrush(Color.FromRgb(21, 101, 192));
            }
            return new SolidColorBrush(Color.FromRgb(67, 160, 71));
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotImplementedException();
        }
    }

    public class ZeroToHiddenConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            return (v is int num && num == 0) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotImplementedException();
        }
    }

    public class IntEqualsConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (v is int num && p != null)
            {
                if (int.TryParse(p.ToString(), out int result))
                {
                    return num == result;
                }
            }
            return false;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotImplementedException();
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0)
            {
                return 0.0;
            }
            if (double.TryParse(values[0]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var fraction))
            {
                return Math.Max(0.0, fraction * 150.0);
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IntEqToBrushConverter : IValueConverter
    {
        private static readonly SolidColorBrush ActiveBg = new SolidColorBrush(Color.FromRgb(21, 101, 192));
        private static readonly SolidColorBrush InactiveBg = new SolidColorBrush(Color.FromRgb(238, 242, 255));

        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (!(v is int num) || !(p is string s) || !int.TryParse(s, out var result) || num != result)
            {
                return InactiveBg;
            }
            return ActiveBg;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotImplementedException();
        }
    }

    public class IntEqToFgConverter : IValueConverter
    {
        private static readonly SolidColorBrush ActiveFg = new SolidColorBrush(Colors.White);
        private static readonly SolidColorBrush InactiveFg = new SolidColorBrush(Color.FromRgb(21, 101, 192));

        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            if (!(v is int num) || !(p is string s) || !int.TryParse(s, out var result) || num != result)
            {
                return InactiveFg;
            }
            return ActiveFg;
        }

        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            throw new NotImplementedException();
        }
    }
}
