using Aevatar.Agents.Cognitive.Researching.Sessions;
using Aevatar.Agents.Cognitive.Streaming;

namespace Aevatar.Agents.Cognitive.Researching.Workflow;

public interface IResearchingStreamEventSinkFactory
{
    IResearchStreamEventSink? Create(ResearchSession session, string runId);
}

public sealed class NullResearchingStreamEventSinkFactory : IResearchingStreamEventSinkFactory
{
    public static readonly NullResearchingStreamEventSinkFactory Instance = new();

    private NullResearchingStreamEventSinkFactory() { }

    public IResearchStreamEventSink? Create(ResearchSession session, string runId) => null;
}
