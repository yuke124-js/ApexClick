namespace ApexClick.Models;

public static class MousePathSimplifier
{
    public static IReadOnlyList<MousePathPoint> Simplify(
        IReadOnlyList<MousePathPoint> points,
        double normalizedTolerance,
        int maxPoints = 32)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count <= 2)
            return points.ToArray();

        maxPoints = Math.Max(2, maxPoints);
        normalizedTolerance = Math.Max(0, normalizedTolerance);

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        SimplifyRange(points, 0, points.Count - 1, normalizedTolerance, keep);

        var result = new List<MousePathPoint>();
        for (var i = 0; i < points.Count; i++)
            if (keep[i]) result.Add(points[i]);

        if (result.Count <= maxPoints)
            return result;

        var compact = new List<MousePathPoint>(maxPoints) { result[0] };
        var step = (result.Count - 1d) / (maxPoints - 1);

        for (var i = 1; i < maxPoints - 1; i++)
            compact.Add(result[(int)Math.Round(i * step)]);

        compact.Add(result[^1]);
        return compact;
    }

    private static void SimplifyRange(
        IReadOnlyList<MousePathPoint> points,
        int start,
        int end,
        double tolerance,
        bool[] keep)
    {
        if (end <= start + 1)
            return;

        var a = points[start];
        var b = points[end];
        var maxDistance = 0d;
        var maxIndex = -1;

        for (var i = start + 1; i < end; i++)
        {
            var distance = PerpendicularDistance(points[i], a, b);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                maxIndex = i;
            }
        }

        if (maxIndex < 0 || maxDistance <= tolerance)
            return;

        keep[maxIndex] = true;
        SimplifyRange(points, start, maxIndex, tolerance, keep);
        SimplifyRange(points, maxIndex, end, tolerance, keep);
    }

    private static double PerpendicularDistance(
        MousePathPoint p,
        MousePathPoint a,
        MousePathPoint b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;

        if (Math.Abs(dx) < double.Epsilon && Math.Abs(dy) < double.Epsilon)
            return Math.Sqrt(Math.Pow(p.X - a.X, 2) + Math.Pow(p.Y - a.Y, 2));

        var twiceArea = Math.Abs(
            dx * (a.Y - p.Y) -
            (a.X - p.X) * dy);

        return twiceArea / Math.Sqrt(dx * dx + dy * dy);
    }
}
