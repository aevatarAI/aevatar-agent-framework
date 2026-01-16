using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

// CalculatorAgentState is defined in demo_messages.proto

/// <summary>
/// Calculator Agent interface for RPC
/// </summary>
public interface ICalculatorAgent
{
    Task<double> AddAsync(double a, double b, CancellationToken ct = default);
    Task<double> SubtractAsync(double a, double b, CancellationToken ct = default);
    Task<double> MultiplyAsync(double a, double b, CancellationToken ct = default);
    Task<double> DivideAsync(double a, double b, CancellationToken ct = default);
    double GetLastResult();
    int GetOperationCount();
}

/// <summary>
/// Example: Calculator agent
/// </summary>
public class CalculatorAgent : GAgentBase<CalculatorAgentState>, ICalculatorAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Calculator Agent - Performs basic arithmetic operations");
    }

    /// <summary>
    /// Addition operation
    /// </summary>
    public async Task<double> AddAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a + b;
        await RecordOperation($"{a} + {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// Subtraction operation
    /// </summary>
    public async Task<double> SubtractAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a - b;
        await RecordOperation($"{a} - {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// Multiplication operation
    /// </summary>
    public async Task<double> MultiplyAsync(double a, double b, CancellationToken ct = default)
    {
        var result = a * b;
        await RecordOperation($"{a} × {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// Division operation
    /// </summary>
    public async Task<double> DivideAsync(double a, double b, CancellationToken ct = default)
    {
        if (Math.Abs(b) < 0.0001)
            throw new DivideByZeroException("Divisor cannot be zero");

        var result = a / b;
        await RecordOperation($"{a} ÷ {b} = {result}", result, ct);
        return result;
    }

    /// <summary>
    /// Get calculation history
    /// </summary>
    public Google.Protobuf.Collections.RepeatedField<string> GetHistory() => State.History;

    /// <summary>
    /// Get last result
    /// </summary>
    public double GetLastResult() => State.LastResult;

    /// <summary>
    /// Get operation count
    /// </summary>
    public int GetOperationCount() => State.OperationCount;

    private async Task RecordOperation(string operation, double result, CancellationToken ct)
    {
        State.LastResult = result;
        State.OperationCount++;
        State.History.Add($"[{State.OperationCount}] {operation}");

        Console.WriteLine($"[CalculatorAgent] Calculation completed: {operation}");

        // Persist state to MongoDB (non-EventSourcing mode)
        if (StateStore != null)
        {
            await StateStore.SaveAsync(Id, State, ct);
            Console.WriteLine($"[CalculatorAgent] State persisted");
        }
    }
}
