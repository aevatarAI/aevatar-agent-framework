using Tui.Core.Primitives;

namespace Tui.Widgets.Core;

public interface IWidget
{
    void Render(WidgetRenderContext context);
}
