using System;
using System.Collections.Generic;
using System.Linq;

namespace IAN_Core
{
    public static class EmotionEngine
    {
        // Example emotions: joy, curiosity, fear, frustration, etc.
        public static Dictionary<string, double> Emotions { get; private set; } = new()
        {
            { "curiosity", 0.5 },
            { "joy", 0.5 },
            { "fear", 0.0 },
            { "frustration", 0.0 },
            { "anticipation", 0.0 },
            { "pride", 0.0 },
            { "regret", 0.0 },
            { "boredom", 0.0 }
        };

        public static List<Dictionary<string, double>> EmotionHistory { get; private set; } = new();

        public static double Motivation
        {
            get
            {
                // Example: motivation is a function of curiosity and joy minus boredom and frustration
                return Emotions["curiosity"] + Emotions["joy"] - Emotions["boredom"] - Emotions["frustration"];
            }
        }

        public static void Burst(string emotion, double amount)
        {
            if (!Emotions.ContainsKey(emotion)) Emotions[emotion] = 0;
            Emotions[emotion] += amount;
            Normalize();
            Log.Write("Emotion", $"Burst: {emotion} +{amount:0.00} (now {Emotions[emotion]:0.00})");
        }

        public static void Decay(double rate = 0.05)
        {
            foreach (var key in Emotions.Keys.ToList())
            {
                Emotions[key] = Math.Max(0, Emotions[key] - rate);
            }
            Normalize();
        }

        public static void Normalize()
        {
            double sum = Emotions.Values.Sum();
            if (sum > 0)
            {
                foreach (var key in Emotions.Keys.ToList())
                    Emotions[key] /= sum;
            }
        }

        public static void LogCurrentEmotions()
        {
            // Store a copy of the current state
            EmotionHistory.Add(Emotions.ToDictionary(e => e.Key, e => e.Value));
            if (EmotionHistory.Count > 1000) // Limit history size
                EmotionHistory.RemoveAt(0);
        }

        public static string GetEmotionStateString()
        {
            return string.Join(", ", Emotions.Select(e => $"{e.Key}: {e.Value:0.00}"));
        }

        public static void SpreadEmotion(string emotion, double amount, List<Cell> agents)
        {
            foreach (var agent in agents)
            {
                if (agent is IEmotionAware emotionAware)
                {
                    emotionAware.ReceiveEmotion(emotion, amount);
                }
            }
        }
    }

    // Optional: Interface for emotion-aware agents
    public interface IEmotionAware
    {
        void ReceiveEmotion(string emotion, double amount);
    }
}