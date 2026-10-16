using System.Windows;
using System.Windows.Controls;
using TradingBacktest.Abstractions;
using TradingBacktest.ViewModels;

namespace TradingBacktest.Controls
{
    /// <summary>
    /// Picks the editor for an indicator parameter from the type the plug-in declared, so
    /// the window never has to know what a particular indicator accepts. The templates are
    /// looked up by key - "ParameterInteger", "ParameterChoice", and so on - in the
    /// element's own resources first, then in the application's.
    /// </summary>
    public sealed class ParameterTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            var parameter = item as IndicatorParameterViewModel;
            if (parameter == null)
            {
                return base.SelectTemplate(item, container);
            }

            return Find(parameter.TemplateKey, container) ?? base.SelectTemplate(item, container);
        }

        private static DataTemplate Find(string key, DependencyObject container)
        {
            FrameworkElement element = container as FrameworkElement;
            while (element != null)
            {
                if (element.Resources.Contains(key))
                {
                    var template = element.Resources[key] as DataTemplate;
                    if (template != null)
                    {
                        return template;
                    }
                }

                element = element.Parent as FrameworkElement;
            }

            if (Application.Current != null && Application.Current.TryFindResource(key) != null)
            {
                return Application.Current.TryFindResource(key) as DataTemplate;
            }

            return null;
        }
    }
}