namespace Tui.Declarative.Reconciliation;

public interface IWidgetLifecycle
{
    void OnMounted();
    void OnUnmounted();
}
