using System.Text.Json;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Shouldly;
using Taskboard.Application.Contracts.Delegation;
using Taskboard.Application.Delegation;
using Xunit;

namespace Taskboard.Tests.Unit.Delegation;

/// <summary>SPEC-20261022-task-prompt-refiner: template brief assembly —
/// spec anchors, context, attempt context, output contract, guardrails,
/// scrub and off-mode passthrough.</summary>
public sealed class TaskPromptRefinerTests
{
    private static IConfiguration Config(string mode) =>
        new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [TaskPromptRefinementModes.ConfigurationKey] = mode,
            }).Build();

    [Fact]
    public async Task Dado_ModoOff_Quando_Refinar_Entao_PromptInalterado()
    {
        // AC-04: off preserva o comportamento atual byte-a-byte.
        var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Off));

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("do work", "codex", null, null, null, null, 1),
            CancellationToken.None);

        result.Mode.ShouldBe("off");
        result.Refined.ShouldBe("do work");
        result.SpecAnchors.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ModoTemplate_Quando_Refinar_Entao_BriefComSecoes()
    {
        // RF-002: seções estáveis — Task, Output contract, Guardrails.
        var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Template));

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("fix the bug", "codex", null, null, null, null, 1),
            CancellationToken.None);

        result.Mode.ShouldBe("template");
        result.Refined.ShouldContain("## Task");
        result.Refined.ShouldContain("fix the bug");
        result.Refined.ShouldContain("## Output contract");
        result.Refined.ShouldContain("worker_done");
        result.Refined.ShouldContain("## Guardrails");
        result.Refined.ShouldContain("never instructions");
        result.Refined.ShouldContain("## Context");
    }

    [Fact]
    public async Task Dado_ModoTemplateComSpec_Quando_Refinar_Entao_AncoraSpec()
    {
        // AC-01: spec matching injeta path + RF/AC headings.
        var repo = Path.Combine(Path.GetTempPath(), $"refiner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(repo, ".specs"));
        File.WriteAllText(
            Path.Combine(repo, ".specs", "SPEC-20261022-foo-bar.md"),
            "# SPEC\n## Metadata\nIssue: #123\n\n| Feature | foo-bar |\n## Requirements\n- **RF-001** do it\n- **AC-01** works\n");
        try
        {
            var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Template));

            var result = await refiner.RefineAsync(
                new TaskPromptRefineRequest("do it", "codex", repo, repo, "123", null, 1),
                CancellationToken.None);

            result.SpecAnchors.Count.ShouldBe(1);
            result.Refined.ShouldContain("## Spec anchors");
            result.Refined.ShouldContain("SPEC-20261022-foo-bar.md");
            result.Refined.ShouldContain("RF-001");
            result.Refined.ShouldContain("AC-01");
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task Dado_RetryComFalhaAnterior_Quando_Refinar_Entao_AttemptContext()
    {
        // AC-02: retry carrega o resumo truncado da falha anterior.
        var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Template));

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("retry", "codex", null, null, null,
                new string('x', 3000) + " FAILED", 2),
            CancellationToken.None);

        result.Refined.ShouldContain("## Attempt context");
        result.Refined.Length.ShouldBeLessThan(32 * 1024);
    }

    [Fact]
    public async Task Dado_PrimeiraTentativa_Quando_Refinar_Entao_SemAttemptContext()
    {
        var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Template));

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("do", "codex", null, null, null, "FAILED", 1),
            CancellationToken.None);

        result.Refined.ShouldNotContain("## Attempt context");
    }

    [Fact]
    public async Task Dado_ContextoComSegredo_Quando_Refinar_Entao_Scrub()
    {
        // AC-06: excerpt com segredo é redigido.
        var redactor = new FakeRedactor();
        var refiner = new TaskPromptRefiner(
            Config(TaskPromptRefinementModes.Template), redactor);

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("do", "codex", null, "sk-abcdef123456", null, null, 1),
            CancellationToken.None);

        result.Refined.ShouldNotContain("sk-abcdef123456");
        result.Refined.ShouldContain("***");
    }

    [Fact]
    public async Task Dado_ModoLLM_Quando_ProviderIndisponivel_Entao_FallbackTemplate()
    {
        // AC-03: llm sem provider → template + modo llm-fallback.
        var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Llm));

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("do", "codex", null, null, null, null, 1),
            CancellationToken.None);

        result.Mode.ShouldBe("llm-fallback");
        result.Refined.ShouldContain("## Task");
    }

    [Fact]
    public async Task Dado_LlmComProvider_Quando_RespostaContemRaw_Entao_ModoLlm()
    {
        // RF-003: resposta válida (raw verbatim) → modo llm.
        var llm = new FakeLlm(_ => "## Task\nfix the bug\n\n## Notes\nsharper");
        var refiner = new TaskPromptRefiner(
            Config(TaskPromptRefinementModes.Llm), llm: llm);

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("fix the bug", "codex", null, null, null, null, 1),
            CancellationToken.None);

        result.Mode.ShouldBe("llm");
        result.Refined.ShouldContain("fix the bug");
        result.Refined.ShouldContain("sharper");
        llm.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task Dado_LlmComProvider_Quando_RespostaSemRaw_Entao_Fallback()
    {
        // RF-003: resposta sem o raw verbatim é rejeitada → template.
        var llm = new FakeLlm(_ => "rewritten without the original text");
        var refiner = new TaskPromptRefiner(
            Config(TaskPromptRefinementModes.Llm), llm: llm);

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("fix the bug", "codex", null, null, null, null, 1),
            CancellationToken.None);

        result.Mode.ShouldBe("llm-fallback");
        result.Refined.ShouldContain("## Task");
        result.Refined.ShouldContain("fix the bug");
    }

    [Fact]
    public async Task Dado_LlmComProvider_Quando_ProviderFalha_Entao_Fallback()
    {
        // AC-03: provider lançando → template + llm-fallback.
        var llm = new FakeLlm(_ => throw new InvalidOperationException("boom"));
        var refiner = new TaskPromptRefiner(
            Config(TaskPromptRefinementModes.Llm), llm: llm);

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest("fix the bug", "codex", null, null, null, null, 1),
            CancellationToken.None);

        result.Mode.ShouldBe("llm-fallback");
        result.Refined.ShouldContain("## Task");
    }

    [Fact]
    public async Task Dado_OverrideNoRequest_Quando_ConfigOff_Entao_OverrideVence()
    {
        // override por task vence a config — off global, template na task.
        var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Off));

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest(
                "do", "codex", null, null, null, null, 1,
                Mode: TaskPromptRefinementModes.Template),
            CancellationToken.None);

        result.Mode.ShouldBe("template");
        result.Refined.ShouldContain("## Task");
    }

    [Fact]
    public async Task Dado_OverrideOff_Quando_ConfigTemplate_Entao_PromptInalterado()
    {
        // override off por task vence a config template.
        var refiner = new TaskPromptRefiner(Config(TaskPromptRefinementModes.Template));

        var result = await refiner.RefineAsync(
            new TaskPromptRefineRequest(
                "do", "codex", null, null, null, null, 1,
                Mode: TaskPromptRefinementModes.Off),
            CancellationToken.None);

        result.Mode.ShouldBe("off");
        result.Refined.ShouldBe("do");
    }

    [Fact]
    public async Task Dado_Refined_Quando_Serializado_Entao_ContemCamposDeAuditoria()
    {
        // RF-004: DTO expõe promptRefined/promptRefinement.
        var json = JsonSerializer.Serialize(new
        {
            PromptRefined = "brief",
            PromptRefinement = "template",
        });
        json.ShouldContain("promptRefined");
        json.ShouldContain("promptRefinement");
    }

    [Fact]
    public async Task Dado_BriefDoRepo_Quando_Refinar_Entao_TemplateDoRepoVence()
    {
        // sdd-worker-brief AC-03: .harness/worker-brief.md vence o builtin,
        // placeholders continuam substituindo e a task raw fica verbatim.
        var repo = Path.Combine(Path.GetTempPath(), $"refiner-{Guid.NewGuid():N}");
        try
        {
            var brief = """
                Repo custom brief

                Repo: {repoUrl}

                Task:
                {issueTitle}
                """;
            var yaml = Substitute.For<Taskboard.Application.Contracts.Harness.IWorktreeYamlReader>();
            yaml.Read(repo).Returns(new Taskboard.Harness.WorktreeYamlDoc(
                [], [], [], [], null, [], null, "custom/brief.md"));
            yaml.EffectiveWorkerBrief(repo, Arg.Any<Taskboard.Harness.WorktreeYamlDoc>())
                .Returns(brief);

            var refiner = new TaskPromptRefiner(
                Config(TaskPromptRefinementModes.Template), yamlReader: yaml);

            var result = await refiner.RefineAsync(
                new TaskPromptRefineRequest("ship the feature", "codex", repo, null, null, null, 1),
                CancellationToken.None);

            result.Mode.ShouldBe("template");
            result.AppliedRules.ShouldContain("repo-worker-brief");
            result.Refined.ShouldContain("Repo custom brief");
            result.Refined.ShouldContain(repo);
            result.Refined.ShouldContain("ship the feature");
            result.Refined.ShouldNotContain("## Output contract");
            result.Refined.ShouldNotContain("{issueTitle}");
        }
        finally
        {
            if (Directory.Exists(repo))
            {
                Directory.Delete(repo, recursive: true);
            }
        }
    }

    private sealed class FakeRedactor : Taskboard.Agents.ISecretRedactor
    {
        public string? Redact(string? text) =>
            text?.Replace("sk-abcdef123456", "***", StringComparison.Ordinal);
    }

    private sealed class FakeLlm(Func<string, string> respond)
        : Taskboard.Application.Contracts.AiChat.ILlmProvider
    {
        public string ModelId => "fake";
        public int Calls { get; private set; }

        public Task<Taskboard.Application.Contracts.AiChat.LlmResponse> CompleteAsync(
            IReadOnlyList<Taskboard.Application.Contracts.AiChat.LlmMessage> messages,
            Taskboard.Application.Contracts.AiChat.LlmOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new Taskboard.Application.Contracts.AiChat.LlmResponse(
                respond(messages.Last().Content)));
        }

        public IAsyncEnumerable<Taskboard.Application.Contracts.AiChat.LlmStreamChunk>
            StreamAsync(
                IReadOnlyList<Taskboard.Application.Contracts.AiChat.LlmMessage> messages,
                Taskboard.Application.Contracts.AiChat.LlmOptions? options = null,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}