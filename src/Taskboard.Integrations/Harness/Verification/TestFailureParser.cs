using System.Xml.Linq;
using Taskboard.Dtos;

namespace Taskboard.Integrations.Harness.Verification;

/// <summary>
/// Parses a TRX test-run file into a <see cref="TestSummaryDto"/> (RF-002) —
/// counters from <c>ResultSummary</c>, failure names/messages/stack traces
/// from failed <c>UnitTestResult</c> entries.
/// </summary>
public static class TestFailureParser
{
    public static TestSummaryDto Parse(string trxXml)
    {
        if (string.IsNullOrWhiteSpace(trxXml))
        {
            return new TestSummaryDto(0, 0, 0, []);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(trxXml);
        }
        catch
        {
            return new TestSummaryDto(0, 0, 0, []);
        }

        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        var counters = doc.Descendants(ns + "Counters").FirstOrDefault();
        var total = IntAttr(counters, "total");
        var passed = IntAttr(counters, "passed");
        var failed = IntAttr(counters, "failed");

        var failures = doc.Descendants(ns + "UnitTestResult")
            .Where(r => (string?)r.Attribute("outcome") == "Failed")
            .Select(r => new TestFailureDto(
                (string?)r.Attribute("testName") ?? "unknown",
                r.Descendants(ns + "Message").FirstOrDefault()?.Value.Trim() ?? string.Empty,
                r.Descendants(ns + "StackTrace").FirstOrDefault()?.Value.Trim()))
            .ToList();

        return new TestSummaryDto(total, passed, Math.Max(failed, failures.Count), failures);
    }

    private static int IntAttr(XElement? element, string name)
        => int.TryParse((string?)element?.Attribute(name), out var v) ? v : 0;
}
