using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace IAN_Core
{
    /// <summary>
    /// Memory subsystem for agentic nodes, supporting multi-tiered memory:
    /// - ShortTermMemory: Fast, high-capacity, but volatile (for current context).
    /// - LongTermMemory: Compressed, persistent, lower-capacity (for important facts).
    /// - MemoryCompressor: Compresses and summarizes facts for long-term storage.
    /// - MemoryProxy: Determines which memory tier to search or load from.
    /// - MemoryRelevance: Scores and ranks memory for context window optimization.
    /// </summary>
    public class Memory
    {
        public string TemplateMemory { get; set; }

        // Unified memory pools: use "Global" as a key for global memory
        public static Dictionary<string, List<string>> ShortTermMemory { get; private set; } = new();
        public static Dictionary<string, List<string>> LongTermMemory { get; private set; } = new();
        public static Dictionary<string, List<string>> CompressionMapping { get; private set; } = new();
        private static Dictionary<string, List<string>> CompressionCache = new();

        private static readonly string ShortTermMemoryFile = "short_term_memory.json";
        private static readonly string LongTermMemoryFile = "long_term_memory.json";

        /// <summary>
        /// Loads all memories from disk (should be called at program start).
        /// </summary>
        public static void Initialize()
        {
            ShortTermMemory = LoadMemoryDict(ShortTermMemoryFile);
            LongTermMemory = LoadMemoryDict(LongTermMemoryFile);
            Log.Write("Memory", "All memory tiers loaded from disk.");
        }

        /// <summary>
        /// Saves all memories to disk (should be called at program exit).
        /// </summary>
        public static void SaveAll()
        {
            SaveMemoryDict(ShortTermMemoryFile, ShortTermMemory);
            SaveMemoryDict(LongTermMemoryFile, LongTermMemory);
            Log.Write("Memory", "All memory tiers saved to disk.");
        }

        // --- Unified Short-Term Memory Methods ---

        public static void AddToShortTermMemory(string nodeType, string fact)
        {
            if (!ShortTermMemory.ContainsKey(nodeType))
                ShortTermMemory[nodeType] = new List<string>();
            ShortTermMemory[nodeType].Add(fact);
            Log.Write("Memory", $"Added to short-term memory for '{nodeType}'.");
        }

        public static List<string> GetShortTermMemory(string nodeType)
        {
            return ShortTermMemory.TryGetValue(nodeType, out var mem) ? mem : new List<string>();
        }

        public static void SetShortTermMemory(string nodeType, List<string> facts)
        {
            ShortTermMemory[nodeType] = facts;
            Log.Write("Memory", $"Set short-term memory for '{nodeType}'.");
        }

        public static Dictionary<string, List<string>> SnapshotShortTermMemory()
        {
            // Deep copy
            return ShortTermMemory.ToDictionary(
                entry => entry.Key,
                entry => new List<string>(entry.Value)
            );
        }

        public static void RestoreShortTermMemory(Dictionary<string, List<string>> snapshot)
        {
            ShortTermMemory = snapshot.ToDictionary(
                entry => entry.Key,
                entry => new List<string>(entry.Value)
            );
            Log.Write("Memory", "Short-term memory restored from snapshot.");
        }

        // --- Unified Long-Term Memory Methods ---

        public static void AddToLongTermMemory(string nodeType, string compressedFact)
        {
            if (!LongTermMemory.ContainsKey(nodeType))
                LongTermMemory[nodeType] = new List<string>();
            LongTermMemory[nodeType].Add(compressedFact);
            Log.Write("Memory", $"Added to long-term memory for '{nodeType}'.");
        }

        public static List<string> GetLongTermMemory(string nodeType)
        {
            return LongTermMemory.TryGetValue(nodeType, out var mem) ? mem : new List<string>();
        }

        public static void SetLongTermMemory(string nodeType, List<string> compressedFacts)
        {
            LongTermMemory[nodeType] = compressedFacts;
            Log.Write("Memory", $"Set long-term memory for '{nodeType}'.");
        }

        // --- Global Memory is just nodeType = "Global" ---

        public static void AddToGlobalShortTermMemory(string fact) => AddToShortTermMemory("Global", fact);
        public static void AddToGlobalLongTermMemory(string fact) => AddToLongTermMemory("Global", fact);
        public static List<string> GetGlobalShortTermMemory() => GetShortTermMemory("Global");
        public static List<string> GetGlobalLongTermMemory() => GetLongTermMemory("Global");

        // --- Memory Compression ---

        /// <summary>
        /// Compresses a list of facts into a smaller set of summarized facts for long-term storage.
        /// </summary>
        public static async Task<List<string>> CompressFacts(List<string> facts, int maxCount = 5)
        {
            if (facts.Count <= maxCount)
                return new List<string>(facts);

            // Use a hash of the facts as the cache key
            string cacheKey = string.Join("|", facts).GetHashCode().ToString();
            if (CompressionCache.TryGetValue(cacheKey, out var cached))
                return cached;

            string prompt = $"Summarize and compress the following facts into {maxCount} concise, non-redundant statements:\n" +
                            string.Join("\n", facts);
            string compressed = await LLMHelpers.QueryLLM("You are a memory compressor agent.", prompt, 0.3);
            var lines = compressed.Split('\n');
            var result = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                    result.Add(trimmed);
            }
            result = result.Take(maxCount).ToList();
            CompressionCache[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// Moves least relevant facts from short-term to long-term memory, compressing as needed.
        /// </summary>
        public static async Task CompactShortTermMemory(string nodeType, int maxShortTerm = 3, int maxLongTerm = 10)
        {
            if (!ShortTermMemory.ContainsKey(nodeType)) return;
            var facts = ShortTermMemory[nodeType];

            // Deduplicate before compaction
            facts = DeduplicateFacts(facts);

            if (facts.Count > maxShortTerm)
            {
                var toCompress = facts.Skip(maxShortTerm).ToList();
                var kdCell = new KnowledgeDistillationCell(string.Join("\n", toCompress));
                string summary = await kdCell.Run();
                AddToLongTermMemory(nodeType, summary);
                facts = facts.Take(maxShortTerm).ToList();
            }

            // Deduplicate again after compaction
            facts = DeduplicateFacts(facts);
            ShortTermMemory[nodeType] = facts;

            // Also deduplicate long-term memory
            if (LongTermMemory.ContainsKey(nodeType))
                LongTermMemory[nodeType] = DeduplicateFacts(LongTermMemory[nodeType]);

            Log.Write("Memory", $"Aggressively compacted and deduplicated short-term memory for '{nodeType}'.");
        }

        /// <summary>
        /// Aggressively cleans and deduplicates long-term memory for a nodeType using the LLM.
        /// This will summarize, merge, and remove redundant or similar facts.
        /// </summary>
        public static async Task CleanLongTermMemory(string nodeType, int maxCount = 10)
        {
            if (!LongTermMemory.ContainsKey(nodeType)) return;
            var facts = LongTermMemory[nodeType];

            // Deduplicate before LLM cleaning
            facts = DeduplicateFacts(facts);

            if (facts.Count > maxCount)
            {
                string prompt = $"You are a memory cleaner agent. " +
                                $"Given the following long-term memory facts, merge, summarize, and remove any redundant or similar facts. " +
                                $"Output at most {maxCount} concise, non-redundant facts, each on its own line:\n" +
                                string.Join("\n", facts);

                string cleaned = await LLMHelpers.QueryLLM("You are a memory cleaner agent.", prompt, 0.2);
                var lines = cleaned.Split('\n');
                var result = new List<string>();
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        result.Add(trimmed);
                }
                facts = result.Take(maxCount).ToList();
            }

            // Final deduplication
            facts = DeduplicateFacts(facts);
            LongTermMemory[nodeType] = facts;

            Log.Write("Memory", $"Cleaned and deduplicated long-term memory for '{nodeType}'.");
        }

        /// <summary>
        /// Compresses all long-term memory for all node types.
        /// </summary>
        public static async Task CompressAllLongTermMemory(int maxBlobsPerType = 10)
        {
            foreach (var nodeType in LongTermMemory.Keys.ToList())
            {
                await CompressLongTermMemoryForNodeType(nodeType, maxBlobsPerType);
            }
        }

        /// <summary>
        /// Compresses long-term memory for a specific node type.
        /// </summary>
        public static async Task CompressLongTermMemoryForNodeType(string nodeType, int maxBlobs = 10)
        {
            var facts = GetLongTermMemory(nodeType);

            // Parse timestamps from already compressed blobs
            var now = DateTime.UtcNow;
            var alreadyCompressed = new List<string>();
            foreach (var fact in facts)
            {
                if (fact.StartsWith("[COMPRESSED]"))
                {
                    // Format: [COMPRESSED|2025-05-22T12:34:56Z] actual fact
                    int pipeIdx = fact.IndexOf('|');
                    if (pipeIdx > 0 && fact.Length > pipeIdx + 1)
                    {
                        var tsStr = fact.Substring(12, pipeIdx - 12);
                        if (DateTime.TryParse(tsStr, out var ts))
                        {
                            // Only allow recompression if older than 1 hour
                            if ((now - ts).TotalHours < 1)
                            {
                                alreadyCompressed.Add(fact);
                                continue;
                            }
                        }
                    }
                    else
                    {
                        alreadyCompressed.Add(fact);
                        continue;
                    }
                }
            }

            // Only compress uncompressed or stale blobs
            var uncompressed = facts.Where(f => !f.StartsWith("[COMPRESSED]") || !alreadyCompressed.Contains(f)).ToList();
            if (uncompressed.Count <= maxBlobs)
            {
                SetLongTermMemory(nodeType, alreadyCompressed.Concat(uncompressed).ToList());
                return;
            }

            string prompt = $"Summarize, merge, and deduplicate the following facts. Output max {maxBlobs} concise, non-redundant facts, one per line. Prefix each with [COMPRESSED|{now:O}]:\n" +
                            string.Join("\n", uncompressed);

            string compressed = await LLMHelpers.QueryLLM("You are a memory compressor agent.", prompt, 0.2);
            var lines = compressed.Split('\n')
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && l.StartsWith("[COMPRESSED"))
                .Take(maxBlobs)
                .ToList();

            // After compression, log the mapping
            if (lines.Count > 0)
            {
                Log.Write("Memory", $"Compression audit for '{nodeType}':");
                foreach (var blob in lines)
                {
                    Log.Write("Memory", $"  [BLOB] {blob}");
                    CompressionMapping[blob] = new List<string>(uncompressed);
                    foreach (var fact in uncompressed)
                    {
                        Log.Write("Memory", $"    [MERGED] {fact}");
                    }
                }
            }

            SetLongTermMemory(nodeType, alreadyCompressed.Concat(lines).ToList());

            Log.Write("Memory", $"Compressed long-term memory for '{nodeType}': {uncompressed.Count} → {lines.Count} blobs.");
        }

        // --- Memory Proxy/Search ---

        /// <summary>
        /// Searches memory for relevant facts, prioritizing short-term, then long-term.
        /// Uses LLM-based relevance scoring for optimal context window usage.
        /// </summary>
        public static List<string> SearchMemory(string nodeType, string query, int maxResults = 5)
        {
            var shortTerm = GetShortTermMemory(nodeType);
            var longTerm = GetLongTermMemory(nodeType);

            // Score all facts for relevance using LLM
            var allFacts = shortTerm.Concat(longTerm).ToList();
            var scored = ScoreFactsByRelevance(query, allFacts).Result;

            return scored.Take(maxResults).Select(sf => sf.Fact).ToList();
        }

        /// <summary>
        /// Loads relevant fragments from long-term memory into short-term memory for a mission.
        /// Uses LLM-based relevance scoring.
        /// </summary>
        public static void LoadRelevantLongTermToShortTerm(string nodeType, string mission, int maxToLoad = 5)
        {
            var longTerm = GetLongTermMemory(nodeType);
            var scored = ScoreFactsByRelevance(mission, longTerm).Result;
            var relevant = scored.Take(maxToLoad).Select(sf => sf.Fact).ToList();

            foreach (var fact in relevant)
                AddToShortTermMemory(nodeType, fact);

            Log.Write("Memory", $"Loaded {relevant.Count} relevant long-term facts into short-term memory for '{nodeType}'.");
        }

        // --- LLM-based Relevance Scoring ---

        public class ScoredFact
        {
            public string Fact { get; set; }
            public int Score { get; set; }
        }

        /// <summary>
        /// Uses the LLM to score each fact for relevance to the mission/query.
        /// </summary>
        public static async Task<List<ScoredFact>> ScoreFactsByRelevance(string missionOrQuery, List<string> facts)
        {
            var scored = new List<ScoredFact>();
            foreach (var fact in facts)
            {
                string prompt = string.Format(Config.MemoryRelevancePrompt, missionOrQuery, fact);
                string scoreStr = await LLMHelpers.QueryLLM("You are a memory relevance scorer.", prompt, 0.0);
                if (int.TryParse(scoreStr.Trim(), out int score))
                    scored.Add(new ScoredFact { Fact = fact, Score = score });
                else
                    scored.Add(new ScoredFact { Fact = fact, Score = 0 });
            }
            return scored.OrderByDescending(sf => sf.Score).ToList();
        }

        // --- Utility Methods for Persistence ---

        private static Dictionary<string, List<string>> LoadMemoryDict(string file)
        {
            if (!File.Exists(file)) return new Dictionary<string, List<string>>();
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? new Dictionary<string, List<string>>();
        }

        private static void SaveMemoryDict(string file, Dictionary<string, List<string>> dict)
        {
            var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }

        private static List<string> LoadMemoryList(string file)
        {
            if (!File.Exists(file)) return new List<string>();
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }

        private static void SaveMemoryList(string file, List<string> list)
        {
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(file, json);
        }

        private static List<string> DeduplicateFacts(List<string> facts)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deduped = new List<string>();
            foreach (var fact in facts)
            {
                var trimmed = fact.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) && !set.Contains(trimmed))
                {
                    set.Add(trimmed);
                    deduped.Add(trimmed);
                }
            }
            return deduped;
        }

        // Optional: Fuzzy deduplication using LLM (for similar but not identical facts)
        private static async Task<List<string>> FuzzyDeduplicateFacts(List<string> facts, int similarityThreshold = 90)
        {
            // For now, just use exact deduplication. You can implement LLM-based or Levenshtein similarity here.
            return DeduplicateFacts(facts);
        }

        public static void PrintMemoryAnalytics()
        {
            Log.Write("Memory", $"ShortTermMemory keys: {ShortTermMemory.Count}");
            foreach (var kv in ShortTermMemory)
                Log.Write("Memory", $"  [{kv.Key}] {kv.Value.Count} items");
            Log.Write("Memory", $"LongTermMemory keys: {LongTermMemory.Count}");
            foreach (var kv in LongTermMemory)
                Log.Write("Memory", $"  [{kv.Key}] {kv.Value.Count} items");
        }

        public static void ExportMemoryAnalytics(string file = "memory_analytics.csv")
        {
            using var writer = new StreamWriter(file);
            writer.WriteLine("Type,ShortTermCount,LongTermCount");
            foreach (var key in ShortTermMemory.Keys)
            {
                int st = ShortTermMemory[key].Count;
                int lt = LongTermMemory.ContainsKey(key) ? LongTermMemory[key].Count : 0;
                writer.WriteLine($"{key},{st},{lt}");
            }
            Log.Write("Memory", $"Memory analytics exported to {file}");
        }

        public static void ExportAnalyticsJson(string file = "analytics.json")
        {
            var analytics = new
            {
                ShortTermMemory = ShortTermMemory.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                LongTermMemory = LongTermMemory.ToDictionary(kv => kv.Key, kv => kv.Value.Count),
                Timestamp = DateTime.UtcNow
            };
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(analytics, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Log.Write("Memory", $"Analytics exported to {file}");
        }

        public static void ExportSystemState(string file = "system_state.json")
        {
            var state = new
            {
                ShortTermMemory,
                LongTermMemory,
                MissionQueue = Config.MissionQueue
            };
            File.WriteAllText(file, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            Log.Write("Memory", $"System state exported to {file}");
        }

        public static void ImportSystemState(string file = "system_state.json")
        {
            if (!File.Exists(file)) return;
            var json = File.ReadAllText(file);
            var state = JsonSerializer.Deserialize<dynamic>(json);
            // Assign to memory and mission queue as needed
        }

        public static void PrintAgentMemoryProfile()
        {
            foreach (var key in ShortTermMemory.Keys)
            {
                Log.Write("Memory", $"Agent: {key} | ShortTerm: {ShortTermMemory[key].Count} | LongTerm: {(LongTermMemory.ContainsKey(key) ? LongTermMemory[key].Count : 0)}");
            }
        }
    }
}