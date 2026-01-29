# EventModules

事件模块与路由机制实现，用于在 Agent 执行过程中按规则分发事件与步骤。

## 主要内容
- `IEventModule` / `IEventModuleFactory`：事件模块契约。
- `EventRoute` / `IEventRouteEvaluator`：路由与评估接口。
- `RoutedEventModule` / `StepExecutionModule`：基于路由的模块与步骤执行模块。

