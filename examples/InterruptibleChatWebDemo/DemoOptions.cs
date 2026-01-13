namespace InterruptibleChatWebDemo;

public sealed class DemoOptions
{
    public string SystemPrompt { get; set; } =
        "You are a helpful assistant.";

    public float Temperature { get; set; } = 0.4f;
    public int MaxOutputTokens { get; set; } = 800;
}


