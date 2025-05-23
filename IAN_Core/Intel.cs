using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;

namespace IAN_Core
{
    public class Intel
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public class Message
        {
            public string role { get; set; }
            public string content { get; set; }
        }

        public class CompletionRequest
        {
            public string model { get; set; }
            public List<Message> messages { get; set; }
            public double temperature { get; set; }
            public int max_tokens { get; set; }
            public bool stream { get; set; }
        }

        public class CompletionResponse
        {
            public List<Choice> choices { get; set; }
            public class Choice
            {
                public Message message { get; set; }
            }
        }

        public static async Task<string> QueryLLMAsync(
            string endpoint,
            string model,
            List<Message> messages,
            double temperature = 0.7,
            int max_tokens = -1,
            bool stream = false)
        {
            var request = new CompletionRequest
            {
                model = model,
                messages = messages,
                temperature = temperature,
                max_tokens = max_tokens,
                stream = stream
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(endpoint, content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var completion = JsonSerializer.Deserialize<CompletionResponse>(responseString);

            return completion?.choices?[0]?.message?.content ?? string.Empty;
        }
    }
}