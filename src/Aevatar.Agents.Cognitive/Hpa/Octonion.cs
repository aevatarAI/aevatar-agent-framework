using System.Runtime.CompilerServices;

namespace Aevatar.Agents.Cognitive.Hpa;

// ============================================================
//  Octonion - Minimal usable implementation
//
//  Design goals:
//  - deterministic: Same input must yield same output (for reasoning system's "geometric evidence layer")
//  - Small and straightforward: Only implement operations we need (multiplication / conjugate / norm / associator)
//
//  Multiplication convention:
//  - Basis: 1, e1..e7
//  - e_i^2 = -1 (i=1..7)
//  - Multiplication defined by Fano plane directed triples (consistent with paper examples):
//      (1,2,3), (1,4,5), (1,7,6), (2,4,6),
//      (2,5,7), (3,4,7), (3,6,5)
// ============================================================

public readonly struct Octonion
{
    public double A0 { get; } // real
    public double A1 { get; }
    public double A2 { get; }
    public double A3 { get; }
    public double A4 { get; }
    public double A5 { get; }
    public double A6 { get; }
    public double A7 { get; }

    public Octonion(
        double a0, double a1, double a2, double a3,
        double a4, double a5, double a6, double a7)
    {
        A0 = a0;
        A1 = a1;
        A2 = a2;
        A3 = a3;
        A4 = a4;
        A5 = a5;
        A6 = a6;
        A7 = a7;
    }

    public static Octonion Zero => new(0, 0, 0, 0, 0, 0, 0, 0);
    public static Octonion One => new(1, 0, 0, 0, 0, 0, 0, 0);

    public Octonion Conjugate() => new(A0, -A1, -A2, -A3, -A4, -A5, -A6, -A7);

    public double NormSquared()
        => A0 * A0 + A1 * A1 + A2 * A2 + A3 * A3 + A4 * A4 + A5 * A5 + A6 * A6 + A7 * A7;

    public double Norm() => Math.Sqrt(NormSquared());

    public Octonion Normalize()
    {
        var n = Norm();
        if (n <= 0 || double.IsNaN(n) || double.IsInfinity(n))
            return One;
        return this / n;
    }

    public double[] ToArray()
        => [A0, A1, A2, A3, A4, A5, A6, A7];

    // ============================================================
    //  Operators
    // ============================================================

    public static Octonion operator +(Octonion x, Octonion y)
        => new(
            x.A0 + y.A0, x.A1 + y.A1, x.A2 + y.A2, x.A3 + y.A3,
            x.A4 + y.A4, x.A5 + y.A5, x.A6 + y.A6, x.A7 + y.A7);

    public static Octonion operator -(Octonion x, Octonion y)
        => new(
            x.A0 - y.A0, x.A1 - y.A1, x.A2 - y.A2, x.A3 - y.A3,
            x.A4 - y.A4, x.A5 - y.A5, x.A6 - y.A6, x.A7 - y.A7);

    public static Octonion operator *(Octonion x, double s)
        => new(
            x.A0 * s, x.A1 * s, x.A2 * s, x.A3 * s,
            x.A4 * s, x.A5 * s, x.A6 * s, x.A7 * s);

    public static Octonion operator /(Octonion x, double s)
        => x * (1.0 / s);

    public static Octonion operator *(Octonion x, Octonion y) => Multiply(x, y);

    // ============================================================
    //  Associator A(x,y,z) = (xy)z - x(yz)
    // ============================================================

    public static Octonion Associator(Octonion x, Octonion y, Octonion z)
        => (x * y) * z - x * (y * z);

    // ============================================================
    //  Multiplication (bilinear expansion on basis table)
    // ============================================================

    // sign[idx] ∈ { -1, +1 }, basis[idx] ∈ [0..7]
    private static readonly sbyte[] MulSign;
    private static readonly byte[] MulBasis;

    static Octonion()
    {
        MulSign = new sbyte[8 * 8];
        MulBasis = new byte[8 * 8];
        BuildMulTable(MulSign, MulBasis);
    }

    private static Octonion Multiply(Octonion x, Octonion y)
    {
        Span<double> xv = stackalloc double[8];
        Span<double> yv = stackalloc double[8];
        Span<double> rv = stackalloc double[8];

        xv[0] = x.A0; xv[1] = x.A1; xv[2] = x.A2; xv[3] = x.A3;
        xv[4] = x.A4; xv[5] = x.A5; xv[6] = x.A6; xv[7] = x.A7;

        yv[0] = y.A0; yv[1] = y.A1; yv[2] = y.A2; yv[3] = y.A3;
        yv[4] = y.A4; yv[5] = y.A5; yv[6] = y.A6; yv[7] = y.A7;

        for (var i = 0; i < 8; i++)
        {
            var xi = xv[i];
            if (xi == 0) continue;

            var row = i * 8;
            for (var j = 0; j < 8; j++)
            {
                var yj = yv[j];
                if (yj == 0) continue;

                var idx = row + j;
                var s = MulSign[idx];
                var k = MulBasis[idx];
                if (s == 0)
                {
                    // If multiplication table is missing entries, it means the Fano triple filling above is incorrect
                    throw new InvalidOperationException($"Octonion mul table missing entry: e{i}*e{j}");
                }
                rv[k] += s * xi * yj;
            }
        }

        return new Octonion(
            rv[0], rv[1], rv[2], rv[3],
            rv[4], rv[5], rv[6], rv[7]);
    }

    // ============================================================
    //  Table builder
    // ============================================================

    private static void BuildMulTable(sbyte[] sign, byte[] basis)
    {
        // 1) Identity: 1*e_j = e_j, e_i*1 = e_i
        for (byte j = 0; j < 8; j++)
        {
            Set(0, j, +1, j, sign, basis);
            Set(j, 0, +1, j, sign, basis);
        }

        // 2) Imaginary squares: e_i*e_i = -1
        for (byte i = 1; i < 8; i++)
        {
            Set(i, i, -1, 0, sign, basis);
        }

        // 3) Fano cycles (orientation matters)
        //    For (a,b,c): a*b=c, b*c=a, c*a=b, and reversed order is negative.
        var cycles = new[]
        {
            new byte[] { 1, 2, 3 },
            new byte[] { 1, 4, 5 },
            new byte[] { 1, 7, 6 },
            new byte[] { 2, 4, 6 },
            new byte[] { 2, 5, 7 },
            new byte[] { 3, 4, 7 },
            new byte[] { 3, 6, 5 }
        };

        foreach (var t in cycles)
        {
            var a = t[0];
            var b = t[1];
            var c = t[2];

            // forward
            Set(a, b, +1, c, sign, basis);
            Set(b, c, +1, a, sign, basis);
            Set(c, a, +1, b, sign, basis);

            // reverse
            Set(b, a, -1, c, sign, basis);
            Set(c, b, -1, a, sign, basis);
            Set(a, c, -1, b, sign, basis);
        }

        // 4) Validate completeness: every pair must have a rule
        for (byte i = 0; i < 8; i++)
        for (byte j = 0; j < 8; j++)
        {
            var idx = i * 8 + j;
            if (sign[idx] == 0)
                throw new InvalidOperationException($"Octonion mul table incomplete at e{i}*e{j}");
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Set(byte i, byte j, sbyte s, byte k, sbyte[] sign, byte[] basis)
    {
        var idx = i * 8 + j;
        sign[idx] = s;
        basis[idx] = k;
    }
}

