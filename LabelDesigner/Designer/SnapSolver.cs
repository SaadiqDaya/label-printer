namespace LabelDesigner.Designer;

/// <summary>
/// Pure math for smart-snap alignment guides: given the moving selection's candidate edges
/// (left/centre/right or top/centre/bottom) and the stationary targets (other elements' edges
/// plus canvas edges/centre), finds the smallest correction that aligns an edge pair.
/// </summary>
public static class SnapSolver
{
    /// <summary>
    /// Returns the correction to add to the drag delta so the closest (moving, target) pair aligns,
    /// and the target coordinate to draw the guide line at. (0, null) when nothing is within
    /// <paramref name="threshold"/>.
    /// </summary>
    public static (double Correction, double? GuideAt) Solve(
        IReadOnlyList<double> movingEdges, IReadOnlyList<double> targets, double threshold)
    {
        double best = double.MaxValue;
        double correction = 0;
        double? guide = null;

        foreach (var m in movingEdges)
            foreach (var t in targets)
            {
                var d = Math.Abs(t - m);
                if (d < best && d <= threshold)
                {
                    best = d;
                    correction = t - m;
                    guide = t;
                }
            }
        return (correction, guide);
    }
}
