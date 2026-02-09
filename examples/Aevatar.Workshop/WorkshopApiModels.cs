namespace Aevatar.Workshop;

public sealed record SetApiKeyInput(string? ProviderName, string? ApiKey);
public sealed record SetDefaultProviderInput(string? ProviderName);
