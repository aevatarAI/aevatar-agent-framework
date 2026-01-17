namespace Tui.Widgets.Containers;

public interface IWidgetContainer
{
    void SetChildren(IReadOnlyList<WidgetSlot> slots);
}
