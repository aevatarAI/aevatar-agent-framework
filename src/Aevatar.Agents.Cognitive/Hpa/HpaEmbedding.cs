using System.Security.Cryptography;
using System.Text;

namespace Aevatar.Agents.Cognitive.Hpa;

// ============================================================
//  HPA Embedding (Complex Phase + Octonion Lift) - deterministic mapping
//
//  Goals:
//  - Treat "proposition dependency set depends_on" as generator list (factorization)
//  - Compute at token-free layer:
//      ρ: radial (complexity/cost)
//      θ×: multiplicative phase
//      Z: ρ·e^{iθ} (complex plane embedding)
//      U: S^7 unit octonion (octonion phase)
//
//  Important:
//  - Not pursuing "physical correctness" here, pursuing "protocol consistency + reproducible + measurable".
//  - Real semantics provided by LLM/proof system; HPA layer only provides geometric evidence and scheduling signals.
// ============================================================

public enum HpaBetaModel
{
    LogPhase,
    OmegaPhase,
    RandomPrimePhase
}

public sealed record HpaEmbeddingConfig
{
    public HpaBetaModel BetaModel { get; init; } = HpaBetaModel.RandomPrimePhase;

    // β(p)=β0·log(p) (paper minimal family)
    public double Beta0 { get; init; } = 4.0;

    // θ×(n)=β1·Ω(n) (paper example family)
    public double Beta1 { get; init; } = 2.0;

    // ρ=exp(sum w(id)) with w(id)=wBase+wScale·u(id)
    public double RadialWBase { get; init; } = 0.12;
    public double RadialWScale { get; init; } = 0.38;

    // Deterministic hash seed (changes geometry but keeps reproducible)
    public int Seed { get; init; } = 0;
}

public static class HpaEmbedding
{
    private const double TwoPi = 2.0 * Math.PI;

    // Cache: id -> unit octonion
    private static readonly Dictionary<(string id, int seed), Octonion> UnitCache = new();

    public static HpaBetaModel ParseBetaModel(string? raw)
    {
        var s = (raw ?? "").Trim().ToLowerInvariant();
        return s switch
        {
            "log_phase" or "log" => HpaBetaModel.LogPhase,
            "omega_phase" or "omega" => HpaBetaModel.OmegaPhase,
            "random_prime_phase" or "random" or "rnd" => HpaBetaModel.RandomPrimePhase,
            _ => HpaBetaModel.RandomPrimePhase
        };
    }

    public static Dictionary<string, object> EmbedToValue(
        IEnumerable<string> dependsOn,
        IEnumerable<string>? factorSequence,
        HpaEmbeddingConfig cfg)
    {
        var embed = Embed(dependsOn, factorSequence, cfg);
        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["log_rho"] = embed.LogRho,
            ["rho"] = embed.Rho,
            ["theta"] = embed.Theta,
            ["z_re"] = embed.ZRe,
            ["z_im"] = embed.ZIm,
            ["u_oct"] = embed.U.ToArray(),
            ["factors"] = embed.Factors
        };
    }

    public static HpaNodeEmbedding Embed(
        IEnumerable<string> dependsOn,
        IEnumerable<string>? factorSequence,
        HpaEmbeddingConfig cfg)
    {
        var factors = NormalizeFactorSequence(dependsOn, factorSequence);
        if (factors.Count == 0)
        {
            // Empty generator: treat it as 1 (identity element)
            return new HpaNodeEmbedding(
                LogRho: 0.0,
                Rho: 1.0,
                Theta: 0.0,
                ZRe: 1.0,
                ZIm: 0.0,
                U: Octonion.One,
                Factors: factors);
        }

        var logRho = 0.0;
        var theta = 0.0;

        var u = Octonion.One;
        foreach (var f in factors)
        {
            logRho += WeightForId(f, cfg);
            theta += BetaForId(f, cfg);
            u = u * UnitOctonionForId(f, cfg);
        }

        theta = Mod2Pi(theta);

        // ρ = exp(logρ), avoid overflow
        var rho = SafeExp(logRho);

        // Z = ρ·e^{iθ}
        var zRe = rho * Math.Cos(theta);
        var zIm = rho * Math.Sin(theta);

        // U in S^7 (numerical safety)
        u = u.Normalize();

        return new HpaNodeEmbedding(
            LogRho: logRho,
            Rho: rho,
            Theta: theta,
            ZRe: zRe,
            ZIm: zIm,
            U: u,
            Factors: factors);
    }

    public static double BetaForId(string id, HpaEmbeddingConfig cfg)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return 0.0;

        return cfg.BetaModel switch
        {
            // β(id) = β0·log(1+hash) mod 2π
            HpaBetaModel.LogPhase => cfg.Beta0 * Math.Log(1.0 + HashToPositiveInt(id, cfg.Seed)),

            // θ× ~ β1·Ω: for abstract generators, treat each factor as Ω=1
            HpaBetaModel.OmegaPhase => cfg.Beta1,

            // β(id) ∈ [0,2π) uniform-ish from hash
            _ => TwoPi * HashToUnit01(id, cfg.Seed)
        };
    }

    public static double WeightForId(string id, HpaEmbeddingConfig cfg)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return 0.0;

        // w(id) = wBase + wScale·u, with u∈[0,1)
        var u = HashToUnit01(id, cfg.Seed ^ 0x51C0_17);
        var w = cfg.RadialWBase + cfg.RadialWScale * u;
        return w < 0 ? 0 : w;
    }

    public static Octonion UnitOctonionForId(string id, HpaEmbeddingConfig cfg)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return Octonion.One;

        var key = (id, cfg.Seed);
        lock (UnitCache)
        {
            if (UnitCache.TryGetValue(key, out var cached))
                return cached;
        }

        // 32 bytes -> 8 uint32 -> [-1,1]
        var bytes = Sha256Bytes($"{cfg.Seed}:{id}");
        Span<double> v = stackalloc double[8];
        for (var i = 0; i < 8; i++)
        {
            var u32 = BitConverter.ToUInt32(bytes, i * 4);
            var x = u32 / (double)uint.MaxValue; // [0,1]
            v[i] = (x * 2.0) - 1.0;              // [-1,1]
        }

        var o = new Octonion(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]).Normalize();
        lock (UnitCache)
        {
            UnitCache[key] = o;
        }
        return o;
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private static List<string> NormalizeFactorSequence(IEnumerable<string> dependsOn, IEnumerable<string>? factorSequence)
    {
        var seq = new List<string>();
        if (factorSequence != null)
        {
            foreach (var raw in factorSequence)
            {
                var s = (raw ?? "").Trim();
                if (s.Length > 0) seq.Add(s);
            }
        }

        if (seq.Count > 0) return seq;

        // Fallback: stable order (ordinal sort) to keep deterministic
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in dependsOn)
        {
            var s = (raw ?? "").Trim();
            if (s.Length == 0) continue;
            if (set.Add(s)) seq.Add(s);
        }

        seq.Sort(StringComparer.Ordinal);
        return seq;
    }

    private static double Mod2Pi(double theta)
    {
        if (double.IsNaN(theta) || double.IsInfinity(theta)) return 0.0;
        theta %= TwoPi;
        if (theta < 0) theta += TwoPi;
        return theta;
    }

    private static double SafeExp(double x)
    {
        // exp(700) ~ 1e304, still finite in double. Keep a conservative clamp.
        if (x > 700) x = 700;
        if (x < -700) x = -700;
        return Math.Exp(x);
    }

    private static double HashToUnit01(string id, int seed)
    {
        var bytes = Sha256Bytes($"{seed}:{id}");
        var u64 = BitConverter.ToUInt64(bytes, 0);
        // Map to [0,1)
        return (u64 >> 11) * (1.0 / (1UL << 53));
    }

    private static long HashToPositiveInt(string id, int seed)
    {
        var bytes = Sha256Bytes($"{seed}:pos:{id}");
        var u64 = BitConverter.ToUInt64(bytes, 0);
        // Avoid 0 (log)
        return 1 + (long)(u64 % 1_000_000_000UL);
    }

    private static byte[] Sha256Bytes(string s)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(s));
    }
}

public sealed record HpaNodeEmbedding(
    double LogRho,
    double Rho,
    double Theta,
    double ZRe,
    double ZIm,
    Octonion U,
    List<string> Factors);

