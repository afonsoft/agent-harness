using System.Xml.Linq;

namespace Taskboard.Integrations.Harness.Verification;

/// <summary>
/// Reads line coverage from a Cobertura XML report (RF-002/RF-003).
/// Returns 0.0 when the file is missing/invalid — the engine reports the
/// warning and continues with the test result (SPEC edge cases).
/// </summary>
public static class CoverageCalculator
{
    public static double ParseLineRate(string coberturaXml)
    {
        if (string.IsNullOrWhiteSpace(coberturaXml))
        {
            return 0.0;
        }

        try
        {
            var root = XDocument.Parse(coberturaXml).Root;
            return root is not null
                && double.TryParse(
                    (string?)root.Attribute("line-rate"),
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var rate)
                ? rate * 100.0
                : 0.0;
        }
        catch
        {
            return 0.0;
        }
    }
}
