using System;
using Shouldly;
using Taskboard.Domain.Entities.Chat;
using Taskboard.ValueObjects;
using Xunit;

namespace Taskboard.Tests.Unit.Domain.Entities;

/// <summary>SPEC-20261005 RF-009: invariantes da subscription de Web Push.</summary>
public sealed class ChatPushSubscriptionTests
{
    [Fact]
    public void Dado_DadosValidos_Quando_Create_Entao_PropriedadesTrimadasETimestamps()
    {
        var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var sub = ChatPushSubscription.Create(
            ChatPushSubscriptionId.NewGuid(),
            "  https://push.example/sub/1  ", "  p256  ", "  auth  ",
            userAgent: "ua", now: now);

        sub.Endpoint.ShouldBe("https://push.example/sub/1");
        sub.P256dh.ShouldBe("p256");
        sub.Auth.ShouldBe("auth");
        sub.UserAgent.ShouldBe("ua");
        sub.CreatedAt.ShouldBe(now);
        sub.UpdatedAt.ShouldBe(now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Dado_EndpointVazio_Quando_Create_Entao_LancaDomainException(string endpoint)
    {
        Should.Throw<DomainException>(() => ChatPushSubscription.Create(
            ChatPushSubscriptionId.NewGuid(), endpoint, "p", "a"));
    }

    [Theory]
    [InlineData("", "a")]
    [InlineData("p", "")]
    public void Dado_KeyVazia_Quando_Create_Entao_LancaDomainException(string p256dh, string auth)
    {
        Should.Throw<DomainException>(() => ChatPushSubscription.Create(
            ChatPushSubscriptionId.NewGuid(), "https://push.example/x", p256dh, auth));
    }

    [Fact]
    public void Dado_SubscriptionExistente_Quando_RotateKeys_Entao_AtualizaKeysETimestamp()
    {
        var created = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var rotated = created.AddMinutes(5);
        var sub = ChatPushSubscription.Create(
            ChatPushSubscriptionId.NewGuid(), "https://push.example/sub/1",
            "old-p", "old-a", now: created);

        sub.RotateKeys("new-p", "new-a", "ua2", rotated);

        sub.P256dh.ShouldBe("new-p");
        sub.Auth.ShouldBe("new-a");
        sub.UserAgent.ShouldBe("ua2");
        sub.CreatedAt.ShouldBe(created);
        sub.UpdatedAt.ShouldBe(rotated);
    }

    [Fact]
    public void Dado_RotateComKeyVazia_Quando_RotateKeys_Entao_LancaDomainException()
    {
        var sub = ChatPushSubscription.Create(
            ChatPushSubscriptionId.NewGuid(), "https://push.example/sub/1", "p", "a");

        Should.Throw<DomainException>(() => sub.RotateKeys("", "a", null));
        Should.Throw<DomainException>(() => sub.RotateKeys("p", null!, null));
    }
}
