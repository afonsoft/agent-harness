using Shouldly;
using Xunit;

namespace Taskboard.Tests.Unit.Blazor;

/// <summary>
/// SPEC-20261008 (sonarqube-autofix): guards de fonte para os fixes S5332,
/// S5693 e S2583 em componentes .razor — o bUnit não observa o conteúdo
/// imperativo de <c>@code</c>, então a asserção atua sobre a fonte.
/// </summary>
public class ChatRazorSonarGuardTests
{
    private static string RepoPath(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Join(dir.FullName, "Taskboard.sln")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("não foi possível localizar a raiz do repo (Taskboard.sln)");
        return Path.Join([dir.FullName, .. parts]);
    }

    [Fact]
    public void Dado_ChatPreviewTab_Quando_LeFonte_Entao_SemLiteralHttp()
    {
        // S5332: nenhum literal "http:// — o scheme vem de Uri.UriSchemeHttp.
        var source = File.ReadAllText(RepoPath(
            "src", "Taskboard.Blazor", "Components", "AiChat", "ChatPreviewTab.razor"));
        source.ShouldNotContain("\"http://");
        source.ShouldContain("Uri.UriSchemeHttp");
    }

    [Fact]
    public void Dado_ProviderChat_Quando_LeFonte_Entao_CapUploadAgregado8MbComErroPorArquivo()
    {
        // S5693: o analyzer multiplica GetMultipleFiles(N) x maxAllowedSize —
        // 4 arquivos x 2MB = agregado de 8MB (= fileUploadSizeLimit do profile).
        var source = File.ReadAllText(RepoPath(
            "src", "Taskboard.Blazor", "Components", "Chat", "ProviderChat.razor"));
        source.ShouldContain("AttachmentMaxFiles = 4");
        source.ShouldContain("AttachmentMaxBytes = 2L * 1024 * 1024");
        source.ShouldContain("args.GetMultipleFiles(AttachmentMaxFiles)");
        source.ShouldContain("file.OpenReadStream(maxAllowedSize: AttachmentMaxBytes)");
        source.ShouldContain("or IOException");
        source.ShouldNotContain("32L * 1024 * 1024");
    }

    [Fact]
    public void Dado_ChatGitBar_Quando_LeFonte_Entao_SemTernarioMortoNoStatus()
    {
        // S2583: o braço `ConversationId is null ? null` era inalcançável —
        // o early return acima já sai quando ConversationId é null.
        var source = File.ReadAllText(RepoPath(
            "src", "Taskboard.Blazor", "Components", "Chat", "ChatGitBar.razor"));
        source.ShouldNotContain("_status = ConversationId is null ?");
        source.ShouldContain("_status = await Client.GetChatGitStatusAsync(ConversationId)");
    }
}
