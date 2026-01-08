using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aevatar.Agents.AI.Tool.Abstractions;
using Aevatar.Agents.AI.Tool.Tools.CustomTools;
using Aevatar.Agents.Core;
using Aevatar.Trade.Tools;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Trade.Agents.AiWars;

/// <summary>
/// AI Wars log uploader agent.
///
/// Data flow:
/// - Receives <see cref="AiWarsLogUploadRequestedEvent"/> (typically from TradeAuditAgent)
/// - Executes dotnet-file skill: weex_ai_order_upload_ai_log.cs (POST /capi/v2/order/uploadAiLog)
/// - Publishes success/failure events upward for auditing
/// </summary>
public sealed class AiWarsLogUploaderAgent : GAgentBase<AiWarsUploaderState>
{
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        State.AgentId = Id.ToString();
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(
            $"AiWarsUploader: ok={State.UploadsSucceeded}, fail={State.UploadsFailed}, last={State.LastUploadTime?.ToDateTime():O}");
    }

    [Aevatar.Agents.Abstractions.Attributes.EventHandler]
    public async Task HandleUploadRequestedAsync(AiWarsLogUploadRequestedEvent evt)
    {
        State.UploadsAttempted++;

        JsonElement? payloadForReceipt = null;

        try
        {
            if (string.IsNullOrWhiteSpace(evt.ArtifactPath) || !File.Exists(evt.ArtifactPath))
            {
                await WriteReceiptAsync(
                    evt,
                    payload: null,
                    toolResult: null,
                    ok: false,
                    errorCode: "FILE_NOT_FOUND",
                    errorMessage: $"Artifact not found: {evt.ArtifactPath}");
                await PublishFailedAsync(evt, "FILE_NOT_FOUND", $"Artifact not found: {evt.ArtifactPath}");
                return;
            }

            var fileName = Path.GetFileName(evt.ArtifactPath);
            var payloadJson = await File.ReadAllTextAsync(evt.ArtifactPath);

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            var root = doc.RootElement;
            payloadForReceipt = root.Clone();

            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("orderId", out var orderIdEl) && orderIdEl.ValueKind == JsonValueKind.Number && orderIdEl.TryGetInt64(out var oid))
                parameters["orderId"] = oid;
            parameters["stage"] = root.TryGetProperty("stage", out var stageEl) ? stageEl.GetString() ?? "" : "";
            parameters["model"] = root.TryGetProperty("model", out var modelEl) ? modelEl.GetString() ?? "" : "";
            parameters["input"] = root.TryGetProperty("input", out var inputEl) ? inputEl.Clone() : default(JsonElement);
            parameters["output"] = root.TryGetProperty("output", out var outputEl) ? outputEl.Clone() : default(JsonElement);
            parameters["explanation"] = root.TryGetProperty("explanation", out var expEl) ? expEl.GetString() ?? "" : "";

            // Execute dotnet-file skill (reads WEEX_* from env).
            var tool = await DotNetFileSkillTool.LoadFromFileAsync(TradeDotNetSkillPaths.WeexAiWarsUploadAiLog, logger: Logger);
            var context = new ToolContext
            {
                AgentId = Id.ToString(),
                AgentType = GetType().FullName ?? GetType().Name,
                Logger = Logger
            };

            var resultMsg = await tool.ExecuteAsync(parameters, context, Logger, CancellationToken.None);
            var result = resultMsg as Struct;

            State.LastUploadTime = Timestamp.FromDateTime(DateTime.UtcNow);

            var ok = result != null &&
                     result.Fields.TryGetValue("success", out var okVal) &&
                     okVal.KindCase == Value.KindOneofCase.BoolValue &&
                     okVal.BoolValue;

            if (ok)
            {
                State.UploadsSucceeded++;
                Logger.LogInformation("[AiWarsUploader] Upload succeeded: {File}", fileName);

                await WriteReceiptAsync(evt, payloadForReceipt, resultMsg, ok: true, errorCode: null, errorMessage: null);

                await PublishAsync(new AiWarsLogUploadSucceededEvent
                {
                    RequestId = evt.RequestId,
                    CycleId = evt.CycleId,
                    RemoteId = string.Empty,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                }, Aevatar.Agents.EventDirection.Up);
            }
            else
            {
                State.UploadsFailed++;
                State.LastError = "UPLOAD_FAILED";

                // Best-effort extract stderr/text
                var stderr = "";
                if (result != null &&
                    result.Fields.TryGetValue("stderr", out var errVal) &&
                    errVal.KindCase == Value.KindOneofCase.StringValue)
                {
                    stderr = errVal.StringValue ?? "";
                }

                await WriteReceiptAsync(
                    evt,
                    payloadForReceipt,
                    resultMsg,
                    ok: false,
                    errorCode: "UPLOAD_FAILED",
                    errorMessage: string.IsNullOrWhiteSpace(stderr) ? "Upload failed." : stderr);

                await PublishFailedAsync(evt, "UPLOAD_FAILED", string.IsNullOrWhiteSpace(stderr) ? "Upload failed." : stderr);
            }
        }
        catch (Exception ex)
        {
            State.UploadsFailed++;
            State.LastError = ex.Message;
            Logger.LogError(ex, "[AiWarsUploader] Upload exception");

            await WriteReceiptAsync(
                evt,
                payloadForReceipt,
                toolResult: null,
                ok: false,
                errorCode: "CLIENT_ERROR",
                errorMessage: ex.Message);

            await PublishFailedAsync(evt, "CLIENT_ERROR", ex.Message);
        }
    }

    private async Task PublishFailedAsync(AiWarsLogUploadRequestedEvent req, string code, string message)
    {
        await PublishAsync(new AiWarsLogUploadFailedEvent
        {
            RequestId = req.RequestId,
            CycleId = req.CycleId,
            ErrorCode = code,
            ErrorMessage = message,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        }, Aevatar.Agents.EventDirection.Up);
    }

    private async Task WriteReceiptAsync(
        AiWarsLogUploadRequestedEvent req,
        JsonElement? payload,
        IMessage? toolResult,
        bool ok,
        string? errorCode,
        string? errorMessage)
    {
        try
        {
            // Place receipts next to the upload artifacts so TradeAudit can reference them deterministically.
            var aiwarsDir = Path.GetDirectoryName(req.ArtifactPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(aiwarsDir))
                return;

            var receiptsDir = Path.Combine(aiwarsDir, "receipts");
            Directory.CreateDirectory(receiptsDir);

            var receiptPath = Path.Combine(receiptsDir, $"aiwars_receipt_{req.RequestId}.json");

            JsonElement? toolObj = null;
            if (toolResult != null)
            {
                using var toolDoc = JsonDocument.Parse(JsonFormatter.Default.Format(toolResult));
                toolObj = toolDoc.RootElement.Clone();
            }

            var receipt = new
            {
                requestId = req.RequestId,
                cycleId = req.CycleId,
                artifactPath = req.ArtifactPath,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                uploaderAgentId = State.AgentId,
                ok,
                error = string.IsNullOrWhiteSpace(errorCode)
                    ? null
                    : new { code = errorCode, message = errorMessage ?? "" },
                payload,
                toolResult = toolObj
            };

            var json = JsonSerializer.Serialize(receipt, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            await File.WriteAllTextAsync(receiptPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[AiWarsUploader] Failed to write upload receipt");
        }
    }
}


