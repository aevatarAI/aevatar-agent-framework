using Aevatar.Agents.Persistence.Graph.Core.IR;

namespace Aevatar.Agents.Persistence.Graph.Core;

/// <summary>
/// 编译器接口：将 <see cref="GraphPlan"/> 编译为后端特定命令。
/// <para>何时用：为不同运行时/存储实现自定义编译过程。</para>
/// </summary>
public interface IGraphCompiler<TTarget>
{
    /// <summary>
    /// 将图操作计划编译为目标命令对象。
    /// </summary>
    /// <param name="plan">包含单个 <see cref="GraphPlan.Operation"/> 的计划，不能为空。</param>
    /// <returns>编译后的后端命令。</returns>
    /// <exception cref="NotSupportedException">遇到未支持的 <see cref="GraphOperation"/> 时抛出。</exception>
    TTarget Compile(GraphPlan plan);
}
