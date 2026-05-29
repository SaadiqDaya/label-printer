using LabelDesigner.Core.Models;
using LabelDesigner.Core.Services;
using LabelDesigner.Services;
using System.Globalization;

namespace LabelDesigner.Helpers;

/// <summary>
/// Resolves DataSourceDefinition items into concrete string values at preview/print time.
/// </summary>
public static class DataSourceResolver
{
    /// <summary>
    /// Returns a dictionary of field-name → resolved value for all data sources.
    /// These are merged into the fields dict before element substitution.
    ///
    /// Date/time values use <see cref="DateTime.Now"/> in the local timezone of the printing
    /// workstation. This is intentional — labels are printed on a single physical machine
    /// in a single venue (VanGo Production), so "today" should mean the local civil date.
    /// </summary>
    public static Dictionary<string, string> Resolve(IEnumerable<DataSourceDefinition> sources)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ds in sources)
        {
            if (string.IsNullOrWhiteSpace(ds.Name) || ds.Type == DataSourceType.Formula) continue;
            // For display/preview, serial shows its configured start (no persistence lookup).
            result[ds.Name] = Format(ds, ds.SerialStart);
        }
        return result;
    }

    /// <summary>
    /// Resolves data sources for the printed label at the given zero-based <paramref name="ordinal"/>
    /// within the current batch. Serial sources advance from their PERSISTED base (so sequences
    /// continue across sessions and jobs): value = persistedBase + ordinal × Increment.
    /// </summary>
    public static Dictionary<string, string> Resolve(LabelTemplate template, int ordinal,
        IReadOnlyDictionary<Guid, long>? reservedBases = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ds in template.DataSources)
        {
            if (string.IsNullOrWhiteSpace(ds.Name) || ds.Type == DataSourceType.Formula) continue;
            // Prefer the batch's reserved base (so all copies use one atomically-reserved range);
            // fall back to the live persisted base for preview/validation callers.
            long baseVal = reservedBases != null && reservedBases.TryGetValue(ds.Id, out var rb)
                ? rb
                : SerialCounterStore.GetBase(template, ds);
            long serial = baseVal + (long)ordinal * Math.Max(1, ds.Increment);
            result[ds.Name] = Format(ds, serial);
        }
        return result;
    }

    /// <summary>
    /// Resolves only the NON-serial data sources (date/time/relative/fixed) — values that are
    /// constant across a batch. Captured into print history so a reprint reproduces the ORIGINAL
    /// date/values, not today's.
    /// </summary>
    public static Dictionary<string, string> ResolveConstants(LabelTemplate template)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ds in template.DataSources)
        {
            if (string.IsNullOrWhiteSpace(ds.Name) || ds.Type == DataSourceType.Serial || ds.Type == DataSourceType.Formula) continue;
            result[ds.Name] = Format(ds, 0);
        }
        return result;
    }

    /// <summary>
    /// Evaluates Formula sources against the supplied field dict (row fields, computed sources and
    /// field defaults must already be present). Call this LAST when building a label's fields.
    /// </summary>
    public static void ApplyFormulas(IEnumerable<DataSourceDefinition> sources, Dictionary<string, string> fields)
    {
        foreach (var ds in sources)
            if (ds.Type == DataSourceType.Formula && !string.IsNullOrWhiteSpace(ds.Name))
                fields[ds.Name] = FormulaEvaluator.Evaluate(ds.FormulaExpression, fields);
    }

    private static string Format(DataSourceDefinition ds, long serialValue)
    {
        try
        {
            return ds.Type switch
            {
                DataSourceType.CurrentDate  => DateTime.Now.ToString(ds.Format, CultureInfo.CurrentCulture),
                DataSourceType.CurrentTime  => DateTime.Now.ToString(ds.Format, CultureInfo.CurrentCulture),
                DataSourceType.RelativeDate => DateTime.Now
                                                  .AddMonths(ds.RelativeMonths)
                                                  .AddDays(ds.RelativeDays)
                                                  .ToString(ds.Format, CultureInfo.CurrentCulture),
                DataSourceType.Serial       => SerialFormatting.Format(serialValue, ds.SerialPrefix, ds.SerialSuffix, ds.SerialRadix, ds.SerialPadWidth, ds.Format),
                DataSourceType.FixedValue   => ds.FixedValue,
                _                           => ""
            };
        }
        catch { return ""; }
    }
}
