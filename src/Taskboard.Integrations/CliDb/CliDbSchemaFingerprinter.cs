using Taskboard.Dtos;

namespace Taskboard.Integrations.CliDb;

/// <summary>
/// Compares schema fingerprints — mismatch means the vendor changed the schema
/// and the extractor must report drift instead of running.
/// SPEC-20260919-cli-db-reader RF-003.
/// </summary>
public static class CliDbSchemaFingerprinter
{
    public static bool Matches(CliDbSchemaFingerprint expected, CliDbSchemaFingerprint actual, out string diff)
    {
        var diffs = new List<string>();
        if (expected.UserVersion != actual.UserVersion)
        {
            diffs.Add($"user_version {expected.UserVersion}→{actual.UserVersion}");
        }
        if (expected.ApplicationId != actual.ApplicationId)
        {
            diffs.Add($"application_id {expected.ApplicationId}→{actual.ApplicationId}");
        }

        var expectedTables = expected.TablesSignature.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var actualTables = actual.TablesSignature.Split('|', StringSplitOptions.RemoveEmptyEntries);
        foreach (var table in expectedTables.Except(actualTables))
        {
            diffs.Add($"missing-or-changed: {table}");
        }
        foreach (var table in actualTables.Except(expectedTables))
        {
            diffs.Add($"unexpected: {table}");
        }

        diff = string.Join("; ", diffs);
        return diffs.Count == 0;
    }
}
