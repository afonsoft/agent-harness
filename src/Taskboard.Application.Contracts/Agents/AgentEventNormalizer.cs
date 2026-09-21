namespace Taskboard.Agents;

/// <summary>
/// Converte mensagens brutas dos transports (one-shot stdout/stderr,
/// JSON-RPC oportunista) em <see cref="AgentExecutionEvent"/> normalizados.
/// Sem estado — sequência/redação ficam no sink.
/// </summary>
public static class AgentEventNormalizer
{
    /// <summary>
    /// Normaliza uma <see cref="AgentLogMessage"/>: quando o transporte já
    /// preencheu <see cref="AgentLogMessage.Kind"/> (ex.: notificação JSON-RPC
    /// parseada), preserva; caso contrário emite <c>output</c>.
    /// </summary>
    public static AgentExecutionEvent FromLogMessage(
        AgentLogMessage message,
        string scopeKind,
        string scopeId,
        string? stageId = null,
        string? sessionId = null)
    {
        var stream = message.Stream switch
        {
            AgentLogStream.StdOut => "stdout",
            AgentLogStream.StdErr => "stderr",
            _ => "system"
        };

        return new AgentExecutionEvent(
            EventId: string.Empty, // sink atribui
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            Sequence: 0,
            TimestampUtc: message.Timestamp,
            Kind: message.Kind ?? AgentEventKinds.Output,
            StageId: stageId,
            SessionId: sessionId,
            Title: null,
            PayloadJson: message.PayloadJson,
            RawJson: null,
            Stream: stream);
    }
}
