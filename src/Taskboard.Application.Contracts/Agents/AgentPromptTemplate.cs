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
        Process the issue using a Knowledge-First, Spec-Driven Development and Harness Engineering workflow.

        Repository:

        {repoUrl}

        Issue title:

        {issueTitle}

        Issue body:

        {issueBody}

        Issue comments:

        {issueComments}

        # Mandatory rules

        - Always use the `knowledge` MCP.
        - Start by calling `read_wiki_structure` with `owner/repo`.
        - Use `write_note` after every major step.
        - Use `write_knowledge` at the end of the process.
        - Always use the `manage-taskboard` skill or CLI.
        - Always start repository analysis from the latest safe local `main`.
        - Never implement directly from the raw issue when no valid SPEC exists.
        - Treat the SPEC as the primary source of truth during implementation.
        - Do not silently invent business rules or acceptance criteria.
        - Do not discard local work automatically.
        - Do not create pull requests, or modify remote resources unless explicitly requested.
        - Do not claim that a tool, command, test, note, or knowledge write succeeded unless it was actually executed successfully.

        # 1. Discover the repository wiki

        Before cloning, updating, analyzing, planning, creating a SPEC, or modifying the repository, extract the repository owner and repository name from `{repoUrl}`.

        Normalize the repository identifier to:

        owner/repo

        Do not infer `owner/repo` from the issue title.

        Use the exact repository URL to extract it.

        Call:

        read_wiki_structure(owner/repo)

        Use the repository wiki to discover:

        - Repository purpose
        - Product context
        - Architecture
        - Components
        - Integrations
        - ADRs
        - Engineering standards
        - Coding conventions
        - Development workflow
        - Branch conventions
        - Testing guidance
        - Security guidance
        - Observability guidance
        - Operational procedures
        - Harness documentation
        - Agent documentation
        - Skill documentation
        - SPEC documentation
        - Known limitations
        - Previous implementation decisions

        If wiki content exists:

        1. Read the complete wiki structure.
        2. Identify pages relevant to the issue.
        3. Use `read_wiki_contents` to read the relevant pages.
        4. Prioritize pages related to:
           - The issue feature
           - The affected component
           - Architecture
           - Business rules
           - Harness
           - Specs
           - Tests
           - Security
           - Observability
           - Operations
        5. Consolidate the relevant findings.

        If the wiki does not exist or no content is returned, record:

        "No repository wiki was found for owner/repo."

        Do not treat missing wiki content as a reason to stop the workflow.

        After completing this step:

        1. Use `write_note`.
        2. Record:
           - `owner/repo`
           - Wiki availability
           - Wiki pages discovered
           - Wiki pages read
           - Relevant findings
           - Missing information
           - Conflicts found
        3. Keep the findings available for SPEC creation, implementation, validation and final knowledge persistence.

        # 2. Initialize task management

        Use the `manage-taskboard` skill when available.

        If the skill is unavailable and its CLI exists, use the `manage-taskboard` CLI.

        Determine the issue card from the provided issue context.

        Move the issue card to the appropriate in-progress state.

        Add an initial taskboard update containing:

        - Repository
        - `owner/repo`
        - Issue title
        - Execution start
        - Current phase
        - Wiki discovery result
        - Planned workflow

        Do not create a duplicate card if one already exists.

        If the exact issue card cannot be identified:

        1. Do not update an unrelated card.
        2. Record the lookup failure.
        3. Continue with repository analysis when safe.
        4. Include the taskboard limitation in every relevant final status.

        After completing this step, use `write_note`.

        The note must contain:

        - Taskboard mechanism used
        - Card identifier, when found
        - Previous state
        - Current state
        - Initial update result
        - Blockers or warnings


        # 3. Prepare the repository

        Work from:

        ~/repos

        Determine the repository directory name from `{repoUrl}`.

        If the repository directory does not exist:

        1. Clone `{repoUrl}` into `~/repos`.
        2. Enter the cloned repository directory.

        If the repository directory already exists:

        1. Do not clone it again.
        2. Enter the existing directory.
        3. Verify that it is a valid Git repository.
        4. Verify that its configured remote corresponds to `{repoUrl}`.
        5. Run:

           git fetch --all --prune
           git checkout main
           git pull --ff-only origin main

        If the local `main` branch does not exist but `origin/main` exists, run:

           git checkout -b main --track origin/main

        If neither `main` nor `origin/main` exists:

        1. Do not guess another default branch.
        2. Record the condition.
        3. Update the taskboard.
        4. Stop repository modifications until the branch requirement can be resolved safely.

        Validate that:

        - The repository path is correct.
        - The repository is valid.
        - The configured remote matches `{repoUrl}`.
        - The current branch is `main`.
        - Local `main` is synchronized with `origin/main`.
        - The working tree is safe for analysis.


        If local changes, untracked files, conflicts, detached HEAD, branch divergence, an incorrect remote, or another condition prevents safe synchronization:

        1. Stop repository modifications.
        2. Record the exact condition.
        3. List the affected files when safe.
        4. Update the taskboard with the blocker.
        5. Do not overwrite or delete existing work.

        All code analysis and implementation must start from a safely updated `main`.

        After completing this step:

        1. Update the taskboard with the repository status.
        2. Use `write_note`.
        3. Record:
           - Repository path
           - Remote
           - Current branch
           - Synchronization status with `origin/main`
           - Working tree state
           - Blockers or unsafe conditions found

        # 4. Validate the harness

        Inspect the repository structure and determine whether it already follows Harness Engineering standards.

        Look for:

        - agents
        - skills
        - commands
        - context
        - hooks
        - harness configuration
        - orchestration artifacts
        - agent workflows

        If the repository already follows the Harness standard:

        1. Use the existing Harness.
        2. Do not create a new one and do not run `create-agent-harness`.
        3. Do not recreate agents; use the existing repository conventions.
        4. Load context through the `knowledge` MCP before selecting agents.
        5. Use `agent_chat` when agent coordination is needed.

        If the repository does not follow the Harness standard:

        1. Run `create-agent-harness`.
        2. Create only the required structure.
        3. Validate that the harness is operational.
        4. Register the `knowledge` MCP as a mandatory context source.
        5. Use the orchestrator only when coordination is required or `create-agent-harness` is unavailable.

        After completing this step, use `write_note` and update the taskboard with the harness status.

        # 5. Resolve the specification

        Specification-first execution is mandatory. Search for an existing SPEC before creating one.

        Priority order:

        1. Knowledge MCP
        2. Existing SPEC
        3. Repository documentation
        4. Wiki content
        5. Issue content

        Inspect the issue title, body, comments, referenced filenames, referenced paths and markdown links for a spec file matching:

        .specs/SPEC-{YYYYMMDD}-{feature}.md

        If an existing SPEC is referenced:

        1. Locate the exact spec and validate filename, path and integrity.
        2. Read the spec completely, plus any referenced documents and repository context.
        3. Execute it with `execute-specs`.
        4. Do not create another spec.
        5. If the referenced spec cannot be located, search the repository, knowledge sources, wiki and documentation before recreating it with `write-specs`.

        If no valid SPEC exists:

        1. Create one with `write-specs` from the Knowledge MCP, wiki, repository documentation, task context and issue content.
        2. Name it `SPEC-{YYYYMMDD}-{feature}.md` — lowercase kebab-case feature name, current date.
        3. The spec must contain: Context, Problem Statement, Business Requirements, Functional Requirements, Non Functional Requirements, Architectural Constraints, Security Requirements, Observability Requirements, Acceptance Criteria, Testing Strategy, Validation Steps, Rollback Strategy, Risks, Assumptions, Implementation Plan, Traceability and Knowledge Sources.
        4. Validate the spec, then execute it with `execute-specs`.

        After completing this step, use `write_note` and update the taskboard with the spec path.

        # 6. Execute the specification

        The SPEC is the source of truth during implementation.

        - Follow the repository architecture, coding standards and conventions.
        - Follow the Harness conventions.
        - Enrich execution with the `knowledge` MCP, repository context and existing patterns.
        - Use specialized skills whenever available.
        - Do not invent requirements; document assumptions explicitly.
        - Keep changes within scope.
        - Create or update unit, integration, contract and validation tests when applicable.

        Update the taskboard with implementation progress and use `write_note` after major implementation steps.

        # 7. Validate

        Run all applicable validation procedures:

        - build
        - lint
        - format
        - unit tests
        - integration tests
        - contract tests
        - security validation
        - static analysis

        Compare the implementation against every acceptance criterion in the SPEC.

        Validate code changes, tests, documentation, specs and taskboard updates. Check for unrelated changes.

        After completing this step, update the taskboard with the validation result and use `write_note`.

        # 8. Finalize

        Before finishing, confirm:

        - Knowledge MCP consulted and wiki discovery executed
        - `write_note` used after every major step
        - Taskboard card moved and updated throughout execution
        - Repository synchronized from a safe local `main`
        - Harness validated
        - Existing SPEC used or new SPEC created and executed
        - Validation executed successfully
        - Acceptance criteria satisfied

        Then:

        1. Use `write_knowledge` to persist the outcome: decisions made, implementation summary, validation results, blockers and lessons learned.
        2. Update the taskboard with the final status, spec reference, implementation summary and validation summary.
        3. Provide a final report containing:
           - Knowledge summary (sources consulted, wiki pages and documents reviewed)
           - Repository status (path, branch, synchronization)
           - Harness status (existing or created, skills and agents used)
           - Spec information (filename, path, existing or generated)
           - Implementation summary (changes completed, files modified)
           - Validation summary (commands executed, tests, results)
           - Taskboard summary (states transitioned, progress updates)
           - Risks and assumptions (remaining concerns, known limitations)
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
            && template[..placeholderIndex].TrimEnd().EndsWith(header, StringComparison.OrdinalIgnoreCase);
        return templateHasHeader ? lines : $"Comments:\n{lines}";
    }
}
