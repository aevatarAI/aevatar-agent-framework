using Tui.Widgets.Core;

namespace Tui.Declarative.Elements;

public sealed record ContainerElement<TWidget> : Element
    where TWidget : IWidget
{
    private readonly Func<TWidget> _factory;
    private readonly Action<TWidget>? _configureAction;

    public override Type WidgetType => typeof(TWidget);
    public override IReadOnlyList<ElementChild> Children { get; }

    public ContainerElement(
        string key,
        Func<TWidget> factory,
        IReadOnlyList<ElementChild> children,
        Action<TWidget>? configureAction = null) : base(key)
    {
        _factory = factory;
        Children = children;
        _configureAction = configureAction;
    }

    public override IWidget CreateWidget()
        => _factory();

    public override void Configure(IWidget widget)
    {
        if (_configureAction is null)
            return;

        if (widget is TWidget typed)
            _configureAction(typed);
    }
}
