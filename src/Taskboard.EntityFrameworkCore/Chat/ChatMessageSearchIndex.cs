using System.Data;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Taskboard.Application.Contracts.Chat;
using Taskboard.EntityFrameworkCore.Data;

namespace Taskboard.EntityFrameworkCore.Chat;

/// <summary>
/// FTS5 index over chat messages (SPEC-20261005-chat-jobs-schedule-search
/// RF-007/RF-008). The <c>ChatMessageFts</c> virtual table is created by the
/// EF migration (and lazily ensured here so <c>EnsureCreated</c>-built test
/// databases get it too). Snippets come from sqlite's <c>snippet()</c> with
/// <c>&lt;mark&gt;</c> delimiters; ranking is <c>bm25()</c> ascending.
/// </summary>
public sealed class ChatMessageSearchIndex : IChatMessageSearchIndex
{
    private const string EnsureSql =
        "CREATE VIRTUAL TABLE IF NOT EXISTS ChatMessageFts " +
        "USING fts5(content, conversationId UNINDEXED, messageId UNINDEXED);";

    private readonly TaskboardDbContext _context;
    private bool _ensured;

    public ChatMessageSearchIndex(TaskboardDbContext context)
    {
        _context = context;
    }

    public async Task IndexAsync(
        string messageId, string conversationId, string content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        await EnsureAsync(cancellationToken);
        await using var command = CreateCommand(
            "DELETE FROM ChatMessageFts WHERE messageId = $id;" +
            "INSERT INTO ChatMessageFts(content, conversationId, messageId) " +
            "VALUES($content, $conv, $id);");
        AddParameter(command, "$id", messageId);
        AddParameter(command, "$conv", conversationId);
        AddParameter(command, "$content", content);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSearchHitDto>> SearchAsync(
        string query, string? conversationId, int limit,
        CancellationToken cancellationToken = default)
    {
        var matchQuery = BuildMatchQuery(query);
        if (matchQuery is null || limit <= 0)
        {
            return [];
        }

        await EnsureAsync(cancellationToken);

        var sql = new StringBuilder(
            "SELECT fts.conversationId, c.Title, fts.messageId, " +
            "snippet(ChatMessageFts, 0, '<mark>', '</mark>', '…', 32) AS snippet, " +
            "m.CreatedAt, bm25(ChatMessageFts) AS rank " +
            "FROM ChatMessageFts fts " +
            "JOIN ChatMessages m ON m.Id = fts.messageId " +
            "JOIN ChatConversations c ON c.Id = fts.conversationId " +
            "WHERE ChatMessageFts MATCH $q");
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            sql.Append(" AND fts.conversationId = $conv");
        }

        sql.Append(" ORDER BY rank LIMIT $lim");

        await using var command = CreateCommand(sql.ToString());
        AddParameter(command, "$q", matchQuery);
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            AddParameter(command, "$conv", conversationId);
        }

        AddParameter(command, "$lim", Math.Min(limit, 50));

        var hits = new List<ChatSearchHitDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new ChatSearchHitDto(
                ConversationId: reader.GetString(0),
                ConversationTitle: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                MessageId: reader.GetString(2),
                Snippet: reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                CreatedAt: DateTime.Parse(
                    reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                Rank: reader.GetDouble(5)));
        }

        return hits;
    }

    public async Task RemoveConversationAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        await EnsureAsync(cancellationToken);
        await using var command = CreateCommand(
            "DELETE FROM ChatMessageFts WHERE conversationId = $conv;");
        AddParameter(command, "$conv", conversationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Builds an FTS5 MATCH string from free text — every whitespace token is
    /// quoted into a phrase and space-joined (implicit AND), so user input can
    /// never inject match operators.
    /// </summary>
    internal static string? BuildMatchQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append('"').Append(token.Replace("\"", "\"\"")).Append('"');
        }

        return builder.ToString();
    }

    private async Task EnsureAsync(CancellationToken cancellationToken)
    {
        if (_ensured)
        {
            return;
        }

        await _context.Database.ExecuteSqlRawAsync(EnsureSql, cancellationToken);
        _ensured = true;
    }

    private System.Data.Common.DbCommand CreateCommand(string commandText)
    {
        var connection = _context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }

        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        return command;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
