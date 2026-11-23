using System;
using System.Windows.Data;
using System.Windows.Markup;
using Vstavka.Core.Localization;

namespace Vstavka.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class TrExtension : MarkupExtension
{
    public string Key {get;set;}="";
    public TrExtension() { }
    public TrExtension(string key) {Key=key;}
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") {Source=Strings.Current,Mode=BindingMode.OneWay}.ProvideValue(serviceProvider);
}
