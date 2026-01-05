using System;
using Orleans.Runtime;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.Stream;

/// <summary>
/// Orleans Message Stream Provider
/// Creates and manages Orleans Stream for each Agent
/// </summary>
public class OrleansMessageStreamProvider
{
    private readonly IStreamProvider _streamProvider;
    private readonly string _streamNamespace;
    
    public OrleansMessageStreamProvider(IStreamProvider streamProvider, string streamNamespace = AevatarAgentsOrleansConstants.StreamNamespace)
    {
        _streamProvider = streamProvider;
        _streamNamespace = streamNamespace;
    }
    
    /// <summary>
    /// Get Agent's Stream
    /// </summary>
    public OrleansMessageStream GetStream(string agentId)
    {
        var streamId = StreamId.Create(_streamNamespace, agentId);
        var stream = _streamProvider.GetStream<byte[]>(streamId);
        return new OrleansMessageStream(agentId, stream);
    }
}

