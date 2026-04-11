using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VerificarDeXMLNFCE
{
    /// <summary>
    /// Behavior anexável que exibe um texto de placeholder em TextBox quando vazio.
    /// Uso no XAML: local:PlaceholderBehavior.Placeholder="Seu texto aqui"
    /// </summary>
    public static class PlaceholderBehavior
    {
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.RegisterAttached(
                "Placeholder",
                typeof(string),
                typeof(PlaceholderBehavior),
                new PropertyMetadata(string.Empty, OnPlaceholderChanged));

        public static string GetPlaceholder(DependencyObject obj) =>
            (string)obj.GetValue(PlaceholderProperty);

        public static void SetPlaceholder(DependencyObject obj, string value) =>
            obj.SetValue(PlaceholderProperty, value);

        private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            tb.Loaded           -= Tb_Loaded;
            tb.TextChanged      -= Tb_TextChanged;
            tb.GotFocus         -= Tb_GotFocus;
            tb.LostFocus        -= Tb_LostFocus;

            tb.Loaded           += Tb_Loaded;
            tb.TextChanged      += Tb_TextChanged;
            tb.GotFocus         += Tb_GotFocus;
            tb.LostFocus        += Tb_LostFocus;
        }

        private static void Tb_Loaded(object sender, RoutedEventArgs e)   => UpdatePlaceholder((TextBox)sender);
        private static void Tb_TextChanged(object sender, TextChangedEventArgs e) => UpdatePlaceholder((TextBox)sender);
        private static void Tb_GotFocus(object sender, RoutedEventArgs e)  => UpdatePlaceholder((TextBox)sender);
        private static void Tb_LostFocus(object sender, RoutedEventArgs e) => UpdatePlaceholder((TextBox)sender);

        private static void UpdatePlaceholder(TextBox tb)
        {
            bool isEmpty = string.IsNullOrEmpty(tb.Text);
            bool focused = tb.IsFocused;

            if (isEmpty && !focused)
            {
                tb.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)); // #475569
                tb.Text       = GetPlaceholder(tb);
                tb.Tag        = "placeholder";
            }
            else if ((string?)tb.Tag == "placeholder" && focused)
            {
                tb.Text       = "";
                tb.Tag        = null;
                tb.Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)); // #F1F5F9
            }
        }
    }
}
