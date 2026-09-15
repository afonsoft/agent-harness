using Taskboard.Application.Contracts.Skills;

namespace Taskboard.Blazor.Components.Shared;

/// <summary>Nó da árvore de arquivos de uma skill (diretório ou arquivo).</summary>
public sealed record SkillFileNode(
    string Name,
    bool IsDirectory,
    IReadOnlyList<SkillFileNode> Children,
    SkillFileDto? File)
{
    /// <summary>Monta a árvore a partir da lista plana de arquivos (paths com <c>/</c>).</summary>
    public static IReadOnlyList<SkillFileNode> Build(IReadOnlyList<SkillFileDto> files)
    {
        var root = new MutableNode();
        foreach (var file in files)
        {
            var segments = file.RelativePath.Split('/');
            var current = root;
            for (var i = 0; i < segments.Length - 1; i++)
            {
                current = current.GetOrAddDirectory(segments[i]);
            }

            current.Files.Add(file);
        }

        return root.ToNodes();
    }

    private sealed class MutableNode
    {
        private readonly Dictionary<string, MutableNode> _directories = new(StringComparer.Ordinal);
        public List<SkillFileDto> Files { get; } = [];

        public MutableNode GetOrAddDirectory(string name)
        {
            if (!_directories.TryGetValue(name, out var node))
            {
                node = new MutableNode();
                _directories[name] = node;
            }

            return node;
        }

        public IReadOnlyList<SkillFileNode> ToNodes()
        {
            var nodes = new List<SkillFileNode>();
            foreach (var (name, dir) in _directories.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                nodes.Add(new SkillFileNode(name, true, dir.ToNodes(), null));
            }

            foreach (var file in Files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
            {
                var name = file.RelativePath[(file.RelativePath.LastIndexOf('/') + 1)..];
                nodes.Add(new SkillFileNode(name, false, [], file));
            }

            return nodes;
        }
    }
}
