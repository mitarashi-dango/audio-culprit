using System;
using System.Windows.Markup;
using AudioCulprit.Core;

namespace AudioCulprit;

public sealed class LocalizeExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(key);
}
