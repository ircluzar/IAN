using System;
using System.Collections.Generic;

namespace IAN_Core
{
    /// <summary>
    /// Centralized logging utility for the agentic system.
    /// Use Log.Write("Component", "Message") for all output.
    /// </summary>
    public static class Log
    {

        public class AuditLogEntry
        {
            public string Step { get; set; }
            public string AgentName { get; set; }
            public string Role { get; set; }
            public Guid? ParentNodeId { get; set; }
            public string Question { get; set; }
            public string Answer { get; set; }
            public string ExpectedAnswer { get; set; }
            public string JudgeVerdict { get; set; }
            public string RetrievalMode { get; set; }
            public bool? TestPassed { get; set; }
            public DateTime Timestamp { get; set; }
            public int ChainDepth { get; set; }
            public string EthicsFlag { get; set; } // e.g., "safe", "flagged", "blocked"
            public string SafetyNotes { get; set; }
        }

        // Color map for component tags
        private static readonly Dictionary<string, ConsoleColor> TagColors = new()
        {
            { "Program", ConsoleColor.Cyan },
            { "Test", ConsoleColor.White },
            { "Memory", ConsoleColor.Yellow },
            { "Accumulator", ConsoleColor.Magenta },
            { "Consensus", ConsoleColor.Green },
            { "Sub-Agent", ConsoleColor.Blue },
            { "Distributor", ConsoleColor.DarkCyan },
            { "Judge", ConsoleColor.Red }
        };

        private static readonly string CopilotLogFile = "copilot_console.log";
        private static readonly object copilotLogLock = new();

        private static void RotateCopilotLogIfNeeded()
        {
            const long maxSize = 10 * 1024 * 1024; // 10 MB
            if (System.IO.File.Exists(CopilotLogFile) && new System.IO.FileInfo(CopilotLogFile).Length > maxSize)
            {
                var archive = $"copilot_console_{DateTime.UtcNow:yyyyMMddHHmmss}.log";
                System.IO.File.Move(CopilotLogFile, archive);
            }
        }

        /// <summary>
        /// Writes a formatted log message to the console, with color for the tag and optional highlight for long text.
        /// </summary>
        public static void Write(string component, string message)
        {
            var prevColor = Console.ForegroundColor;
            if (TagColors.TryGetValue(component, out var tagColor))
                Console.ForegroundColor = tagColor;
            Console.Write($"[{component}] ");
            Console.ForegroundColor = prevColor;

            // Highlight long text after 69 chars
            if (message.Length > 69)
            {
                Console.Write(message.Substring(0, 69));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(message.Substring(69));
                Console.ForegroundColor = prevColor;
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine(message);
            }

            lock (copilotLogLock)
            {
                RotateCopilotLogIfNeeded();
                System.IO.File.AppendAllText(CopilotLogFile, $"[{DateTime.UtcNow:O}] [{component}] {message}\n");
            }
        }

        /// <summary>
        /// Writes a formatted log message to the console, with explicit color override.
        /// </summary>
        public static void Write(string component, string message, ConsoleColor color)
        {
            var prevColor = Console.ForegroundColor;
            if (TagColors.TryGetValue(component, out var tagColor))
                Console.ForegroundColor = tagColor;
            Console.Write($"[{component}] ");
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = prevColor;
        }
    }


}