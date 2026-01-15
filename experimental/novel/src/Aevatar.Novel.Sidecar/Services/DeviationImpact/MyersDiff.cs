namespace Aevatar.Novel.Sidecar.Services.DeviationImpact;

// ============================================================
//  MyersDiff (v1)
//
//  - Minimal Myers diff for line arrays.
//  - Produces an edit script of Equal/Insert/Delete operations.
//
//  REF:
//  - Eugene W. Myers, "An O(ND) Difference Algorithm and Its Variations"
// ============================================================

public static class MyersDiff
{
    public enum OpKind
    {
        Equal,
        Insert,
        Delete
    }

    public sealed record Op(OpKind Kind, string Text);

    public static IReadOnlyList<Op> DiffLines(string[] a, string[] b, int maxLinesForMyers = 20_000)
    {
        // Safety: for extremely large inputs, fall back to a coarse diff.
        if (a.Length + b.Length > maxLinesForMyers)
        {
            return CoarseDiff(a, b);
        }

        var n = a.Length;
        var m = b.Length;
        var max = n + m;

        var offset = max;
        var v = new int[2 * max + 1];
        var trace = new List<int[]>(max + 1);

        for (var i = 0; i < v.Length; i++) v[i] = 0;

        for (var d = 0; d <= max; d++)
        {
            trace.Add((int[])v.Clone());

            for (var k = -d; k <= d; k += 2)
            {
                var kIndex = k + offset;

                int x;
                if (k == -d || (k != d && v[kIndex - 1] < v[kIndex + 1]))
                {
                    x = v[kIndex + 1]; // down (insert)
                }
                else
                {
                    x = v[kIndex - 1] + 1; // right (delete)
                }

                var y = x - k;

                while (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    x++;
                    y++;
                }

                v[kIndex] = x;

                if (x >= n && y >= m)
                {
                    return Backtrack(trace, a, b, offset, n, m);
                }
            }
        }

        return Backtrack(trace, a, b, offset, n, m);
    }

    private static IReadOnlyList<Op> Backtrack(List<int[]> trace, string[] a, string[] b, int offset, int n, int m)
    {
        var ops = new List<Op>(n + m);

        var x = n;
        var y = m;

        for (var d = trace.Count - 1; d >= 0; d--)
        {
            var v = trace[d];
            var k = x - y;

            var prevK = 0;
            if (d == 0)
            {
                prevK = 0;
            }
            else if (k == -d || (k != d && v[k - 1 + offset] < v[k + 1 + offset]))
            {
                prevK = k + 1;
            }
            else
            {
                prevK = k - 1;
            }

            var prevX = v[prevK + offset];
            var prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                x--;
                y--;
                ops.Add(new Op(OpKind.Equal, a[x]));
            }

            if (d == 0)
                break;

            if (x == prevX)
            {
                // insertion
                y--;
                ops.Add(new Op(OpKind.Insert, b[y]));
            }
            else
            {
                // deletion
                x--;
                ops.Add(new Op(OpKind.Delete, a[x]));
            }
        }

        ops.Reverse();
        return ops;
    }

    private static IReadOnlyList<Op> CoarseDiff(string[] a, string[] b)
    {
        // Very coarse: common prefix/suffix, treat middle as replace.
        var i = 0;
        while (i < a.Length && i < b.Length && string.Equals(a[i], b[i], StringComparison.Ordinal))
            i++;

        var ai = a.Length - 1;
        var bi = b.Length - 1;
        while (ai >= i && bi >= i && string.Equals(a[ai], b[bi], StringComparison.Ordinal))
        {
            ai--;
            bi--;
        }

        var ops = new List<Op>();
        for (var p = 0; p < i; p++) ops.Add(new Op(OpKind.Equal, a[p]));
        for (var p = i; p <= ai; p++) ops.Add(new Op(OpKind.Delete, a[p]));
        for (var p = i; p <= bi; p++) ops.Add(new Op(OpKind.Insert, b[p]));
        for (var p = ai + 1; p < a.Length; p++) ops.Add(new Op(OpKind.Equal, a[p]));
        return ops;
    }
}


