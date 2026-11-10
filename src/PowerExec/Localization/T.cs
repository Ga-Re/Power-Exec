using System.Windows.Markup;
using PowerExec.Core.Localization;

namespace PowerExec.Localization;

/// <summary>Übersetzter Text in XAML: <c>Text="{l:T 'Speichern'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
