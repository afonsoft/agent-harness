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

    public const int MaxLength = 8192;

    public const string Builtin = """
        Process the issue below using the repository and workflow rules defined in this prompt.

        ## 1. Repository preparation

        Work from the current working directory:

        ~/repos

        Determine the repository directory name from {repoUrl}.

        ### If the repository directory does not exist

        Clone the repository:

        git clone {repoUrl}

        Then enter the cloned repository directory.

        ### If the repository directory already exists

        Do not clone it again.

        Enter the existing repository directory and verify that it is a valid Git repository and that its configured remote corresponds to {repoUrl}.

        Before analyzing the issue or modifying any files, synchronize the repository with the latest remote main branch:

        git fetch --all --prune
        git checkout main
        git pull --ff-only origin main

        If the local main branch does not exist, create it tracking origin/main:

        git checkout -b main --track origin/main

        Do not use `git reset --hard`, do not discard local changes, and do not delete untracked files automatically.

        If local changes, untracked files, merge conflicts, detached HEAD state, or branch divergence prevent a safe checkout or pull, stop the implementation and report the exact condition instead of overwriting or deleting work.

        Validate that:

        - The current branch is main.
        - The local main branch is synchronized with origin/main.
        - The working tree is in a safe state for analysis.
        - The repository remote matches {repoUrl}.

        Always start the analysis from the updated main branch.

        ## 2. Harness validation

        Inspect the repository before selecting the execution workflow.

        Determine whether the repository already follows the Agent Harness standard by checking its existing agent configuration, skills, instructions, commands, hooks, context files, and repository conventions.

        Do not recreate, overwrite, or reinitialize an existing valid Harness.

        ### If the repository already follows the Harness standard

        - Use the Harness capabilities already available in the repository.
        - Do not invoke the `orchestrator` skill.
        - Do not invoke the `create-agent-harness` skill.
        - Continue directly to the SPEC resolution workflow.

        ### If the repository does not follow the Harness standard

        - Prefer the `orchestrator` skill to create or complete the required Harness structure.
        - Use the `orchestrator` skill only when orchestration is required to coordinate the Harness creation or when `create-agent-harness` is unavailable or insufficient.
        - After creating or adjusting the Harness, validate that it is operational before continuing.
        - Do not change application code during the Harness preparation phase unless strictly required by the Harness setup.

        ## 3. SPEC resolution

        Analyze all issue content before creating or executing a specification:

        - Issue title
        - Issue body
        - Issue comments
        - Referenced files
        - Referenced paths
        - Markdown links
        - Code-formatted filenames

        Search for an explicit SPEC reference matching this naming convention:

        .SPEC-{YYYYMMDD}-{feature}.md

        Treat a SPEC as explicitly provided only when the issue identifies a concrete filename or path matching the convention. A generic mention such as "create a spec" or "follow the spec" is not sufficient.

        ### If the issue specifies an existing SPEC

        1. Locate the exact referenced `.SPEC-{YYYYMMDD}-{feature}.md` file in the repository.
        2. Validate that the filename and path match the issue reference.
        3. Read the complete SPEC and any files referenced by it.
        4. Use the `execute-specs` skill to execute that SPEC.
        5. Do not create a duplicate SPEC.
        6. Do not use `write-specs` unless the referenced SPEC cannot be found or is structurally invalid.

        If the referenced SPEC cannot be found:

        - Search the repository for the exact filename.
        - Search for close matches only to diagnose the problem.
        - Do not silently execute a different SPEC.
        - If no valid match exists, use `write-specs` to create a new SPEC based on the complete issue context and clearly record that the referenced SPEC was unavailable.

        ### If the issue does not specify an existing SPEC

        1. Use the `write-specs` skill to create a new specification.
        2. Name it according to:

           .SPEC-{YYYYMMDD}-{feature}.md

        3. Derive `{feature}` from the issue using a concise, descriptive, lowercase kebab-case name.
        4. Use the current execution date for `{YYYYMMDD}`.
        5. Build the SPEC from:
           - {issueTitle}
           - {issueBody}
           - {issueComments}
           - Repository architecture and conventions
           - Existing documentation
           - Relevant Harness context
           - Additional context retrieved from the `knowledge` MCP when necessary
        6. Ensure the SPEC contains:
           - Context and problem statement
           - Goals
           - Scope
           - Out-of-scope items
           - Functional requirements
           - Non-functional requirements
           - Architectural constraints
           - Security and observability requirements
           - Implementation plan
           - Testing strategy
           - Acceptance criteria
           - Validation steps
           - Traceability to the source issue
        7. Validate the generated SPEC before implementation.
        8. After validation, use the `execute-specs` skill to execute the newly created SPEC.

        Never start implementation directly from the raw issue when no valid SPEC exists.

        ## 4. Execution rules

        During SPEC execution:

        - Treat the SPEC as the primary source of truth.
        - Follow the repository's existing architecture, coding conventions, instructions, and Harness definitions.
        - Use the available specialized skills whenever applicable.
        - Consult the `knowledge` MCP only when additional context is required.
        - Do not invent missing business rules or acceptance criteria.
        - Record assumptions explicitly in the SPEC.
        - Keep changes limited to the issue scope.
        - Create or update automated tests for the implemented behavior.
        - Run the relevant build, lint, formatting, unit test, integration test, and validation commands supported by the repository.
        - Do not claim a validation succeeded unless the corresponding command was executed successfully.
        - Do not commit, push, open a pull request, or modify remote resources unless explicitly requested.

        ## 5. Taskboard management

        Use the `manage-taskboard` skill to manage the issue card throughout the execution.

        - Move the issue to the appropriate in-progress state before implementation.
        - Add meaningful progress updates at important checkpoints.
        - Record the SPEC filename used or created.
        - Record validation results and any blockers.
        - Move the issue to the appropriate final state only after implementation and validation are complete.
        - Do not mark the issue as completed when required validations fail.

        ## 6. Final validation

        Before finishing:

        1. Confirm whether the repository already had a valid Harness or whether one was created.
        2. Confirm which Harness-related skill was used, if any.
        3. Confirm the repository was analyzed from the updated main branch.
        4. Confirm the SPEC filename and path.
        5. Confirm whether the SPEC came from the issue or was created with `write-specs`.
        6. Confirm that `execute-specs` was used.
        7. Review all changed files.
        8. Run the repository's applicable validation commands.
        9. Compare the implementation against every acceptance criterion in the SPEC.
        10. Check for unrelated or accidental changes.
        11. Confirm that the issue card contains the final execution status.

        ## 7. Final response

        Provide a concise final report containing:

        - Repository preparation status
        - Harness validation result
        - Harness-related skill used, if applicable
        - SPEC filename and path
        - Whether the SPEC was provided or generated
        - Summary of implemented changes
        - Files changed
        - Tests and validation commands executed
        - Validation results
        - Taskboard update status
        - Remaining risks, assumptions, or blockers

        ## Issue

        Title:

        {issueTitle}

        Body:

        {issueBody}

        Comments:

        {issueComments}
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
