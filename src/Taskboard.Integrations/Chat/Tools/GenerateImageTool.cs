using System.Text.Json;
using Taskboard.Application.Contracts.Chat;

namespace Taskboard.Integrations.Chat.Tools;

/// <summary>
/// Generates an image via the conversation provider's
/// <c>/v1/images/generations</c> endpoint (RF-009) — the file is persisted
/// under the data dir and referenced by the message.
/// </summary>
public sealed class GenerateImageTool(OpenAiCompatibleClient client, ChatImageStore imageStore) : IChatTool
{
    public string Name => "generate_image";
    public string Description =>
        "Generate an image from a text prompt using the provider's image endpoint. "
        + "The image is attached to the conversation.";
    public string ParametersJson => """
        {"type":"object","properties":{"prompt":{"type":"string","description":"Image prompt"},"size":{"type":"string","description":"Optional size, e.g. 1024x1024"}},"required":["prompt"]}
        """;

    public async Task<ChatToolResult> ExecuteAsync(
        JsonElement arguments, ChatToolContext context, CancellationToken cancellationToken)
    {
        var prompt = arguments.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : string.Empty;
        var size = arguments.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = "prompt is required" }), Refused: true, "empty prompt");
        }

        if (string.IsNullOrWhiteSpace(context.ImageModel))
        {
            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                error = "no image model configured — set the image capability default in Settings → Chat",
            }));
        }

        try
        {
            var b64 = await client.GenerateImageAsync(
                context.ProviderBaseUrl, context.ProviderApiKey, context.ImageModel, prompt, size, cancellationToken)
                .ConfigureAwait(false);
            var imagePath = imageStore.Save(b64);
            if (imagePath is null)
            {
                return new ChatToolResult(JsonSerializer.Serialize(new { error = "failed to persist generated image" }));
            }

            return new ChatToolResult(JsonSerializer.Serialize(new
            {
                prompt,
                imagePath,
                message = "image generated and attached to the conversation",
            }));
        }
        catch (ChatProviderException ex)
        {
            return new ChatToolResult(JsonSerializer.Serialize(new { error = ex.Message }));
        }
    }
}
