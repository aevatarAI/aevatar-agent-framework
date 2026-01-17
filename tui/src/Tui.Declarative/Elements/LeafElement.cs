using Tui.Widgets.Core;

namespace Tui.Declarative.Elements;

public sealed record LeafElement<TWidget>(
    string Key,
    Func<TWidget> Factory,
    Action<TWidget>? ConfigureAction = null) : Element(Key)
    where TWidget : IWidget
{
    public override Type WidgetType => typeof(TWidget);

    public override IWidget CreateWidget()
        => Factory();

    public override void Configure(IWidget widget)
    {
        if (ConfigureAction is null)
            return;

        if (widget is TWidget typed)
            ConfigureAction(typed);
    }
}
