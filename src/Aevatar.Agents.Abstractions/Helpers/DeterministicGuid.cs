using System.Security.Cryptography;
using System.Text;

namespace Aevatar.Agents.Abstractions.Helpers;

// ============================================================
//  Deterministic Guid
//  Deterministic Guid (for "same session => same set of ActorIds")
//
//  WHY:
//  - Frontend needs "stateless refresh": after refresh, server must be able to stably locate the same set of Agents
//  - Local/Orleans/ProtoActor runtimes all use Guid as Actor key
//  - We use hash(name) -> Guid approach to avoid maintaining additional mapping tables at outer layer
// ============================================================
public static class DeterministicGuid
{
    /// <summary>
    /// Create a deterministic Guid from an input string.
    /// </summary>
    public static Guid FromString(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("input is required", nameof(input));

        // NOTE:
        // - SHA256 is stable and widely available.
        // - We only take the first 16 bytes as Guid payload.
        // - Keep the algorithm in ONE place so all modules use identical IDs.
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input.Trim()));

        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);

        // Set version/variant bits (best-effort; not strictly required but helps debugging).
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50); // version 5-ish
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80); // RFC 4122 variant

        return new Guid(guidBytes);
    }
}

