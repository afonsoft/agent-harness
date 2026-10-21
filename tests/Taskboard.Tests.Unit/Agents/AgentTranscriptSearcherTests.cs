using Shouldly;
using Taskboard.Integrations.Agents;
using Xunit;

namespace Taskboard.Tests.Unit.Agents;

/// <summary>
/// SPEC-20261020-session-history-parity RF-002/NFR: bounded transcript
/// search — $HOME confinement, ≤160c snippets, match cap, case-insensitive
/// literal needle. Exercises the in-process fallback (rgPath: null).
/// </summary>
public class AgentTranscriptSearcherTests : IDisposable
{
    private readonly string _root = Path.Join(
        Path.GetTempPath(), $"ts-search-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private AgentTranscriptSearcher NewSearcher() =>
        new(home: _root, rgPath: null);

    private string Write(string name, string content)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Join(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task Dado_Match_Quando_Search_Entao_SnippetDaLinha()
    {
        var path = Write("a.jsonl", "line one\nsecond line with needle here\nthird");

        var hits = await NewSearcher().SearchAsync([path], "needle", 10);

        hits[path].ShouldBe("second line with needle here");
    }

    [Fact]
    public async Task Dado_CaseDiferente_Quando_Search_Entao_CaseInsensitive()
    {
        var path = Write("a.jsonl", "NEEDLE in caps");

        var hits = await NewSearcher().SearchAsync([path], "needle", 10);

        hits.ShouldContainKey(path);
    }

    [Fact]
    public async Task Dado_LinhaLonga_Quando_Search_Entao_SnippetMax160()
    {
        var path = Write("a.jsonl", new string('x', 300) + " needle " + new string('y', 100));

        var hits = await NewSearcher().SearchAsync([path], "needle", 10);

        hits[path].Length.ShouldBeLessThanOrEqualTo(160);
    }

    [Fact]
    public async Task Dado_PathForaDoHome_Quando_Search_Entao_Ignorado()
    {
        var outside = Path.Join(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "needle outside home");
        try
        {
            var hits = await NewSearcher().SearchAsync([outside], "needle", 10);

            hits.ShouldBeEmpty();
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task Dado_MaisMatchesQueCap_Quando_Search_Entao_CapHonrado()
    {
        var paths = Enumerable.Range(0, 5)
            .Select(i => Write($"f{i}.txt", $"needle {i}"))
            .ToList();

        var hits = await NewSearcher().SearchAsync(paths, "needle", maxMatches: 2);

        hits.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Dado_SemMatch_Quando_Search_Entao_Vazio()
    {
        var path = Write("a.txt", "nothing here");

        var hits = await NewSearcher().SearchAsync([path], "needle", 10);

        hits.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dado_ArquivoInexistente_Quando_Search_Entao_Ignorado()
    {
        var missing = Path.Join(_root, "nope.txt");

        var hits = await NewSearcher().SearchAsync([missing], "needle", 10);

        hits.ShouldBeEmpty();
    }
}
