using Tui.Core.Layout;
using Tui.Widgets.Core;

namespace Tui.Declarative.Elements;

public abstract record Element(string Key)
{
    public abstract Type WidgetType { get; }
    public abstract IWidget CreateWidget();
    public abstract void Configure(IWidget widget);
    public virtual IReadOnlyList<ElementChild> Children => Array.Empty<ElementChild>();
}

public sealed record ElementChild(Element Element, FlexItem Layout);
