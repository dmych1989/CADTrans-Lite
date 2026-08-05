// Services/AiTextFilter.cs
// AI text filter — uses an OpenAI-compatible API to judge whether each text needs translation.
// Reuses the same API format as CustomAiTranslator (shared BaseUrl/ApiKey configuration).

using System.Net.Http;
using System.Text;
using System.Text.Json;
using CADTransLite.Core.Models;

namespace CADTransLite.Core.Services;

/// <summary>
/// AI 文本过滤器 — 使用 AI 判断文本是否需要翻译。
/// 复用 OpenAI 兼容 API 格式（与 CustomAiTranslator 共享 BaseUrl/ApiKey 配置）。
/// </summary>
public sealed class AiTextFilter
{
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _modelName;
    private readonly string _filterPrompt;
    private readonly int _batchSize;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Default prompt template used when <see cref="_filterPrompt"/> is empty.
    /// Supports placeholders: {sourceLang}, {targetLang}, {texts}.
    /// </summary>
    private const string DefaultPromptTemplate =
        @"You are a CAD drawing text filter. Judge whether each text needs translation from {sourceLang} to {targetLang}.

Rules:
- KEEP: Text that contains meaningful words, descriptions, labels, notes, or instructions that need translation
- SKIP: Text that is only dimensions (e.g. ""Ø12"", ""R5""), tolerances (e.g. ""±0.05""), pure numbers (e.g. ""3.5""), codes/standards (e.g. ""ISO 2768""), symbols, or short technical annotations that don't need translation

Return a JSON array where each element has:
- ""index"": the 0-based index of the text in the input list
- ""decision"": ""KEEP"" or ""SKIP""
- ""reason"": brief explanation

Texts to judge:
{texts}";

    /// <summary>
    /// Initializes a new instance of <see cref="AiTextFilter"/>.
    /// </summary>
    /// <param name="apiKey">API key for the OpenAI-compatible endpoint.</param>
    /// <param name="baseUrl">Base URL for the API (e.g. https://api.openai.com/v1).</param>
    /// <param name="modelName">Model name to use for filtering.</param>
    /// <param name="filterPrompt">Custom prompt template. Empty = use default template.</param>
    /// <param name="batchSize">Number of texts per API call. Default 50.</param>
    public AiTextFilter(string apiKey, string baseUrl, string modelName, string filterPrompt = "", int batchSize = 50)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _modelName = modelName;
        _filterPrompt = filterPrompt;
        _batchSize = batchSize > 0 ? batchSize : 50;

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    /// <summary>
    /// 对文本列表进行 AI 过滤判定。
    /// 对每个 TranslationItem 设置 AiFilterDecision ("KEEP" / "SKIP") 和 AiFilterReason。
    /// </summary>
    /// <param name="items">要判定的翻译条目列表。</param>
    /// <param name="sourceLang">源语言代码。</param>
    /// <param name="targetLang">目标语言代码。</param>
    /// <param name="protectTableHeaders">是否保护表格表头（自动 KEEP）。</param>
    /// <param name="progress">进度报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>过滤掉的条目数（SKIP 的数量）。</returns>
    public async Task<int> FilterAsync(
        List<TranslationItem> items,
        string sourceLang,
        string targetLang,
        bool protectTableHeaders = true,
        IProgress<(int current, int total, string message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Filter out items that don't need AI judgment
        var needJudgment = new List<(int listIndex, string text)>();

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // Skip empty text
            if (string.IsNullOrWhiteSpace(item.OriginalText))
            {
                item.AiFilterDecision = "SKIP";
                item.AiFilterReason = "空文本";
                continue;
            }

            // Skip items already filtered by DxfTextCleaner
            if (item.Status == "skipped")
            {
                item.AiFilterDecision = "SKIP";
                item.AiFilterReason = "已被清洗器过滤";
                continue;
            }

            // Protect table headers (row 0) — auto KEEP
            if (protectTableHeaders && item.EntityType == EntityType.TableCell && item.TableRow == 0)
            {
                item.AiFilterDecision = "KEEP";
                item.AiFilterReason = "表格表头（自动保留）";
                continue;
            }

            needJudgment.Add((i, item.OriginalText));
        }

        if (needJudgment.Count == 0)
        {
            return items.Count(it => it.AiFilterDecision == "SKIP");
        }

        // Step 2: Send in batches
        int totalBatches = (needJudgment.Count + _batchSize - 1) / _batchSize;
        int processedBatches = 0;

        for (int batchStart = 0; batchStart < needJudgment.Count; batchStart += _batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int batchEnd = Math.Min(batchStart + _batchSize, needJudgment.Count);
            var batch = needJudgment.GetRange(batchStart, batchEnd - batchStart);

            string prompt = BuildBatchPrompt(batch, sourceLang, targetLang);

            try
            {
                string aiResponse = await CallAiAsync(prompt, cancellationToken);
                var decisions = ParseAiResponse(aiResponse);

                // Map AI decisions back to TranslationItems
                foreach (var (batchIndex, decision, reason) in decisions)
                {
                    if (batchIndex >= 0 && batchIndex < batch.Count)
                    {
                        int listIndex = batch[batchIndex].listIndex;
                        items[listIndex].AiFilterDecision = decision;
                        items[listIndex].AiFilterReason = reason;
                    }
                }

                // Default to KEEP for items not returned by AI (safety: prefer over-translation over under-translation)
                foreach (var (listIndex, text) in batch)
                {
                    if (items[listIndex].AiFilterDecision == null)
                    {
                        items[listIndex].AiFilterDecision = "KEEP";
                        items[listIndex].AiFilterReason = "AI 未返回判定，默认保留";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // If AI call fails for this batch, mark all items in batch as KEEP (safety)
                foreach (var (listIndex, text) in batch)
                {
                    if (items[listIndex].AiFilterDecision == null)
                    {
                        items[listIndex].AiFilterDecision = "KEEP";
                        items[listIndex].AiFilterReason = "AI 调用失败，默认保留";
                    }
                }
            }

            processedBatches++;
            progress?.Report((processedBatches, totalBatches,
                $"AI过滤 {processedBatches}/{totalBatches} 批"));
        }

        // Step 3: Count SKIP items
        return items.Count(it => it.AiFilterDecision == "SKIP");
    }

    /// <summary>
    /// Builds the prompt for a single batch of texts.
    /// </summary>
    private string BuildBatchPrompt(List<(int listIndex, string text)> batch, string sourceLang, string targetLang)
    {
        string template = string.IsNullOrWhiteSpace(_filterPrompt) ? DefaultPromptTemplate : _filterPrompt;

        var sb = new StringBuilder();
        for (int i = 0; i < batch.Count; i++)
        {
            sb.AppendLine($"[{i}] {batch[i].text}");
        }

        return template
            .Replace("{sourceLang}", sourceLang)
            .Replace("{targetLang}", targetLang)
            .Replace("{texts}", sb.ToString());
    }

    /// <summary>
    /// Calls the AI API with the given prompt and returns the response content.
    /// Uses OpenAI-compatible chat completions format.
    /// </summary>
    private async Task<string> CallAiAsync(string prompt, CancellationToken ct)
    {
        var requestBody = new
        {
            model = _modelName,
            messages = new object[]
            {
                new { role = "system", content = "You are a precise text classification assistant. Always respond with valid JSON only." },
                new { role = "user", content = prompt },
            },
            temperature = 0.1,
            max_tokens = 4000,
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync(
            $"{_baseUrl.TrimEnd('/')}/chat/completions",
            content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"AI filter API error: {response.StatusCode}");

        var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
        return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    /// <summary>
    /// Parses the AI response and extracts filter decisions.
    /// Handles JSON wrapped in markdown code blocks or with extra text.
    /// If parsing fails, returns an empty list (caller should default to KEEP).
    /// </summary>
    private List<(int index, string decision, string reason)> ParseAiResponse(string aiResponse)
    {
        var results = new List<(int, string, string)>();

        // Try to extract JSON array from the response.
        // AI might wrap the JSON in ```json ... ``` or add extra text.
        string json = aiResponse.Trim();
        int startIdx = json.IndexOf('[');
        int endIdx = json.LastIndexOf(']');
        if (startIdx >= 0 && endIdx > startIdx)
        {
            json = json[startIdx..(endIdx + 1)];
        }
        else
        {
            // No JSON array found — return empty (all items will default to KEEP)
            return results;
        }

        try
        {
            var arr = JsonSerializer.Deserialize<JsonElement>(json);
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    int index = item.TryGetProperty("index", out var idxProp) ? idxProp.GetInt32() : -1;
                    string decision = item.TryGetProperty("decision", out var decProp) ? decProp.GetString() ?? "KEEP" : "KEEP";
                    string reason = item.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() ?? "" : "";
                    if (index >= 0)
                        results.Add((index, decision.ToUpperInvariant(), reason));
                }
            }
        }
        catch (JsonException)
        {
            // If JSON parsing fails, return empty list — caller will default all items to KEEP
        }

        return results;
    }
}
