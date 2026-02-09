namespace Aevatar.Agents.AI.Core;

public interface IEventModuleFactory
{
    bool TryCreate(string name, out IEventModule module);
}
