using Taskboard.Agents;

namespace Taskboard.Application.Contracts.Agents;

/// <summary>
/// Default agent prompt template and placeholder substitution
/// (SPEC-20260918-agent-execution-ux RF-004). The runtime override lives at
/// <c>Taskboard:Agents:DefaultPrompt</c>; this builtin is the fallback.
/// </summary>
public static class AgentPromptTemplate
{
    public const string ConfigurationKey = "Taskboard:Agents:DefaultPrompt";

    public const int MaxLength = 16384;

    public const string Builtin = """
        Process the issue using a Knowledge-First, Spec-Driven Development (SDD), and Harness Engineering workflow.

        The goal is to always start from the latest main branch, leverage institutional knowledge before making decisions, use specifications as the source of truth, and continuously update task execution status through the taskboard.

        ======================================================================
        0. KNOWLEDGE DISCOVERY (MANDATORY)
        ======================================================================

        Knowledge discovery is mandatory.

        Before performing repository analysis, specification creation, planning, implementation, debugging, refactoring, testing, or validation, consult the Knowledge MCP.

        Knowledge MCP is the primary source of truth for business rules, architectural decisions, standards, patterns, lessons learned, runbooks, ADRs, documentation, previous implementations, and project-specific knowledge.

        Always execute the following workflow:

        ### Step 1 - Search for Existing Knowledge

        Search using:

        - Repository name
        - Project name
        - Issue title
        - Issue keywords
        - Business domain terms
        - Technical keywords
        - Referenced features
        - Referenced components
        - Existing specs

        Tools:

        - search_knowledge
        - ask_knowledge

        ### Step 2 - Search Internal Documentation

        If additional context is needed:

        - query_openclaw_vault
        - read_document
        - ask_question

        ### Step 3 - Search Wiki Content

        Always attempt to discover wiki content:

        - read_wiki_structure
        - read_wiki_contents

        ### Step 4 - Search External References

        If the issue references documents, URLs, frameworks, libraries, standards, APIs, vendors, or external systems:

        - firecrawl_search
        - firecrawl_scrape
        - firecrawl_parse

        ### Step 5 - Perform Broader Research

        When additional context or technical research is beneficial:

        - tavily_search
        - tavily_extract
        - tavily_research
        - tavily_crawl

        ### Step 6 - Agent Collaboration

        If multiple agents need coordination:

        - agent_chat

        ### Knowledge Rules

        - Never skip knowledge discovery.
        - Never start implementation without consulting Knowledge MCP.
        - Never create a spec using only the issue when additional knowledge is available.
        - Consolidate all discovered knowledge before creating or executing specs.
        - Use discovered information to enrich planning, specifications, implementation, tests, documentation, and validation.

        If no relevant knowledge exists, explicitly record:

        "No relevant knowledge was found in the Knowledge MCP."

        ======================================================================
        1. REPOSITORY PREPARATION
        ======================================================================

        Working directory:

        ~/repos

        Determine repository name from:

        {repoUrl}

        ----------------------------------------------------------------------
        If repository does not exist
        ----------------------------------------------------------------------

        Clone repository:

        git clone {repoUrl}

        Enter repository directory.

        ----------------------------------------------------------------------
        If repository already exists
        ----------------------------------------------------------------------

        Do not clone again.

        Enter repository directory.

        Validate:

        - Repository exists
        - Repository is a valid git repository
        - Remote matches {repoUrl}

        Synchronize repository:

        git fetch --all --prune
        git checkout main
        git pull --ff-only origin main

        If local main branch does not exist:

        git checkout -b main --track origin/main

        Never automatically:

        - git reset --hard
        - delete user files
        - remove untracked content
        - discard local work

        If local modifications prevent safe synchronization:

        Stop and report:

        - modified files
        - conflicts
        - diverged branches
        - detached head state

        Do not overwrite user work.

        ----------------------------------------------------------------------
        Repository Validation
        ----------------------------------------------------------------------

        Confirm:

        - Current branch is main
        - Local main is synchronized with origin/main
        - Working tree is safe
        - Remote matches repository URL

        All subsequent analysis must begin from the updated main branch.

        ======================================================================
        2. TASKBOARD MANAGEMENT (MANDATORY)
        ======================================================================

        The manage-taskboard skill or CLI is mandatory.

        Taskboard management must occur throughout execution.

        Immediately:

        - Move card to In Progress

        Add updates for:

        1. Knowledge Discovery started
        2. Knowledge sources discovered
        3. Repository validation completed
        4. Harness validation completed
        5. Spec discovery completed
        6. Spec creation completed
        7. Spec execution started
        8. Implementation progress
        9. Validation progress
        10. Blockers and assumptions
        11. Final validation results

        Before completion:

        - Attach spec reference
        - Attach validation summary
        - Attach implementation summary

        Do not mark task complete unless:

        - Knowledge discovery executed
        - Spec exists
        - Validation completed
        - Required checks succeeded

        ======================================================================
        3. HARNESS VALIDATION
        ======================================================================

        Inspect repository structure.

        Determine whether repository already follows Harness Engineering standards.

        Look for:

        - agents
        - skills
        - commands
        - context
        - hooks
        - harness configuration
        - orchestration artifacts
        - agent workflows

        ----------------------------------------------------------------------
        If repository already follows Harness standard
        ----------------------------------------------------------------------

        Use existing Harness.

        Do NOT create a new Harness.

        Do NOT execute create-agent-harness.

        Do NOT recreate agents.

        Use existing repository conventions.

        Load context through Knowledge MCP before selecting agents.

        Use:

        - agent_chat

        when agent coordination is needed.

        ----------------------------------------------------------------------
        If repository does not follow Harness standard
        ----------------------------------------------------------------------

        Execute:

        create-agent-harness

        Create only required structure.

        Validate harness is operational.

        Register Knowledge MCP as a mandatory context source.

        Use orchestrator only when:

        - coordination is required
        or
        - create-agent-harness is unavailable

        ======================================================================
        4. SPEC RESOLUTION
        ======================================================================

        Specification-first execution is mandatory.

        Priority order:

        1. Knowledge MCP
        2. Existing Spec
        3. Repository Documentation
        4. Wiki Content
        5. OpenClaw Vault
        6. Issue Content

        Never implement directly from the raw issue.

        ----------------------------------------------------------------------
        Look for Existing Spec
        ----------------------------------------------------------------------

        Inspect:

        - Issue title
        - Issue body
        - Issue comments
        - Referenced filenames
        - Referenced paths
        - Markdown links

        Search for:

        .SPEC-{YYYYMMDD}-{feature}.md

        ----------------------------------------------------------------------
        If Existing Spec Is Referenced
        ----------------------------------------------------------------------

        Locate exact spec.

        Validate:

        - filename
        - path
        - integrity

        Read spec completely.

        Read any referenced documents.

        Read repository context.

        Execute:

        execute-specs

        Do NOT create another spec.

        If referenced spec cannot be located:

        - search repository
        - search knowledge sources
        - search wiki
        - search documentation

        Only after validation failure:

        Use:

        write-specs

        to recreate the spec.

        ----------------------------------------------------------------------
        If No Existing Spec Exists
        ----------------------------------------------------------------------

        Use:

        write-specs

        Create:

        .SPEC-{YYYYMMDD}-{feature}.md

        Feature name requirements:

        - lowercase
        - kebab-case
        - descriptive

        Use current date for YYYYMMDD.

        Create specification from:

        - Knowledge MCP
        - OpenClaw Vault
        - Wiki
        - Repository Documentation
        - Task Context
        - Issue Content

        Generated spec must contain:

        # Context

        # Problem Statement

        # Business Requirements

        # Functional Requirements

        # Non Functional Requirements

        # Architectural Constraints

        # Security Requirements

        # Observability Requirements

        # Acceptance Criteria

        # Testing Strategy

        # Validation Steps

        # Rollback Strategy

        # Risks

        # Assumptions

        # Implementation Plan

        # Traceability

        # Knowledge Sources

        Knowledge Sources section must list:

        - search_knowledge queries
        - ask_knowledge responses
        - vault documents
        - wiki content
        - firecrawl results
        - tavily results

        Validate generated spec.

        After validation:

        Execute:

        execute-specs

        ======================================================================
        5. SPEC EXECUTION
        ======================================================================

        Spec is the source of truth.

        During execution:

        - Follow repository architecture
        - Follow existing coding standards
        - Follow repository conventions
        - Follow Harness conventions

        Always enrich execution using:

        - Knowledge MCP
        - Repository context
        - Existing patterns

        Use specialized skills whenever available.

        Do not invent requirements.

        Document assumptions explicitly.

        Keep changes within scope.

        Create or update:

        - Unit Tests
        - Integration Tests
        - Contract Tests
        - Validation Tests

        when applicable.

        ======================================================================
        6. VALIDATION
        ======================================================================

        Run all applicable validation procedures.

        Examples:

        - build
        - lint
        - format
        - unit tests
        - integration tests
        - contract tests
        - security validation
        - static analysis

        Do not claim validation success unless executed successfully.

        Compare implementation against every acceptance criterion.

        Validate:

        - code changes
        - tests
        - documentation
        - specs
        - taskboard updates

        Check for unrelated changes.

        ======================================================================
        7. RESTRICTIONS
        ======================================================================

        Do NOT:

        - commit
        - push
        - create pull requests
        - modify remote resources

        unless explicitly requested.

        Do NOT:

        - skip Knowledge MCP
        - skip spec generation
        - skip spec execution
        - skip taskboard updates

        Knowledge Discovery, Specs, and Taskboard are mandatory phases.

        ======================================================================
        8. FINAL VALIDATION CHECKLIST
        ======================================================================

        Before finishing confirm:

        □ Knowledge MCP consulted

        □ search_knowledge executed

        □ ask_knowledge executed

        □ Repository synchronized with main

        □ Harness validated

        □ Existing spec found OR spec created

        □ write-specs executed (when required)

        □ execute-specs executed

        □ Tests executed

        □ Validation completed

        □ Taskboard updated

        □ Acceptance criteria satisfied

        ======================================================================
        9. FINAL REPORT
        ======================================================================

        Provide a final report containing:

        # Knowledge Summary

        - Knowledge sources consulted
        - Documents reviewed
        - Wiki pages reviewed
        - Vault entries reviewed
        - External research performed

        # Repository Status

        - Repository path
        - Current branch
        - Synchronization status

        # Harness Status

        - Existing Harness or Created Harness
        - Skills and agents used

        # Spec Information

        - Spec filename
        - Spec path
        - Existing or Generated

        # Implementation Summary

        - Changes completed
        - Files modified

        # Validation Summary

        - Commands executed
        - Tests executed
        - Results

        # Taskboard Summary

        - States transitioned
        - Progress updates recorded

        # Risks and Assumptions

        - Remaining concerns
        - Known limitations

        ======================================================================
        ISSUE
        ======================================================================

        Title:

        {issueTitle}

        Body:

        {issueBody}

        Comments:

        {issueComments}

        Repository:

        {repoUrl}
        """;


    /// <summary>
    /// Returns true when the template carries the <c>{issueComments}</c>
    /// placeholder (older overrides may not).
    /// </summary>
    public static bool HasCommentsPlaceholder(string template) =>
        template.Contains("{issueComments}", StringComparison.Ordinal);

    /// <summary>
    /// Substitutes <c>{repoUrl}</c>, <c>{issueTitle}</c>, <c>{issueBody}</c> and
    /// <c>{issueComments}</c>. The comments section keeps exactly one
    /// "Comments:" header — whether it comes from the template or from the
    /// supplied value — so older overrides without the header still render a
    /// labelled section and callers may pass bare lines or a labelled section.
    /// </summary>
    public static string Render(
        string template, string? repoUrl, string? issueTitle, string? issueBody, string? issueComments = null)
    {
        var comments = NormalizeComments(template, issueComments);
        return template
            .Replace("{repoUrl}", repoUrl ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueTitle}", issueTitle ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueBody}", issueBody ?? string.Empty, StringComparison.Ordinal)
            .Replace("{issueComments}", comments ?? string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string? NormalizeComments(string template, string? issueComments)
    {
        if (string.IsNullOrWhiteSpace(issueComments))
        {
            return issueComments;
        }

        var lines = issueComments.Trim();
        const string header = "Comments:";
        if (lines.StartsWith(header, StringComparison.Ordinal))
        {
            lines = lines[header.Length..].TrimStart();
        }

        var placeholderIndex = template.IndexOf("{issueComments}", StringComparison.Ordinal);
        var templateHasHeader = placeholderIndex >= 0
            && template[..placeholderIndex].TrimEnd().EndsWith(header, StringComparison.Ordinal);
        return templateHasHeader ? lines : $"Comments:\n{lines}";
    }
}
