using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Taskboard.Chat;
using Taskboard.Domain.Entities.Chat;
using Taskboard.EntityFrameworkCore.ValueConverters;
using Taskboard.ValueObjects;

namespace Taskboard.EntityFrameworkCore.Configurations;

public sealed class ChatProviderConfiguration : IEntityTypeConfiguration<ChatProvider>
{
    public void Configure(EntityTypeBuilder<ChatProvider> builder)
    {
        builder.ToTable("ChatProviders");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(128);

        builder.HasIndex(p => p.Name)
            .IsUnique();

        builder.Property(p => p.BaseUrl)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(p => p.ApiKey)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(p => p.Enabled)
            .IsRequired();

        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.UpdatedAt);
    }
}

public sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("ChatConversations");

        builder.Property(c => c.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Title)
            .IsRequired()
            .HasMaxLength(240);

        builder.Property(c => c.Model)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.ProviderName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(c => c.CreatedAt);
        builder.Property(c => c.UpdatedAt);
        builder.Property(c => c.ArchivedAt);

        // SPEC-20261005-chat-tool-approval RF-006: per-conversation preset +
        // RF-004 allowed-list (JSON array of tool names).
        builder.Property(c => c.PermissionPreset)
            .IsRequired()
            .HasMaxLength(16);
        builder.Property(c => c.AllowedToolsJson);

        // SPEC-20261005-chat-plan-mode RF-001: on|off.
        builder.Property(c => c.PlanMode)
            .IsRequired()
            .HasMaxLength(8)
            .HasDefaultValue(ChatPlanModes.Off);

        // SPEC-20261005-chat-fork-steering RF-001: fork lineage.
        builder.Property(c => c.ForkedFromConversationId)
            .HasMaxLength(128);
        builder.Property(c => c.ForkedAtMessageId)
            .HasMaxLength(128);
        builder.HasIndex(c => c.ForkedFromConversationId);

        builder.Property(c => c.Version)
            .IsConcurrencyToken();

        builder.HasIndex(c => c.UpdatedAt);
        builder.HasIndex(c => c.ArchivedAt);

        builder.HasMany(c => c.Messages)
            .WithOne()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// SPEC-20261005-chat-background-resume RF-001: durable run rows — the detached
/// executor's lifecycle + in-flight checkpoints. Cascade-deleted with the
/// conversation.
/// </summary>
public sealed class ChatRunConfiguration : IEntityTypeConfiguration<ChatRun>
{
    public void Configure(EntityTypeBuilder<ChatRun> builder)
    {
        builder.ToTable("ChatRuns");

        builder.Property(r => r.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatRunId>());

        builder.HasKey(r => r.Id);

        builder.Property(r => r.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(r => r.TriggerMessageId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatMessageId>());

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(new StringValueObjectConverter<ChatRunStatus>());

        builder.Property(r => r.PartialContent);
        builder.Property(r => r.PartialReasoning);
        builder.Property(r => r.Error)
            .HasMaxLength(1024);
        builder.Property(r => r.TokensIn);
        builder.Property(r => r.TokensOut);
        // SPEC-20261005-chat-context-management RF-007: meter + compaction stats.
        builder.Property(r => r.ContextTokensLimit);
        builder.Property(r => r.CompactionCount)
            .HasDefaultValue(0);
        builder.Property(r => r.CreatedAt);
        builder.Property(r => r.StartedAt);
        // SPEC-20261012-chat-run-controls: parked-run timestamp.
        builder.Property(r => r.PausedAt);
        builder.Property(r => r.FinishedAt);

        builder.Property(r => r.Version)
            .IsConcurrencyToken();

        // One live run lookup per conversation (attach path + history badge)
        // and the dispatcher's queued-FIFO scan.
        builder.HasIndex(r => new { r.ConversationId, r.Status });
        builder.HasIndex(r => r.CreatedAt);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// SPEC-20261005-chat-tool-approval RF-001: pending tool-execution questions —
/// replayable on re-attach, answerable from any tab, audited by
/// decided-at/by. Cascade-deleted with the conversation.
/// </summary>
public sealed class ChatApprovalConfiguration : IEntityTypeConfiguration<ChatApproval>
{
    public void Configure(EntityTypeBuilder<ChatApproval> builder)
    {
        builder.ToTable("ChatApprovals");

        builder.Property(a => a.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatApprovalId>());

        builder.HasKey(a => a.Id);

        builder.Property(a => a.RunId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatRunId>());

        builder.Property(a => a.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(a => a.ToolCallId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.ToolName)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(a => a.ArgumentsPreview)
            .IsRequired()
            .HasMaxLength(ChatApproval.PlanPreviewMaxLength);

        // SPEC-20261005-chat-plan-mode RF-003: tool-call | plan-review.
        builder.Property(a => a.Kind)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue(ChatApprovalKind.ToolCall)
            .HasConversion(new StringValueObjectConverter<ChatApprovalKind>());

        builder.Property(a => a.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(new StringValueObjectConverter<ChatApprovalStatus>());

        builder.Property(a => a.RequestedAt);
        builder.Property(a => a.DecidedAt);
        builder.Property(a => a.Decision)
            .HasMaxLength(1024);
        builder.Property(a => a.DecidedBy)
            .HasMaxLength(16)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : ChatApprovalDecidedBy.From(v));

        // SPEC-20261013-chat-risk-approvals: classifier tier + reason.
        builder.Property(a => a.Risk)
            .HasMaxLength(8);
        builder.Property(a => a.RiskReason)
            .HasMaxLength(200);

        builder.Property(a => a.Version)
            .IsConcurrencyToken();

        // Re-attach replay + sidebar badge scan (RF-007) + pending
        // plan-review lookup when plan mode flips off mid-review.
        builder.HasIndex(a => new { a.ConversationId, a.Status });
        builder.HasIndex(a => a.RunId);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(a => a.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// SPEC-20261005-chat-background-resume RF-009: browser Web Push endpoints —
/// unique per endpoint; re-subscribe rotates keys in place.
/// </summary>
public sealed class ChatPushSubscriptionConfiguration : IEntityTypeConfiguration<ChatPushSubscription>
{
    public void Configure(EntityTypeBuilder<ChatPushSubscription> builder)
    {
        builder.ToTable("ChatPushSubscriptions");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatPushSubscriptionId>());

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Endpoint)
            .IsRequired()
            .HasMaxLength(2048);

        builder.HasIndex(s => s.Endpoint)
            .IsUnique();

        builder.Property(s => s.P256dh)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.Auth)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(s => s.UserAgent)
            .HasMaxLength(512);

        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.UpdatedAt);

        builder.Property(s => s.Version)
            .IsConcurrencyToken();
    }
}

public sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");

        builder.Property(m => m.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatMessageId>());

        builder.HasKey(m => m.Id);

        builder.Property(m => m.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(m => m.Role)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(new StringValueObjectConverter<ChatMessageRole>());

        builder.Property(m => m.Content)
            .IsRequired();

        builder.Property(m => m.ToolCallsJson);
        builder.Property(m => m.ToolCallId)
            .HasMaxLength(128);
        builder.Property(m => m.ToolName)
            .HasMaxLength(64);
        builder.Property(m => m.Refused)
            .IsRequired();
        builder.Property(m => m.ImagePath)
            .HasMaxLength(512);
        builder.Property(m => m.TokensIn);
        builder.Property(m => m.TokensOut);
        builder.Property(m => m.Model)
            .HasMaxLength(128);
        // SPEC-20261005-chat-context-management RF-004: "normal" | "summary" +
        // the stored-message bound a summary supersedes on the wire.
        builder.Property(m => m.Kind)
            .IsRequired()
            .HasMaxLength(16)
            .HasDefaultValue(ChatMessageKinds.Normal);
        builder.Property(m => m.SupersedesUntilMessageId)
            .HasMaxLength(128);
        // SPEC-20261005-chat-fork-steering RF-001: back-pointer into the
        // source conversation the copy came from.
        builder.Property(m => m.ForkedFromMessageId)
            .HasMaxLength(128);
        builder.Property(m => m.CreatedAt);

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}

/// <summary>
/// SPEC-20261005-chat-fork-steering RF-005: durable steer inbox — one row per
/// pending steering message, claimed by the executor at tool-result
/// boundaries. Cascade-deleted with the conversation.
/// </summary>
public sealed class ChatSteerConfiguration : IEntityTypeConfiguration<ChatSteer>
{
    public void Configure(EntityTypeBuilder<ChatSteer> builder)
    {
        builder.ToTable("ChatSteers");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatSteerId>());

        builder.HasKey(s => s.Id);

        builder.Property(s => s.RunId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatRunId>());

        builder.Property(s => s.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(s => s.Content)
            .IsRequired();

        builder.Property(s => s.CreatedAt);
        builder.Property(s => s.ClaimedAt);
        builder.Property(s => s.AttachmentIdsJson);

        // Drain scan (unclaimed per run) + cancel lookup.
        builder.HasIndex(s => new { s.RunId, s.ClaimedAt });
        builder.HasIndex(s => s.ConversationId);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>SPEC-20261005-chat-attachments-feedback RF-001.</summary>
public sealed class ChatAttachmentConfiguration : IEntityTypeConfiguration<ChatAttachment>
{
    public void Configure(EntityTypeBuilder<ChatAttachment> builder)
    {
        builder.ToTable("ChatAttachments");

        builder.Property(a => a.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatAttachmentId>());

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(a => a.MessageId)
            .HasMaxLength(128)
            .HasConversion(new NullableStringIdValueConverter<ChatMessageId>());

        builder.Property(a => a.FileName)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(a => a.ContentType)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(a => a.ByteSize);

        builder.Property(a => a.StoragePath)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(a => a.Sha256)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(a => a.CreatedAt);
        builder.Property(a => a.BoundAt);

        // Orphan sweep (staged + old) and per-message load for wire/UI.
        builder.HasIndex(a => new { a.ConversationId, a.MessageId });
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => a.MessageId);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(a => a.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>SPEC-20261005-chat-attachments-feedback RF-005 — one row per message.</summary>
public sealed class ChatMessageFeedbackConfiguration : IEntityTypeConfiguration<ChatMessageFeedback>
{
    public void Configure(EntityTypeBuilder<ChatMessageFeedback> builder)
    {
        builder.ToTable("ChatMessageFeedbacks");

        builder.Property(f => f.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatMessageFeedbackId>());

        builder.HasKey(f => f.Id);

        builder.Property(f => f.MessageId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatMessageId>());

        builder.Property(f => f.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(f => f.Rating)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(f => f.Category)
            .HasMaxLength(32);

        builder.Property(f => f.Note)
            .HasMaxLength(ChatMessageFeedback.MaxNoteLength);

        builder.Property(f => f.Version)
            .IsConcurrencyToken();

        builder.Property(f => f.CreatedAt);
        builder.Property(f => f.UpdatedAt);

        // Upsert target + sidebar "com feedback negativo" filter (RF-006).
        builder.HasIndex(f => f.MessageId)
            .IsUnique();
        builder.HasIndex(f => new { f.ConversationId, f.Rating });

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(f => f.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>SPEC-20261005-chat-attachments-feedback RF-007.</summary>
public sealed class ChatRunDeliverableConfiguration : IEntityTypeConfiguration<ChatRunDeliverable>
{
    public void Configure(EntityTypeBuilder<ChatRunDeliverable> builder)
    {
        builder.ToTable("ChatRunDeliverables");

        builder.Property(d => d.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatRunDeliverableId>());

        builder.HasKey(d => d.Id);

        builder.Property(d => d.RunId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatRunId>());

        builder.Property(d => d.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(d => d.Path)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(d => d.AddedLines);
        builder.Property(d => d.RemovedLines);

        builder.Property(d => d.Source)
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(d => d.Summary)
            .HasMaxLength(512);

        builder.Property(d => d.CreatedAt);

        builder.HasIndex(d => d.RunId);
        builder.HasIndex(d => d.ConversationId);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(d => d.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>SPEC-20261005-chat-jobs-schedule-search RF-001 — one row per background job.</summary>
public sealed class ChatJobConfiguration : IEntityTypeConfiguration<ChatJob>
{
    public void Configure(EntityTypeBuilder<ChatJob> builder)
    {
        builder.ToTable("ChatJobs");

        builder.Property(j => j.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatJobId>());

        builder.HasKey(j => j.Id);

        builder.Property(j => j.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(j => j.RunId)
            .HasMaxLength(128)
            .HasConversion(new NullableStringIdValueConverter<ChatRunId>());

        builder.Property(j => j.Command)
            .HasMaxLength(ChatJob.MaxCommandLength)
            .IsRequired();

        builder.Property(j => j.Status)
            .HasMaxLength(16)
            .IsRequired()
            .HasConversion(new StringValueObjectConverter<ChatJobStatus>());

        builder.Property(j => j.ExitCode);
        builder.Property(j => j.Pid);

        builder.Property(j => j.OutputPath)
            .HasMaxLength(512);

        builder.Property(j => j.Error)
            .HasMaxLength(1024);

        builder.Property(j => j.CreatedAt);
        builder.Property(j => j.StartedAt);
        builder.Property(j => j.FinishedAt);

        // Jobs strip (active per conversation) + boot-sweep scan (non-terminal).
        builder.HasIndex(j => new { j.ConversationId, j.CreatedAt });
        builder.HasIndex(j => j.Status);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(j => j.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>SPEC-20261005-chat-jobs-schedule-search RF-004 — due-scan index on active rows.</summary>
public sealed class ChatScheduleConfiguration : IEntityTypeConfiguration<ChatSchedule>
{
    public void Configure(EntityTypeBuilder<ChatSchedule> builder)
    {
        builder.ToTable("ChatSchedules");

        builder.Property(s => s.Id)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatScheduleId>());

        builder.HasKey(s => s.Id);

        builder.Property(s => s.ConversationId)
            .HasMaxLength(128)
            .HasConversion(new StringIdValueConverter<ChatConversationId>());

        builder.Property(s => s.Kind)
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.CronExpression)
            .HasMaxLength(ChatSchedule.MaxCronLength);

        builder.Property(s => s.TimeZoneId)
            .HasMaxLength(ChatSchedule.MaxTimeZoneLength);

        builder.Property(s => s.NextFireAtUtc);

        builder.Property(s => s.Title)
            .HasMaxLength(ChatSchedule.MaxTitleLength)
            .IsRequired();

        builder.Property(s => s.Prompt)
            .HasMaxLength(ChatSchedule.MaxPromptLength)
            .IsRequired();

        builder.Property(s => s.Active);
        builder.Property(s => s.LastDeliveredAt);
        builder.Property(s => s.CreatedAt);

        // Due scan: active rows ordered by next fire + per-conversation listing.
        builder.HasIndex(s => new { s.Active, s.NextFireAtUtc });
        builder.HasIndex(s => s.ConversationId);

        builder.HasOne<ChatConversation>()
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
