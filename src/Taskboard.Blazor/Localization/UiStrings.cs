namespace Taskboard.Blazor.Localization;

/// <summary>
/// SPEC-20261008-locale-picker RF-001: UI string table for the Blazor shell.
/// pt-BR is the product language (SPEC-20261003-i18n-consistency); every key
/// exists in all three dictionaries and lookup falls back
/// selected → pt-BR → en → the key itself, so a missing translation can
/// never render blank.
/// </summary>
public static class UiStrings
{
    public const string DefaultCulture = "pt-BR";

    public static readonly IReadOnlyList<string> SupportedCultures =
        ["pt-BR", "en", "es"];

    private static readonly IReadOnlyDictionary<string, string> PtBr =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // ---- layout / topbar ----
            ["layout.skipToMain"] = "Pular para o conteúdo principal",
            ["layout.expandSidebar"] = "Expandir barra lateral",
            ["layout.collapseSidebar"] = "Recolher barra lateral",
            ["layout.close"] = "Fechar",
            ["layout.openNav"] = "Abrir navegação",
            ["layout.error"] = "Ocorreu um erro inesperado. Atualize a página.",
            ["topbar.settings"] = "Configurações",
            ["topbar.logout"] = "Sair",
            ["topbar.language"] = "Idioma",

            // ---- nav ----
            ["nav.sidebarNav"] = "Navegação lateral",
            ["nav.repository"] = "Repositório",
            ["nav.selectRepo"] = "Selecionar repositório",
            ["nav.repoShort"] = "Repo",
            ["nav.repoInvalid"] = "Repositório '{0}' inválido. Use o formato owner/repo.",
            ["nav.board"] = "Quadro",
            ["nav.aicode"] = "AI Code",
            ["nav.terminal"] = "Terminal",
            ["nav.gantt"] = "Gantt",
            ["nav.workflow"] = "Fluxo",
            ["nav.specs"] = "Specs",
            ["nav.editor"] = "VS Code",
            ["nav.cockpit"] = "Cockpit",
            ["nav.agents"] = "Agentes CLI",
            ["nav.finops"] = "FinOps",
            ["nav.settings"] = "Configurações",
            ["nav.skills"] = "Skills",
            ["nav.jobs"] = "Jobs",
            ["nav.prompts"] = "Prompts",
            ["nav.issues"] = "Issues",

            // ---- login ----
            ["login.pageTitle"] = "Entrar - Harness",
            ["login.subtitle"] = "Entre para continuar",
            ["login.username"] = "Usuário",
            ["login.password"] = "Senha",
            ["login.signin"] = "Entrar",

            // ---- shared ----
            ["common.loading"] = "Carregando…",
            ["common.noItems"] = "Nenhum item",
            ["common.cancel"] = "Cancelar",
            ["common.save"] = "Salvar",
            ["common.close"] = "Fechar",
            ["common.back"] = "Voltar",
            ["common.write"] = "Escrever",
            ["common.preview"] = "Visualizar",
            ["common.current"] = "atual",

            // ---- board (GitHub components) ----
            ["board.loadingIssues"] = "Carregando issues…",
            ["board.openInCockpit"] = "Abrir no Cockpit",
            ["board.filterPlaceholder"] = "Filtrar por título, descrição ou label…",
            ["board.filterAria"] = "Filtrar issues",
            ["board.filterPriorityAria"] = "Filtrar por prioridade",
            ["board.allPriorities"] = "Todas as prioridades",
            ["board.issueActions"] = "Ações da issue #{0}",
            ["board.moveTo"] = "Mover para…",
            ["board.priorityHeader"] = "Prioridade",
            ["board.priorityTitle"] = "Prioridade: {0}",
            ["board.dragAria"] = "{0}, arraste para mover",
            ["board.agoNow"] = "agora",
            ["board.agoMin"] = "há {0}min",
            ["board.agoHours"] = "há {0}h",
            ["board.agoDays"] = "há {0}d",
            ["board.runStarted"] = "Run iniciado para a issue #{0} — acompanhe pelo Cockpit.",
            ["board.issueClosed"] = "Issue #{0} fechada ({1}).",
            ["board.closeError"] = "Erro ao fechar issue: {0}",
            ["board.priorityError"] = "Erro ao atualizar prioridade: {0}",
            ["board.moved"] = "Issue #{0} movida para {1}.",
            ["board.movedWithAgent"] = "Issue #{0} movida para {1} e agente {2} iniciado — abra no Cockpit.",
            ["board.moveError"] = "Erro ao mover issue: {0}",
            ["board.loadError"] = "Erro ao carregar issues: {0}",
            ["newtask.dialogTitle"] = "Nova Tarefa",

            ["move.title"] = "Mover issue #{0} para…",
            ["move.aria"] = "Mover issue #{0}",
            ["move.closeIssue"] = "Fechar issue",
            ["move.archive"] = "Arquivar",

            ["newtask.title"] = "Título",
            ["newtask.description"] = "Descrição (markdown)",
            ["newtask.descAria"] = "Descrição da tarefa em markdown",
            ["newtask.descPlaceholder"] = "## Contexto\n\n- item\n\n```bash\ncomando\n```",
            ["newtask.column"] = "Coluna Inicial",
            ["newtask.nothingToPreview"] = "Nada para visualizar.",
            ["newtask.created"] = "Issue criada com sucesso.",
            ["newtask.createError"] = "Erro ao criar issue: {0}",

            ["agentselect.title"] = "Selecionar Agente CLI",
            ["agentselect.noAgents1"] = "Nenhum agente CLI habilitado e autenticado. Ative um agente em",
            ["agentselect.noAgents2"] = "ou faça login pelo",
            ["agentselect.agent"] = "Agente",
            ["agentselect.branch"] = "Branch de trabalho",
            ["agentselect.scope"] = "Escopo",
            ["agentselect.instructions"] = "Instruções",
            ["agentselect.instructionsRequired"] = "As instruções são obrigatórias.",
            ["agentselect.detecting"] = "Detectando agentes…",
            ["agentselect.start"] = "Iniciar Agente",
            ["agentselect.detectError"] = "Erro ao detectar agentes: {0}",

            ["detail.repo"] = "Repositório:",
            ["detail.tabHistory"] = "Histórico",
            ["detail.tabComments"] = "Comentários",

            ["tasklog.title"] = "Execução do Agente",
            ["tasklog.noActiveRun"] = "Nenhuma execução ativa para cancelar.",
            ["tasklog.permissionExpired"] = "Permissão expirada ou já respondida.",
            ["tasklog.permissionError"] = "Erro ao responder permissão: {0}",

            ["chat.permissionRequest"] = "solicitação de permissão",
            ["chat.agentQuestion"] = "pergunta do agente",

            ["comments.loading"] = "Carregando comentários…",
            ["comments.none"] = "Nenhum comentário nesta issue.",
            ["comments.new"] = "Novo comentário",
            ["comments.publishedSuffix"] = "(publicado no GitHub)",
            ["comments.placeholder"] = "Deixe contexto para o próximo agente ou etapa…",
            ["comments.loadError"] = "Não foi possível carregar os comentários: {0}",
            ["comments.publishedToast"] = "Comentário publicado no GitHub.",

            ["agentconfig.repo"] = "Repositório",
            ["agentconfig.promptPlaceholder"] = "Instruções para o agente executar nesta issue…",
            ["agentconfig.promptRequired"] = "O prompt é obrigatório.",
            ["agentconfig.model"] = "Modelo",
            ["agentconfig.managedByCli"] = "Gerenciado pela CLI",
            ["agentconfig.cliPicksModel"] = "A CLI escolhe o modelo",
            ["agentconfig.command"] = "Comando",
            ["agentconfig.agentStarted"] = "Agente {0} iniciado para a issue #{1} — abra no Cockpit.",
            ["agentconfig.execError"] = "Erro ao executar agente: {0}",

            ["detail.tabDetails"] = "Detalhes",
            ["detail.tabAgentConfig"] = "Agent Config",
            ["detail.tabLogs"] = "Logs do Agente",
            ["detail.column"] = "Coluna:",
            ["detail.agent"] = "Agente:",
            ["detail.noAgentRan"] = "Nenhum agente executou nesta issue",
            ["detail.started"] = "iniciado",
            ["detail.finished"] = "finalizado",
            ["detail.openGithub"] = "Abrir no GitHub",
            ["detail.updated"] = "Issue #{0} atualizada.",
            ["detail.saveError"] = "Erro ao salvar issue: {0}",

            ["tasklog.clear"] = "Limpar",
            ["tasklog.clearError"] = "Erro ao limpar logs: {0}",
            ["tasklog.cancelError"] = "Erro ao cancelar: {0}",

            ["comments.post"] = "Comentar",
            ["comments.postError"] = "Falha ao comentar: {0}",
            ["comments.viewRendered"] = "Renderizado",
            ["comments.viewSource"] = "Fonte",
            ["comments.viewAria"] = "Visualização do comentário",

            ["common.edit"] = "Editar",
            ["common.run"] = "Executar",

            // SPEC-20261009: delegation wave P5.
            ["agents.compare.useLeg"] = "Usar esta perna",
            ["agents.compare.useLegPr"] = "Usar e abrir PR",
            ["agents.compare.openPr"] = "Abrir PR",
            ["agents.activity.title"] = "Atividade",
            ["taskdetail.delegate"] = "Delegar para worktree",
        };

    private static readonly IReadOnlyDictionary<string, string> En =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["layout.skipToMain"] = "Skip to main content",
            ["layout.expandSidebar"] = "Expand sidebar",
            ["layout.collapseSidebar"] = "Collapse sidebar",
            ["layout.close"] = "Close",
            ["layout.openNav"] = "Open navigation",
            ["layout.error"] = "An unexpected error occurred. Please refresh the page.",
            ["topbar.settings"] = "Settings",
            ["topbar.logout"] = "Log out",
            ["topbar.language"] = "Language",

            ["nav.sidebarNav"] = "Sidebar navigation",
            ["nav.repository"] = "Repository",
            ["nav.selectRepo"] = "Select repository",
            ["nav.repoShort"] = "Repo",
            ["nav.repoInvalid"] = "Invalid repository '{0}'. Use the format owner/repo.",
            ["nav.board"] = "Board",
            ["nav.aicode"] = "AI Code",
            ["nav.terminal"] = "Terminal",
            ["nav.gantt"] = "Gantt",
            ["nav.workflow"] = "Workflow",
            ["nav.specs"] = "Specs",
            ["nav.editor"] = "VS Code",
            ["nav.cockpit"] = "Cockpit",
            ["nav.agents"] = "CLI Agents",
            ["nav.finops"] = "FinOps",
            ["nav.settings"] = "Settings",
            ["nav.skills"] = "Skills",
            ["nav.jobs"] = "Jobs",
            ["nav.prompts"] = "Prompts",
            ["nav.issues"] = "Issues",

            ["login.pageTitle"] = "Login - Harness",
            ["login.subtitle"] = "Sign in to continue",
            ["login.username"] = "Username",
            ["login.password"] = "Password",
            ["login.signin"] = "Sign in",

            ["common.loading"] = "Loading…",
            ["common.noItems"] = "No items",
            ["common.cancel"] = "Cancel",
            ["common.save"] = "Save",
            ["common.close"] = "Close",
            ["common.back"] = "Back",
            ["common.write"] = "Write",
            ["common.preview"] = "Preview",
            ["common.current"] = "current",

            ["board.loadingIssues"] = "Loading issues…",
            ["board.openInCockpit"] = "Open in Cockpit",
            ["board.filterPlaceholder"] = "Filter by title, description or label…",
            ["board.filterAria"] = "Filter issues",
            ["board.filterPriorityAria"] = "Filter by priority",
            ["board.allPriorities"] = "All priorities",
            ["board.issueActions"] = "Actions for issue #{0}",
            ["board.moveTo"] = "Move to…",
            ["board.priorityHeader"] = "Priority",
            ["board.priorityTitle"] = "Priority: {0}",
            ["board.dragAria"] = "{0}, drag to move",
            ["board.agoNow"] = "now",
            ["board.agoMin"] = "{0}min ago",
            ["board.agoHours"] = "{0}h ago",
            ["board.agoDays"] = "{0}d ago",
            ["board.runStarted"] = "Run started for issue #{0} — follow it in Cockpit.",
            ["board.issueClosed"] = "Issue #{0} closed ({1}).",
            ["board.closeError"] = "Failed to close issue: {0}",
            ["board.priorityError"] = "Failed to update priority: {0}",
            ["board.moved"] = "Issue #{0} moved to {1}.",
            ["board.movedWithAgent"] = "Issue #{0} moved to {1} and agent {2} started — open in Cockpit.",
            ["board.moveError"] = "Failed to move issue: {0}",
            ["board.loadError"] = "Failed to load issues: {0}",
            ["newtask.dialogTitle"] = "New task",

            ["move.title"] = "Move issue #{0} to…",
            ["move.aria"] = "Move issue #{0}",
            ["move.closeIssue"] = "Close issue",
            ["move.archive"] = "Archive",

            ["newtask.title"] = "Title",
            ["newtask.description"] = "Description (markdown)",
            ["newtask.descAria"] = "Task description in markdown",
            ["newtask.descPlaceholder"] = "## Context\n\n- item\n\n```bash\ncommand\n```",
            ["newtask.column"] = "Initial column",
            ["newtask.nothingToPreview"] = "Nothing to preview.",
            ["newtask.created"] = "Issue created successfully.",
            ["newtask.createError"] = "Failed to create issue: {0}",

            ["agentselect.title"] = "Select CLI agent",
            ["agentselect.noAgents1"] = "No CLI agent is enabled and authenticated. Enable one in",
            ["agentselect.noAgents2"] = "or log in via the",
            ["agentselect.agent"] = "Agent",
            ["agentselect.branch"] = "Work branch",
            ["agentselect.scope"] = "Scope",
            ["agentselect.instructions"] = "Instructions",
            ["agentselect.instructionsRequired"] = "Instructions are required.",
            ["agentselect.detecting"] = "Detecting agents…",
            ["agentselect.start"] = "Start agent",
            ["agentselect.detectError"] = "Failed to detect agents: {0}",

            ["detail.repo"] = "Repository:",
            ["detail.tabHistory"] = "History",
            ["detail.tabComments"] = "Comments",

            ["tasklog.title"] = "Agent run",
            ["tasklog.noActiveRun"] = "No active run to cancel.",
            ["tasklog.permissionExpired"] = "Permission expired or already answered.",
            ["tasklog.permissionError"] = "Failed to answer permission: {0}",

            ["chat.permissionRequest"] = "permission request",
            ["chat.agentQuestion"] = "agent question",

            ["comments.loading"] = "Loading comments…",
            ["comments.none"] = "No comments on this issue.",
            ["comments.new"] = "New comment",
            ["comments.publishedSuffix"] = "(posted to GitHub)",
            ["comments.placeholder"] = "Leave context for the next agent or step…",
            ["comments.loadError"] = "Could not load comments: {0}",
            ["comments.publishedToast"] = "Comment posted to GitHub.",

            ["agentconfig.repo"] = "Repository",
            ["agentconfig.promptPlaceholder"] = "Instructions for the agent to run on this issue…",
            ["agentconfig.promptRequired"] = "The prompt is required.",
            ["agentconfig.model"] = "Model",
            ["agentconfig.managedByCli"] = "Managed by the CLI",
            ["agentconfig.cliPicksModel"] = "The CLI picks the model",
            ["agentconfig.command"] = "Command",
            ["agentconfig.agentStarted"] = "Agent {0} started for issue #{1} — open in Cockpit.",
            ["agentconfig.execError"] = "Failed to run agent: {0}",

            ["detail.tabDetails"] = "Details",
            ["detail.tabAgentConfig"] = "Agent Config",
            ["detail.tabLogs"] = "Agent logs",
            ["detail.column"] = "Column:",
            ["detail.agent"] = "Agent:",
            ["detail.noAgentRan"] = "No agent ran on this issue",
            ["detail.started"] = "started",
            ["detail.finished"] = "finished",
            ["detail.openGithub"] = "Open on GitHub",
            ["detail.updated"] = "Issue #{0} updated.",
            ["detail.saveError"] = "Failed to save issue: {0}",

            ["tasklog.clear"] = "Clear",
            ["tasklog.clearError"] = "Failed to clear logs: {0}",
            ["tasklog.cancelError"] = "Failed to cancel: {0}",

            ["comments.post"] = "Comment",
            ["comments.postError"] = "Failed to comment: {0}",
            ["comments.viewRendered"] = "Rendered",
            ["comments.viewSource"] = "Source",
            ["comments.viewAria"] = "Comment view",

            ["common.edit"] = "Edit",
            ["common.run"] = "Run",

            // SPEC-20261009: delegation wave P5.
            ["agents.compare.useLeg"] = "Use this leg",
            ["agents.compare.useLegPr"] = "Use and open PR",
            ["agents.compare.openPr"] = "Open PR",
            ["agents.activity.title"] = "Activity",
            ["taskdetail.delegate"] = "Delegate to worktree",
        };

    private static readonly IReadOnlyDictionary<string, string> Es =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["layout.skipToMain"] = "Saltar al contenido principal",
            ["layout.expandSidebar"] = "Expandir barra lateral",
            ["layout.collapseSidebar"] = "Contraer barra lateral",
            ["layout.close"] = "Cerrar",
            ["layout.openNav"] = "Abrir navegación",
            ["layout.error"] = "Se produjo un error inesperado. Actualiza la página.",
            ["topbar.settings"] = "Configuración",
            ["topbar.logout"] = "Cerrar sesión",
            ["topbar.language"] = "Idioma",

            ["nav.sidebarNav"] = "Navegación lateral",
            ["nav.repository"] = "Repositorio",
            ["nav.selectRepo"] = "Seleccionar repositorio",
            ["nav.repoShort"] = "Repo",
            ["nav.repoInvalid"] = "Repositorio '{0}' no válido. Usa el formato owner/repo.",
            ["nav.board"] = "Tablero",
            ["nav.aicode"] = "AI Code",
            ["nav.terminal"] = "Terminal",
            ["nav.gantt"] = "Gantt",
            ["nav.workflow"] = "Flujo",
            ["nav.specs"] = "Specs",
            ["nav.editor"] = "VS Code",
            ["nav.cockpit"] = "Cockpit",
            ["nav.agents"] = "Agentes CLI",
            ["nav.finops"] = "FinOps",
            ["nav.settings"] = "Configuración",
            ["nav.skills"] = "Skills",
            ["nav.jobs"] = "Jobs",
            ["nav.prompts"] = "Prompts",
            ["nav.issues"] = "Issues",

            ["login.pageTitle"] = "Iniciar sesión - Harness",
            ["login.subtitle"] = "Inicia sesión para continuar",
            ["login.username"] = "Usuario",
            ["login.password"] = "Contraseña",
            ["login.signin"] = "Iniciar sesión",

            ["common.loading"] = "Cargando…",
            ["common.noItems"] = "Sin elementos",
            ["common.cancel"] = "Cancelar",
            ["common.save"] = "Guardar",
            ["common.close"] = "Cerrar",
            ["common.back"] = "Volver",
            ["common.write"] = "Escribir",
            ["common.preview"] = "Vista previa",
            ["common.current"] = "actual",

            ["board.loadingIssues"] = "Cargando incidencias…",
            ["board.openInCockpit"] = "Abrir en Cockpit",
            ["board.filterPlaceholder"] = "Filtrar por título, descripción o etiqueta…",
            ["board.filterAria"] = "Filtrar incidencias",
            ["board.filterPriorityAria"] = "Filtrar por prioridad",
            ["board.allPriorities"] = "Todas las prioridades",
            ["board.issueActions"] = "Acciones de la incidencia #{0}",
            ["board.moveTo"] = "Mover a…",
            ["board.priorityHeader"] = "Prioridad",
            ["board.priorityTitle"] = "Prioridad: {0}",
            ["board.dragAria"] = "{0}, arrastra para mover",
            ["board.agoNow"] = "ahora",
            ["board.agoMin"] = "hace {0}min",
            ["board.agoHours"] = "hace {0}h",
            ["board.agoDays"] = "hace {0}d",
            ["board.runStarted"] = "Ejecución iniciada para la incidencia #{0} — síguela en Cockpit.",
            ["board.issueClosed"] = "Incidencia #{0} cerrada ({1}).",
            ["board.closeError"] = "Error al cerrar la incidencia: {0}",
            ["board.priorityError"] = "Error al actualizar la prioridad: {0}",
            ["board.moved"] = "Incidencia #{0} movida a {1}.",
            ["board.movedWithAgent"] = "Incidencia #{0} movida a {1} y agente {2} iniciado — abre en Cockpit.",
            ["board.moveError"] = "Error al mover la incidencia: {0}",
            ["board.loadError"] = "Error al cargar las incidencias: {0}",
            ["newtask.dialogTitle"] = "Nueva incidencia",

            ["move.title"] = "Mover incidencia #{0} a…",
            ["move.aria"] = "Mover incidencia #{0}",
            ["move.closeIssue"] = "Cerrar incidencia",
            ["move.archive"] = "Archivar",

            ["newtask.title"] = "Título",
            ["newtask.description"] = "Descripción (markdown)",
            ["newtask.descAria"] = "Descripción de la tarea en markdown",
            ["newtask.descPlaceholder"] = "## Contexto\n\n- elemento\n\n```bash\ncomando\n```",
            ["newtask.column"] = "Columna inicial",
            ["newtask.nothingToPreview"] = "Nada que previsualizar.",
            ["newtask.created"] = "Incidencia creada correctamente.",
            ["newtask.createError"] = "Error al crear la incidencia: {0}",

            ["agentselect.title"] = "Seleccionar agente CLI",
            ["agentselect.noAgents1"] = "No hay ningún agente CLI habilitado y autenticado. Activa uno en",
            ["agentselect.noAgents2"] = "o inicia sesión por el",
            ["agentselect.agent"] = "Agente",
            ["agentselect.branch"] = "Rama de trabajo",
            ["agentselect.scope"] = "Ámbito",
            ["agentselect.instructions"] = "Instrucciones",
            ["agentselect.instructionsRequired"] = "Las instrucciones son obligatorias.",
            ["agentselect.detecting"] = "Detectando agentes…",
            ["agentselect.start"] = "Iniciar agente",
            ["agentselect.detectError"] = "Error al detectar agentes: {0}",

            ["detail.repo"] = "Repositorio:",
            ["detail.tabHistory"] = "Historial",
            ["detail.tabComments"] = "Comentarios",

            ["tasklog.title"] = "Ejecución del agente",
            ["tasklog.noActiveRun"] = "No hay ejecución activa para cancelar.",
            ["tasklog.permissionExpired"] = "Permiso expirado o ya respondido.",
            ["tasklog.permissionError"] = "Error al responder el permiso: {0}",

            ["chat.permissionRequest"] = "solicitud de permiso",
            ["chat.agentQuestion"] = "pregunta del agente",

            ["comments.loading"] = "Cargando comentarios…",
            ["comments.none"] = "No hay comentarios en esta incidencia.",
            ["comments.new"] = "Nuevo comentario",
            ["comments.publishedSuffix"] = "(publicado en GitHub)",
            ["comments.placeholder"] = "Deja contexto para el siguiente agente o paso…",
            ["comments.loadError"] = "No se pudieron cargar los comentarios: {0}",
            ["comments.publishedToast"] = "Comentario publicado en GitHub.",

            ["agentconfig.repo"] = "Repositorio",
            ["agentconfig.promptPlaceholder"] = "Instrucciones para que el agente ejecute en esta incidencia…",
            ["agentconfig.promptRequired"] = "El prompt es obligatorio.",
            ["agentconfig.model"] = "Modelo",
            ["agentconfig.managedByCli"] = "Gestionado por la CLI",
            ["agentconfig.cliPicksModel"] = "La CLI elige el modelo",
            ["agentconfig.command"] = "Comando",
            ["agentconfig.agentStarted"] = "Agente {0} iniciado para la incidencia #{1} — abre en Cockpit.",
            ["agentconfig.execError"] = "Error al ejecutar el agente: {0}",

            ["detail.tabDetails"] = "Detalles",
            ["detail.tabAgentConfig"] = "Agent Config",
            ["detail.tabLogs"] = "Logs del agente",
            ["detail.column"] = "Columna:",
            ["detail.agent"] = "Agente:",
            ["detail.noAgentRan"] = "Ningún agente ejecutó en esta incidencia",
            ["detail.started"] = "iniciado",
            ["detail.finished"] = "finalizado",
            ["detail.openGithub"] = "Abrir en GitHub",
            ["detail.updated"] = "Incidencia #{0} actualizada.",
            ["detail.saveError"] = "Error al guardar la incidencia: {0}",

            ["tasklog.clear"] = "Limpiar",
            ["tasklog.clearError"] = "Error al limpiar los logs: {0}",
            ["tasklog.cancelError"] = "Error al cancelar: {0}",

            ["comments.post"] = "Comentar",
            ["comments.postError"] = "Error al comentar: {0}",
            ["comments.viewRendered"] = "Renderizado",
            ["comments.viewSource"] = "Fuente",
            ["comments.viewAria"] = "Vista del comentario",

            ["common.edit"] = "Editar",
            ["common.run"] = "Ejecutar",

            // SPEC-20261009: delegation wave P5.
            ["agents.compare.useLeg"] = "Usar esta rama",
            ["agents.compare.useLegPr"] = "Usar y abrir PR",
            ["agents.compare.openPr"] = "Abrir PR",
            ["agents.activity.title"] = "Actividad",
            ["taskdetail.delegate"] = "Delegar a worktree",
        };

    /// <summary>Dictionary for a supported culture; unknown → pt-BR.</summary>
    public static IReadOnlyDictionary<string, string> For(string culture) =>
        culture switch
        {
            "en" => En,
            "es" => Es,
            _ => PtBr,
        };

    /// <summary>
    /// Resolves <paramref name="key"/> in <paramref name="culture"/>, falling
    /// back to pt-BR, then en, then the key itself — never empty.
    /// </summary>
    public static string Get(string culture, string key)
    {
        if (For(culture).TryGetValue(key, out var value))
        {
            return value;
        }

        if (PtBr.TryGetValue(key, out value) || En.TryGetValue(key, out value))
        {
            return value;
        }

        return key;
    }

    /// <summary>All keys defined for the product language — the parity baseline.</summary>
    public static IReadOnlyDictionary<string, string> Baseline => PtBr;
}
