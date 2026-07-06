using System.Text.Json.Serialization;

namespace LabelDesigner.Core.Models;

public enum BtwMigrationStatus
{
    /// <summary>Found in the scan, nothing done yet.</summary>
    Pending,
    /// <summary>A .lbl skeleton (size + optional backdrop/fields) has been created.</summary>
    SkeletonCreated,
    /// <summary>Being rebuilt in the Designer.</summary>
    InProgress,
    /// <summary>Rebuilt and verified — this .btw is no longer needed.</summary>
    Done,
    /// <summary>Deliberately not migrating (obsolete label etc.).</summary>
    Skipped,
    /// <summary>The .btw header could not be read; size/title unknown.</summary>
    Unreadable
}

/// <summary>
/// One BarTender file in the migration tracker. The tracker records the manual rebuild of the
/// .btw library (auto-conversion of BarTender's proprietary binary is deliberately NOT attempted),
/// so progress toward dropping the BarTender licence is visible and nothing gets forgotten.
/// </summary>
public class BtwMigrationEntry
{
    public string SourcePath { get; set; } = "";
    public string Title { get; set; } = "";
    public double WidthMm  { get; set; }
    public double HeightMm { get; set; }
    public string Printer { get; set; } = "";

    public BtwMigrationStatus Status { get; set; } = BtwMigrationStatus.Pending;

    /// <summary>Name of the .lbl template this .btw is being rebuilt as (set when the skeleton is created).</summary>
    public string TargetTemplateName { get; set; } = "";

    public string Notes { get; set; } = "";
    public DateTime? UpdatedUtc { get; set; }

    /// <summary>Set during scan when the recorded .btw no longer exists on disk. Not persisted.</summary>
    [JsonIgnore] public bool SourceMissing { get; set; }

    [JsonIgnore] public string FileName => Path.GetFileName(SourcePath);

    [JsonIgnore] public string SizeText =>
        WidthMm > 0 && HeightMm > 0 ? $"{WidthMm:0.#} × {HeightMm:0.#} mm" : "?";
}
