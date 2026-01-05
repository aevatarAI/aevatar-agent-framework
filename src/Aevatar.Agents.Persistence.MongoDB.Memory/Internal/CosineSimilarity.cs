namespace Aevatar.Agents.Persistence.MongoDB.Memory.Internal;

internal static class CosineSimilarity
{
    internal static double Compute(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count == 0 || b.Count == 0 || a.Count != b.Count)
        {
            return 0d;
        }

        double dot = 0;
        double na = 0;
        double nb = 0;

        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            dot += x * y;
            na += x * x;
            nb += y * y;
        }

        if (na <= 0 || nb <= 0)
        {
            return 0d;
        }

        return dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}



