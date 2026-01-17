using Tui.Core.Primitives;

namespace Tui.Core.Layout;

public enum FlexDirection
{
    Row = 0,
    Column = 1
}

public enum CrossAlignment
{
    Start = 0,
    Center = 1,
    End = 2,
    Stretch = 3
}

public readonly struct FlexItem
{
    public int Basis { get; }
    public int Grow { get; }
    public int Shrink { get; }
    public int Min { get; }
    public int Max { get; }
    public int CrossSize { get; }
    public CrossAlignment Align { get; }

    public FlexItem(
        int basis,
        int grow = 0,
        int shrink = 1,
        int min = 0,
        int max = int.MaxValue,
        int crossSize = 0,
        CrossAlignment align = CrossAlignment.Stretch)
    {
        Basis = basis;
        Grow = grow;
        Shrink = shrink;
        Min = min;
        Max = max;
        CrossSize = crossSize;
        Align = align;
    }
}

public static class FlexLayout
{
    public static IReadOnlyList<Rect> Layout(Rect container, FlexDirection direction, IReadOnlyList<FlexItem> items)
    {
        if (items.Count == 0)
            return Array.Empty<Rect>();

        var mainSize = direction == FlexDirection.Row ? container.Width : container.Height;
        var crossSize = direction == FlexDirection.Row ? container.Height : container.Width;
        var sizes = new int[items.Count];
        var total = 0;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sizes[i] = Clamp(item.Basis, item.Min, item.Max);
            total += sizes[i];
        }

        if (total < mainSize)
            Grow(items, sizes, mainSize - total);
        else if (total > mainSize)
            Shrink(items, sizes, total - mainSize);

        var rects = new Rect[items.Count];
        var cursor = direction == FlexDirection.Row ? container.X : container.Y;

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var cross = ResolveCrossSize(crossSize, item);
            var crossPos = ResolveCrossPosition(container, direction, cross, item.Align);
            rects[i] = direction == FlexDirection.Row
                ? new Rect(cursor, crossPos, sizes[i], cross)
                : new Rect(crossPos, cursor, cross, sizes[i]);
            cursor += sizes[i];
        }

        return rects;
    }

    private static int ResolveCrossSize(int containerCross, FlexItem item)
    {
        if (item.Align == CrossAlignment.Stretch)
            return containerCross;
        if (item.CrossSize <= 0)
            return containerCross;
        return Math.Min(item.CrossSize, containerCross);
    }

    private static int ResolveCrossPosition(Rect container, FlexDirection direction, int crossSize, CrossAlignment alignment)
    {
        var crossStart = direction == FlexDirection.Row ? container.Y : container.X;
        var crossTotal = direction == FlexDirection.Row ? container.Height : container.Width;

        return alignment switch
        {
            CrossAlignment.Center => crossStart + (crossTotal - crossSize) / 2,
            CrossAlignment.End => crossStart + (crossTotal - crossSize),
            _ => crossStart
        };
    }

    private static void Grow(IReadOnlyList<FlexItem> items, int[] sizes, int extra)
    {
        var growIndices = new List<int>();
        var growSum = 0;

        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Grow > 0)
            {
                growIndices.Add(i);
                growSum += items[i].Grow;
            }
        }

        if (growSum == 0)
            return;

        var remaining = extra;
        for (var i = 0; i < growIndices.Count; i++)
        {
            var idx = growIndices[i];
            var share = (int)Math.Floor(extra * (items[idx].Grow / (double)growSum));
            var target = sizes[idx] + share;
            var clamped = Math.Min(target, items[idx].Max);
            var applied = clamped - sizes[idx];
            sizes[idx] += applied;
            remaining -= applied;
        }

        for (var i = 0; remaining > 0 && i < growIndices.Count; i++)
        {
            var idx = growIndices[i];
            if (sizes[idx] < items[idx].Max)
            {
                sizes[idx]++;
                remaining--;
            }
        }
    }

    private static void Shrink(IReadOnlyList<FlexItem> items, int[] sizes, int deficit)
    {
        var shrinkable = new List<int>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Shrink > 0 && sizes[i] > items[i].Min)
                shrinkable.Add(i);
        }

        var remaining = deficit;
        while (remaining > 0 && shrinkable.Count > 0)
        {
            var weightSum = 0;
            foreach (var idx in shrinkable)
                weightSum += items[idx].Shrink;

            if (weightSum == 0)
                break;

            var anyReduced = false;
            for (var i = shrinkable.Count - 1; i >= 0 && remaining > 0; i--)
            {
                var idx = shrinkable[i];
                var weight = items[idx].Shrink;
                var share = (int)Math.Ceiling(remaining * (weight / (double)weightSum));
                var maxReduce = sizes[idx] - items[idx].Min;
                var reduce = Math.Min(share, maxReduce);

                if (reduce <= 0)
                {
                    shrinkable.RemoveAt(i);
                    continue;
                }

                sizes[idx] -= reduce;
                remaining -= reduce;
                anyReduced = true;

                if (sizes[idx] <= items[idx].Min)
                    shrinkable.RemoveAt(i);
            }

            if (!anyReduced)
                break;
        }
    }

    private static int Clamp(int value, int min, int max)
        => Math.Min(Math.Max(value, min), max);
}
