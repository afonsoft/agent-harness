using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskboard.Application.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.Repositories;

namespace Taskboard.Server.Services.Slack;

/// <summary>
/// SPEC-20261025 C1: outbound side of the Slack channel — joins the
/// <see cref="IChatRunNotifier"/> fan-out. A pending approval on a
/// channel-bound conversation posts an Approve/Reject card in the bound
/// thread; a terminal run posts a one-line status. Only conversations with
/// a <see cref="ChatChannelBinding"/> are notified — UI-only conversations
/// stay UI-only.
/// </summary>
public sealed class SlackChannelNotifier(
    IRepository<ChatChannelBinding> bindings,
    SlackApiClient slack,
    ILogger<SlackChannelNotifier> logger) : IChatRunNotifier
{
    public async Task RunCompletedAsync(ChatRun run, CancellationToken cancellationToken)
    {
        if (!slack.IsReady)
        {
            return;
        }

        var binding = await BindingForAsync(run.ConversationId.Value, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            return;
        }

        await SafePostAsync(
            binding, $"Run {run.Id.Value[..Math.Min(8, run.Id.Value.Length)]} finished — {run.Status.Value}",
            blocks: null, cancellationToken).ConfigureAwait(false);
    }

    public async Task ApprovalAskedAsync(
        ChatRun run, string approvalId, string toolName, CancellationToken cancellationToken)
    {
        if (!slack.IsReady)
        {
            return;
        }

        var binding = await BindingForAsync(run.ConversationId.Value, cancellationToken).ConfigureAwait(false);
        if (binding is null)
        {
            return;
        }

        var blocks = JsonSerializer.SerializeToElement(new object[]
        {
            new
            {
                type = "section",
                text = new
                {
                    type = "mrkdwn",
                    text = $"*Approval needed* — `{toolName}` (run `{run.Id.Value[..Math.Min(8, run.Id.Value.Length)]}`)",
                },
            },
            new
            {
                type = "actions",
                elements = new object[]
                {
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "Approve" },
                        style = "primary",
                        action_id = $"{SlackEnvelopeRouter.ApprovalActionPrefix}{approvalId}",
                        value = "allow",
                    },
                    new
                    {
                        type = "button",
                        text = new { type = "plain_text", text = "Reject" },
                        style = "danger",
                        action_id = $"{SlackEnvelopeRouter.ApprovalActionPrefix}{approvalId}",
                        value = "deny",
                    },
                },
            },
        });

        await SafePostAsync(binding, $"Approval needed — {toolName}", blocks, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ChatChannelBinding?> BindingForAsync(string conversationId, CancellationToken ct) =>
        await bindings.Query
            .FirstOrDefaultAsync(
                b => b.Channel == SlackApiClient.ChannelName && b.ConversationId.Value == conversationId, ct)
            .ConfigureAwait(false);

    private async Task SafePostAsync(
        ChatChannelBinding binding, string text, JsonElement? blocks, CancellationToken ct)
    {
        try
        {
            await slack.PostMessageAsync(
                binding.ExternalChannelId, binding.ExternalThreadId, text, blocks, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "slack: post to {Channel} failed", binding.ExternalChannelId);
        }
    }
}
