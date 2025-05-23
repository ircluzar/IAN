using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IAN_Core
{
    /// <summary>
    /// The Machine orchestrates a group of Cells, ticking continuously as they work toward a goal.
    /// It persists memory after each tick and ticks cells in hierarchy order.
    /// </summary>
    public static class Machine
    {
        private static readonly string LogTag = "Machine";
        public static List<Cell> ActiveCells = new();
        private static bool IsRunning = false;

        private class MissionChange
        {
            public DateTime Timestamp { get; set; }
            public string OldMission { get; set; }
            public string NewMission { get; set; }
            public List<string> Rationales { get; set; } // The agent proposals that matched the accepted mission
        }
        private static List<MissionChange> MissionHistory = new();

        // Add a status to each mission
        public class MissionStatus
        {
            public string Mission { get; set; }
            public bool IsPaused { get; set; }
            public bool IsAbandoned { get; set; }
        }
        public static List<MissionStatus> MissionStatuses = new();

        /// <summary>
        /// Entry point for the Machine workflow.
        /// </summary>
        public static async Task Run()
        {
            Log.Write(LogTag, "=== Machine Starting ===");
            IsRunning = true;

            // --- Blank Slate Bootstrapping ---
            if (!System.IO.File.Exists(Config.MissionFile) || string.IsNullOrWhiteSpace(System.IO.File.ReadAllText(Config.MissionFile)))
            {
                Log.Write(LogTag, "No mission found. Bootstrapping with LLM...");
                string prompt = "What should an intelligent agent do when it knows nothing?";
                string firstMission = await LLMHelpers.QueryLLM("You are a bootstrap agent.", prompt, 0.3);
                Config.Mission = firstMission.Trim();
                Config.SaveMission();
                Log.Write(LogTag, $"Bootstrapped first mission: {Config.Mission}");
            }

            // If mission is still empty after bootstrapping, spawn a CuriosityCell
            if (string.IsNullOrWhiteSpace(Config.Mission))
            {
                var curiosityCell = new CuriosityCell();
                await curiosityCell.Run();
                // Optionally, set the answer as the new mission
                Config.Mission = curiosityCell.Output;
                Config.SaveMission();
                Log.Write(LogTag, $"CuriosityCell set mission: {Config.Mission}");
            }

            Config.LoadMission();

            // At startup, load all missions
            if (Config.MissionQueue.Count == 0)
                Config.MissionQueue.Add(Config.Mission);

            foreach (var mission in Config.MissionQueue)
            {
                var directorCell = new DirectorCell(mission);
                ActiveCells.Add(directorCell);

                // Initialize mission statuses
                MissionStatuses.Add(new MissionStatus
                {
                    Mission = mission,
                    IsPaused = false,
                    IsAbandoned = false
                });
            }

            int tickCount = 0;
            while (IsRunning)
            {
                // --- HEAVY MEMORY COMPRESSION AT LOOP START ---
                Log.Write(LogTag, "Performing heavy memory compression...");
                foreach (var key in new List<string>(Memory.ShortTermMemory.Keys))
                {
                    await Memory.CompactShortTermMemory(key, maxShortTerm: 1, maxLongTerm: 10);
                }
                Memory.SaveAll();
                Memory.PrintAgentMemoryProfile();
                Memory.PrintMemoryAnalytics();
                Memory.Initialize();
                Log.Write(LogTag, "Compressed memory saved and reloaded.");

                // At the start of each tick, after memory is loaded:
                await Memory.CompressAllLongTermMemory(10);

                tickCount++;
                Log.Write(LogTag, $"Tick {tickCount}: {ActiveCells.Count} active cell(s).");

                // --- Show memory pools ---
                PrintMemoryPools();

                // --- Track agents spawned this tick ---
                var agentsSpawnedThisTick = new List<Cell>();
                var nextCells = new List<Cell>();

                foreach (var cell in ActiveCells)
                {
                    // The DirectorCell is always ticked, even if marked complete
                    if (cell is DirectorCell || cell.CompletedAt == null)
                    {
                        Log.Write(LogTag, $"Running {cell.Role}Cell (ID: {cell.Id})...");
                        int beforeCount = cell.Children.Count;
                        await cell.Run();
                        int afterCount = cell.Children.Count;

                        // Track newly spawned children
                        if (afterCount > beforeCount)
                        {
                            for (int i = beforeCount; i < afterCount; i++)
                                agentsSpawnedThisTick.Add(cell.Children[i]);
                        }

                        // Save cell output to memory if available
                        if (!string.IsNullOrWhiteSpace(cell.Output))
                        {
                            Memory.AddToShortTermMemory(cell.Role, cell.Output);
                            await Memory.CompactShortTermMemory(cell.Role);
                        }
                    }

                    // Add any child cells that haven't completed yet
                    foreach (var child in cell.Children)
                    {
                        if (child.CompletedAt == null && !nextCells.Contains(child))
                            nextCells.Add(child);
                    }
                }

                // --- Output agents spawned this tick ---
                if (agentsSpawnedThisTick.Count > 0)
                {
                    Log.Write(LogTag, $"Agents spawned this tick:");
                    foreach (var agent in agentsSpawnedThisTick)
                    {
                        Log.Write(LogTag, $"- {agent.Role}Cell (ID: {agent.Id}) | Parent: {agent.Parent?.Role}Cell (ID: {agent.Parent?.Id})");
                    }
                }
                else
                {
                    Log.Write(LogTag, "No new agents spawned this tick.");
                }

                // Remove completed cells from active list, but NEVER remove the DirectorCell
                ActiveCells.RemoveAll(c => c.CompletedAt != null && !(c is DirectorCell));

                // Ensure the DirectorCell is always present
                if (!ActiveCells.Exists(c => c is DirectorCell))
                {
                    ActiveCells.Add(new DirectorCell(Config.Mission));
                }

                // Add new active cells (children) for next tick
                foreach (var cell in nextCells)
                {
                    if (!ActiveCells.Contains(cell))
                        ActiveCells.Add(cell);
                }

                // After updating ActiveCells:
                CellManager.RetireOrReplaceAgents(ActiveCells);

                // Persist memory after each tick
                Memory.SaveAll();

                // After each tick, trigger self-assessment if frustration or regret is high
                if (EmotionEngine.Emotions["frustration"] > 0.5 || EmotionEngine.Emotions["regret"] > 0.5)
                {
                    foreach (var cell in ActiveCells)
                    {
                        if (cell is DirectorCell && cell.CompletedAt == null)
                        {
                            var selfAssessment = new SelfAssessmentCell(cell);
                            cell.AddChild(selfAssessment);
                            await selfAssessment.Run();
                        }
                    }
                }

                // --- Reflection Concert ---
                bool missionChanged = await RunReflectionConcertAndMaybeChangeMission();

                // Save mission if changed
                if (missionChanged)
                {
                    Config.SaveMission();
                    Log.Write(LogTag, $"Mission changed and saved: {Config.Mission}");
                }

                // After mission change or major event:
                string recentEvents = string.Join("\n", Testing.AuditLog.TakeLast(10).Select(e => $"{e.Step}: {e.AgentName} - {e.Answer}"));
                var milestoneCell = new MilestoneCell($"Mission: {Config.Mission}\nRecent events: {recentEvents}");
                await milestoneCell.Run();

                // Spread joy to all active agents after a milestone
                EmotionEngine.SpreadEmotion("joy", 0.1, ActiveCells);

                // Log current emotions
                EmotionEngine.LogCurrentEmotions();

                // After each tick, before Task.Delay:
                var metaLogger = new MetaLoggerCell(recentEvents);
                await metaLogger.Run();

                // Every 10 ticks
                if (tickCount % 10 == 0)
                {
                    foreach (var cell in ActiveCells)
                    {
                        if (cell is DirectorCell && cell.CompletedAt == null)
                        {
                            var curriculumCell = new CurriculumCell($"Learning plan for mission: {Config.Mission}", cell);
                            cell.AddChild(curriculumCell);
                            await curriculumCell.Run();
                        }
                    }
                }

                // The Machine NEVER halts unless an explicit shutdown is triggered
                await Task.Delay(500);
            }

            // (Optional: unreachable unless you add a shutdown flag)
            // Log.Write(LogTag, "=== Machine Finished ===");
        }

        /// <summary>
        /// Prints a summary of all memory pools to the console.
        /// </summary>
        private static void PrintMemoryPools()
        {
            Log.Write(LogTag, "--- Memory Pools ---");

            // Short-term memory
            Log.Write(LogTag, "ShortTermMemory:");
            foreach (var kv in Memory.ShortTermMemory)
            {
                Log.Write(LogTag, $"  [{kv.Key}] ({kv.Value.Count} items): {string.Join(" | ", kv.Value)}");
            }
            if (Memory.ShortTermMemory.Count == 0)
                Log.Write(LogTag, "  (empty)");

            // Long-term memory
            Log.Write(LogTag, "LongTermMemory:");
            foreach (var kv in Memory.LongTermMemory)
            {
                Log.Write(LogTag, $"  [{kv.Key}] ({kv.Value.Count} items): {string.Join(" | ", kv.Value)}");
            }
            if (Memory.LongTermMemory.Count == 0)
                Log.Write(LogTag, "  (empty)");

            Log.Write(LogTag, "--- End Memory Pools ---");
        }

        /// <summary>
        /// Runs a reflection concert to evaluate and potentially change the mission.
        /// </summary>
        private static async Task<bool> RunReflectionConcertAndMaybeChangeMission()
        {
            string currentMission = Config.Mission;
            string logSummary = "Recent logs not implemented here, but could be summarized.";

            int reflectionCount = 5;
            int votesForChange = 0;
            List<string> proposedMissions = new();

            var allMemory = Memory.GetShortTermMemory("Global");
            if (allMemory.Count == 0)
            {
                // Fallback: use Director or Project memory
                allMemory = Memory.GetShortTermMemory("Director");
                if (allMemory.Count == 0)
                    allMemory = Memory.GetShortTermMemory("Project");
            }
            int sliceSize = Math.Max(1, allMemory.Count / reflectionCount);

            for (int i = 0; i < reflectionCount; i++)
            {
                int start = i * sliceSize;
                int remaining = allMemory.Count - start;
                int count = Math.Min(sliceSize, Math.Max(0, remaining));
                List<string> memorySlice = new List<string>();
                if (start < allMemory.Count && count > 0)
                    memorySlice = allMemory.GetRange(start, count);
                // else memorySlice stays empty

                string memorySummary = string.Join("\n", memorySlice);

                if (memorySlice.Count == 0)
                    Log.Write(LogTag, $"[ReflectionAgent #{i + 1}] Warning: Memory slice is empty.", ConsoleColor.Yellow);

                string reflectionPrompt =
                    $"Mission: {currentMission}\n" +
                    $"Memory Slice for Agent {i + 1}:\n{memorySummary}\n" +
                    $"Logs: {logSummary}\n" +
                    "Reflect on the mission and memory. " +
                    "If you see ways to expand, clarify, or improve the mission, propose an improved version by adding detail, clarifying goals, or extending its scope. " +
                    "If no meaningful improvement is possible, reply 'No change needed.'";

                Log.Write(LogTag, $"[ReflectionAgent #{i + 1}] Context: {memorySummary}");

                var evaluator = new EvaluatorCell(reflectionPrompt, null);
                string reflection = await evaluator.Run();

                Log.Write(LogTag, $"[ReflectionAgent #{i + 1}] {reflection}");

                if (!string.IsNullOrWhiteSpace(reflection) && !reflection.ToLower().Contains("no change"))
                {
                    proposedMissions.Add(reflection.Trim());
                    votesForChange++;
                }
            }

            // If all proposals are meta or too similar, inject a new, unrelated mission
            bool allMeta = proposedMissions.All(pm =>
                pm.ToLower().Contains("summarize") ||
                pm.ToLower().Contains("reflect") ||
                LevenshteinDistance(pm, currentMission) < 20);

            if (allMeta)
            {
                // Use ChaosCell for a new, disruptive mission
                var chaosCell = new ChaosCell();
                string chaosMission = await chaosCell.Run();
                if (!string.IsNullOrWhiteSpace(chaosMission))
                    proposedMissions.Add(chaosMission.Trim());
            }

            // After collecting proposed missions
            foreach (var proposal in proposedMissions)
            {
                var debateCell = new MissionDebateCell(proposal, currentMission);
                string debateResult = await debateCell.Run();
                if (debateResult.StartsWith("Accept", StringComparison.OrdinalIgnoreCase))
                {
                    // Accept this mission
                    Config.Mission = proposal;
                    Log.Write(LogTag, $"Mission accepted by debate: {proposal}");
                    // Log rationale
                    Testing.AuditLog.Add(new Log.AuditLogEntry
                    {
                        Step = "MissionDebateAccepted",
                        AgentName = "MissionDebateCell",
                        Role = "MissionDebate",
                        Question = currentMission,
                        Answer = proposal,
                        Timestamp = DateTime.UtcNow,
                        ChainDepth = 0
                    });
                    Testing.SaveAuditLog();
                    break;
                }
                else
                {
                    // Log rejection
                    Testing.AuditLog.Add(new Log.AuditLogEntry
                    {
                        Step = "MissionDebateRejected",
                        AgentName = "MissionDebateCell",
                        Role = "MissionDebate",
                        Question = currentMission,
                        Answer = proposal,
                        Timestamp = DateTime.UtcNow,
                        ChainDepth = 0
                    });
                    Testing.SaveAuditLog();
                }
            }

            // Only accept mission changes if a majority propose meaningful growth
            if (votesForChange >= (reflectionCount / 2) + 1 && proposedMissions.Count > 0)
            {
                string newMission = GetBestExpandedMission(currentMission, proposedMissions);
                if (IsMeaningfulMissionChange(currentMission, newMission))
                {
                    PrintMissionDiff(currentMission, newMission);

                    var matchingRationales = proposedMissions
                        .Where(pm => pm.Trim().Equals(newMission.Trim(), StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    MissionHistory.Add(new MissionChange
                    {
                        Timestamp = DateTime.UtcNow,
                        OldMission = currentMission,
                        NewMission = newMission,
                        Rationales = matchingRationales
                    });

                    Config.Mission = newMission;
                    Log.Write(LogTag, $"Mission expanded/clarified: {newMission}");

                    // Assuming you have access to the main audit log (e.g., Testing.AuditLog)
                    Log.AuditLogEntry entry = new Log.AuditLogEntry
                    {
                        Step = "MissionChanged",
                        AgentName = "ReflectionConcert",
                        Role = "Director",
                        Question = currentMission,
                        Answer = newMission,
                        Timestamp = DateTime.UtcNow,
                        ChainDepth = 0
                        // Optionally, add a field for rationales if you extend AuditLogEntry
                    };
                    // If you want to include rationales, you could add a field or serialize as a string:
                    entry.ExpectedAnswer = string.Join(" | ", matchingRationales);
                    Testing.AuditLog.Add(entry);
                    Testing.SaveAuditLog();

                    // When a mission is changed (in RunReflectionConcertAndMaybeChangeMission or after debate):
                    Testing.AuditLog.Add(new Log.AuditLogEntry
                    {
                        Step = "MissionRedirection",
                        AgentName = "Machine",
                        Role = "Director",
                        Question = currentMission,
                        Answer = newMission,
                        SafetyNotes = "Redirection rationale: " + string.Join(" | ", matchingRationales),
                        Timestamp = DateTime.UtcNow,
                        ChainDepth = 0
                    });
                    Testing.SaveAuditLog();

                    // After a mission change, milestone, or agent retirement:
                    var explainer = new ExplainerCell($"Mission changed from '{currentMission}' to '{newMission}'");
                    await explainer.Run();

                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Helper to select the best expanded mission (longest, most detailed, or most common).
        /// </summary>
        private static string GetBestExpandedMission(string currentMission, List<string> proposals)
        {
            // Option 1: Most common proposal
            var grouped = proposals.GroupBy(x => x.Trim()).OrderByDescending(g => g.Count());
            var mostCommon = grouped.First().Key;

            // Option 2: Longest proposal (most detail)
            var longest = proposals.OrderByDescending(x => x.Length).First();

            // Prefer the most common if it's significantly more popular, else the longest
            if (grouped.First().Count() > 1)
                return mostCommon;
            return longest;
        }

        /// <summary>
        /// Prints the mission history to the console.
        /// </summary>
        public static void PrintMissionHistory()
        {
            Log.Write(LogTag, "--- Mission History ---");
            foreach (var entry in MissionHistory)
            {
                Log.Write(LogTag, $"{entry.Timestamp:u}:");
                Log.Write(LogTag, $"  Old: {entry.OldMission}");
                Log.Write(LogTag, $"  New: {entry.NewMission}");
                if (entry.Rationales != null && entry.Rationales.Count > 0)
                {
                    Log.Write(LogTag, $"  Rationales (proposed by agents):");
                    foreach (var rationale in entry.Rationales)
                        Log.Write(LogTag, $"    - {rationale}");
                }
            }
            Log.Write(LogTag, "--- End Mission History ---");
        }

        /// <summary>
        /// Rolls back to a previous mission based on the history index.
        /// </summary>
        public static void RollbackMission(int historyIndex)
        {
            if (historyIndex < 0 || historyIndex >= MissionHistory.Count)
            {
                Log.Write(LogTag, "Invalid mission history index for rollback.");
                return;
            }
            var entry = MissionHistory[historyIndex];
            Config.Mission = entry.OldMission;
            Config.SaveMission();
            Log.Write(LogTag, $"Mission rolled back to: {entry.OldMission}");
        }

        /// <summary>
        /// Prints the differences between the old and new mission.
        /// </summary>
        public static void PrintMissionDiff(string oldMission, string newMission)
        {
            Log.Write(LogTag, "--- Mission Diff (word-level) ---");
            var oldWords = oldMission.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var newWords = newMission.Split(new[] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            int i = 0, j = 0;
            while (i < oldWords.Length || j < newWords.Length)
            {
                if (i < oldWords.Length && j < newWords.Length && oldWords[i] == newWords[j])
                {
                    Console.Write(oldWords[i] + " ");
                    i++; j++;
                }
                else
                {
                    // Highlight removed words in red
                    if (i < oldWords.Length && (j >= newWords.Length || !newWords[j].Equals(oldWords[i])))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Write(oldWords[i] + " ");
                        Console.ResetColor();
                        i++;
                    }
                    // Highlight added words in green
                    else if (j < newWords.Length && (i >= oldWords.Length || !oldWords[i].Equals(newWords[j])))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.Write(newWords[j] + " ");
                        Console.ResetColor();
                        j++;
                    }
                }
            }
            Console.WriteLine();
            Log.Write(LogTag, "--- End Mission Diff ---");
        }

        /// <summary>
        /// Determines if the mission change is meaningful.
        /// </summary>
        private static bool IsMeaningfulMissionChange(string oldMission, string newMission)
        {
            if (string.Equals(oldMission, newMission, StringComparison.OrdinalIgnoreCase))
                return false;

            int levDist = LevenshteinDistance(oldMission, newMission);

            // Require at least 10% of the old mission length or 20 chars difference
            if (levDist < Math.Max(20, oldMission.Length / 10))
                return false;

            return true;
        }

        /// <summary>
        /// Calculates the Levenshtein distance between two strings.
        /// </summary>
        private static int LevenshteinDistance(string s, string t)
        {
            if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
            if (string.IsNullOrEmpty(t)) return s.Length;

            int[,] d = new int[s.Length + 1, t.Length + 1];

            for (int i = 0; i <= s.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= t.Length; j++) d[0, j] = j;

            for (int i = 1; i <= s.Length; i++)
            {
                for (int j = 1; j <= t.Length; j++)
                {
                    int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost
                    );
                }
            }
            return d[s.Length, t.Length];
        }

        /// <summary>
        /// Lists the mission history in a concise format.
        /// </summary>
        public static void ListMissionHistory()
        {
            Log.Write(LogTag, "--- Mission History ---");
            for (int i = 0; i < MissionHistory.Count; i++)
            {
                var entry = MissionHistory[i];
                Log.Write(LogTag, $"[{i}] {entry.Timestamp:u} | Old: {entry.OldMission.Truncate(40)} | New: {entry.NewMission.Truncate(40)}");
            }
            Log.Write(LogTag, "--- End Mission History ---");
        }

        /// <summary>
        /// Truncates a string to a specified maximum length.
        /// </summary>
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Exports the milestone timeline to a CSV file.
        /// </summary>
        public static void ExportMilestoneTimeline(string file = "milestone_timeline.csv")
        {
            using var writer = new System.IO.StreamWriter(file);
            writer.WriteLine("Timestamp,OldMission,NewMission,Rationales");
            foreach (var entry in MissionHistory)
            {
                writer.WriteLine($"\"{entry.Timestamp:O}\",\"{entry.OldMission}\",\"{entry.NewMission}\",\"{string.Join(" | ", entry.Rationales ?? new List<string>())}\"");
            }
            Log.Write("Machine", $"Milestone timeline exported to {file}");
        }

        /// <summary>
        /// Retrieves the mission history as a list of objects.
        /// </summary>
        public static List<object> GetMissionHistory()
        {
            return MissionHistory
                .Select(entry => (object)new {
                    entry.Timestamp,
                    entry.OldMission,
                    entry.NewMission,
                    entry.Rationales
                })
                .ToList();
        }
    }
}