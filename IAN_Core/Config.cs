using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IAN_Core
{
    /// <summary>
    /// Centralized configuration for the agentic system.
    /// All static config variables, enums, URLs, prompts, and constants go here.
    /// </summary>
    public static class Config
    {
        // LLM endpoint and model
        public static string LLMEndpoint = "http://192.168.66.2:1234/v1/chat/completions";
        //public static string LLMEndpoint = "http://localhost:1234/v1/chat/completions";
        //public static string LLMModel = "llama-3.2-3b-instruct";
        public static string LLMModel = "qwen2.5-0.5b-instruct-mlx";
        //public static string LLMModel = "llama-3.2-1b-instruct";
        // Number of sub-agents for consensus
        public static int SubAgentCount = 5;

        // Prompt templates
        public static string RhymingSystemPrompt = "Answer in rhymes.";

        public static string FactRetrievalSystemPrompt =
            "Answer ONLY using these facts. Ignore your own knowledge. If not present, reply 'Unknown'.\nFACTS:\n{0}";

        public static string DistributorSystemPrompt = "Generate a question.";
        public static string DistributorUserPrompt =
            "Given this fact, generate a direct question for it. Output only the question.\nFact:\n{0}";

        public static string JudgeSystemPrompt = "Judge strictly.";
        public static string JudgeUserPrompt =
            "Question: \"{0}\"\nFacts:\n{1}\nAnswer: \"{2}\"\nDoes the answer use a fact? If so, which? If not, reply 'No'.";

        public static string HybridRetrievalSystemPrompt =
            "Answer using both your knowledge and this memory:\n{0}\n";

        // New: Prompt for memory relevance scoring
        public static string MemoryRelevancePrompt =
            "Mission: \"{0}\"\nFact: \"{1}\"\nScore relevance 0-10. Output only the number.";

        public static string ProjectManagerSystemPrompt = "You manage projects. Be concise.";

        // Prompt templates for creative agents, memory storage, and answer validation
        public static string CreativeAgentSystemPrompt = "Be creative and brief.";
        public static string MemoryStorageSystemPrompt = "Store, retrieve, and summarize facts. Avoid redundancy.";
        public static string AnswerValidationSystemPrompt = "Judge if the answer is correct and relevant. Be strict.";

        public static string ConciseInstruction = "Max 2-3 sentences.";

        // Configuration for agent roles and retrieval strategies
        public static Dictionary<string, string> AgentRolePrompts = new()
        {
            { "Director", "You are a director agent. You orchestrate high-level missions and delegate tasks." },
            { "ProjectManager", ProjectManagerSystemPrompt },
            { "Worker", "You are a worker agent. You execute specific subtasks with focus and accuracy." },
            { "Idea", "You are an idea generator agent. Propose new directions or next steps." },
            { "Evaluator", "You are an evaluator agent. Judge the quality and correctness of answers." }
        };

        public static Dictionary<string, AgentRetrievalMode> AgentRetrievalStrategies = new()
        {
            { "Director", AgentRetrievalMode.Hybrid },
            { "ProjectManager", AgentRetrievalMode.Hybrid },
            { "Worker", AgentRetrievalMode.Hybrid },
            { "Idea", AgentRetrievalMode.ModelOnly },
            { "Evaluator", AgentRetrievalMode.MemoryOnly }
        };

        public static Dictionary<Guid, string> AgentModelOverrides { get; } = new Dictionary<Guid, string>();

        // Test-specific parameters (e.g., number of creative attempts, judge strictness)
        public static int CreativeAttemptsPerTest = 3;
        public static double JudgeStrictnessThreshold = 0.8; // 0.0 = lenient, 1.0 = strict

        // Mission-related configuration
        public static string MissionFile = "mission.txt";
        public static string Mission = "Say as little as possible.";

        public static string BaseIdea = "A very few words.";

        public static List<string> MissionQueue = new List<string>();

        public static void SaveMission()
        {
            File.WriteAllText(MissionFile, Mission);
        }

        public static void LoadMission()
        {
            if (File.Exists(MissionFile))
                Mission = File.ReadAllText(MissionFile);
        }

        public static List<string> UserSafetyKeywords = new() { "unsafe", "bias", "hallucinate", "offensive", "toxic" };

        public static bool IsUnsafe(string output)
        {
            if (!string.IsNullOrWhiteSpace(output) && UserSafetyKeywords.Any(k => output.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                // ...existing safety logic...
                return true;
            }
            return false;
        }

        // === ENUMS MOVED HERE FOR CONSISTENCY ===

        /// <summary>
        /// Types of consensus that can be reached by the orchestrator.
        /// </summary>
        public enum ConsensusType
        {
            Word,
            Number,
            Bool,
            Knowledge
        }

        /// <summary>
        /// Modes for agent retrieval (model, memory, hybrid).
        /// </summary>
        public enum AgentRetrievalMode
        {
            ModelOnly,
            MemoryOnly,
            Hybrid
        }

        /// <summary>
        /// Types of models available for use.
        /// </summary>
        public enum ModelType
        {
            Tiny,
            Mid,
            Thinker
        }

        // Model addresses
        public static string TinyModelAddress = "http://192.168.66.2:1234/v1/chat/completions";
        public static string MidModelAddress = "http://192.168.66.2:1234/v1/chat/completions"; // Example: use a real IP or DNS name
        public static string ThinkerModelAddress = "http://192.168.66.2:1234/v1/chat/completions";
    }
}