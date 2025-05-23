using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IAN_Core
{
    public static class LLMHelpers
    {
        /// <summary>
        /// Query the LLM with a timeout and retry mechanism.
        /// </summary>
        public static async Task<string> QueryLLM(
            string systemRole,
            string userPrompt,
            double temperature = 0.7,
            int timeoutSeconds = 15,
            int maxRetries = 5)
        {
            int attempt = 0;
            Exception lastException = null;
            while (attempt < maxRetries)
            {
                attempt++;
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                    var task = QueryLLMCore(systemRole, userPrompt, temperature);
                    var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
                    if (completed == task)
                    {
                        return await task;
                    }
                    else
                    {
                        throw new TimeoutException($"LLM query timed out after {timeoutSeconds} seconds (attempt {attempt}).");
                    }
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Log.Write("LLM", $"LLM query failed (attempt {attempt}): {ex.Message}");
                    if (attempt >= maxRetries)
                        throw;
                    await Task.Delay(1000);
                }
            }
            throw lastException ?? new Exception("Unknown LLM query failure.");
        }

        // The original LLM query logic, separated for clarity.
        private static async Task<string> QueryLLMCore(
            string systemRole,
            string userPrompt,
            double temperature = 0.7)
        {
            var messages = new List<Intel.Message>
            {
                new Intel.Message { role = "system", content = systemRole },
                new Intel.Message { role = "user", content = $"{Config.ConciseInstruction}\n{userPrompt}" }
            };
            return (await Intel.QueryLLMAsync(Config.LLMEndpoint, Config.LLMModel, messages, temperature, -1, false)).Trim();
        }

        /// <summary>
        /// Generates a fact (real or corrupted) using the LLM.
        /// </summary>
        public static Task<string> GenerateFact(bool real)
        {
            string systemRole = real ? "You are a real-world fact generator." : "You are a creative fact generator.";
            string userPrompt = real
                ? "Generate a single interesting, true, real-world fact (not about a person). Output only the fact."
                : "Generate a single fictional, obviously false, or absurd 'fact' about a country or city. Make it sound plausible but clearly incorrect. Output only the fact.";
            return QueryLLM(systemRole, userPrompt);
        }

        /// <summary>
        /// Generates a question from a fact using the LLM.
        /// </summary>
        public static Task<string> GenerateQuestion(string fact)
        {
            string systemRole = "You are a question generator.";
            string userPrompt = $"Given the following fact, generate a direct question that would prompt someone to answer with this fact. Output only the question.\nFact:\n{fact}";
            return QueryLLM(systemRole, userPrompt);
        }

        /// <summary>
        /// Extracts the answer from a fact using the LLM.
        /// </summary>
        public static Task<string> ExtractAnswer(string fact)
        {
            string systemRole = "You are an answer extractor.";
            string userPrompt = $"Given the following fact, extract the answer (the key information) as briefly as possible. Output only the answer.\nFact:\n{fact}";
            return QueryLLM(systemRole, userPrompt);
        }

        /// <summary>
        /// Generates a list of corrupted facts using the LLM.
        /// </summary>
        public static async Task<List<string>> GenerateCorruptedFactsList(int count = 15)
        {
            string systemRole = "You are a creative fact generator.";
            string userPrompt = $"Generate a list of {count} fictional, obviously false, or absurd 'facts' (one per line). Do not number them. Make them sound plausible but clearly incorrect.";
            string factsRaw = await QueryLLM(systemRole, userPrompt, 0.7);
            var facts = new List<string>();
            foreach (var line in factsRaw.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    facts.Add(trimmed);
            }
            return facts;
        }
    }

    public static class ModelSelector
    {
        public static (string endpoint, string model) AssignModel(string agentRole, string taskType, string context, Guid? agentId = null)
        {
            if (agentId != null && Config.AgentModelOverrides.TryGetValue(agentId.Value, out var overrideModel))
            {
                if (overrideModel == "tiny-0.5b") return (Config.TinyModelAddress, "tiny-0.5b");
                if (overrideModel == "mid-8b") return (Config.MidModelAddress, "mid-8b");
                if (overrideModel == "thinker-70b") return (Config.ThinkerModelAddress, "thinker-70b");
            }
            // Example logic: prioritize Tiny for routing/fast, Thinker for deep/long, Mid as fallback
            if (agentRole == "Worker" && (taskType.Contains("route") || taskType.Contains("decision")))
                return (Config.TinyModelAddress, "tiny-0.5b");
            if (context.Length > 500 || agentRole == "Director")
                return (Config.ThinkerModelAddress, "thinker-70b");
            return (Config.MidModelAddress, "mid-8b");
        }
    }

    public static class ModelHealthManager
    {
        private static readonly Dictionary<string, bool> Health = new();

        public static bool IsAvailable(string endpoint)
        {
            if (!Health.ContainsKey(endpoint))
                Health[endpoint] = Ping(endpoint);
            return Health[endpoint];
        }

        public static void MarkUnavailable(string endpoint)
        {
            Health[endpoint] = false;
        }

        public static void MarkAvailable(string endpoint)
        {
            Health[endpoint] = true;
        }

        private static bool Ping(string endpoint)
        {
            try
            {
                // Simple HTTP HEAD or GET request to check endpoint health
                using var client = new System.Net.Http.HttpClient();
                var response = client.GetAsync(endpoint).Result;
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static void RefreshAll()
        {
            foreach (var endpoint in new[] { Config.TinyModelAddress, Config.MidModelAddress, Config.ThinkerModelAddress })
                Health[endpoint] = Ping(endpoint);
        }
    }
}