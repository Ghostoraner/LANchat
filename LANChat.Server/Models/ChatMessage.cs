using System;
using System.Text.Json.Serialization;

namespace LANChat.Server.Models
{
    public class ChatMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "message"; 

        [JsonPropertyName("sender")]
        public string Sender { get; set; } = "System";

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        
        [JsonPropertyName("to")]
        public string? To { get; set; }

        
        [JsonPropertyName("transferId")]
        public string? TransferId { get; set; }

        [JsonPropertyName("fileName")]
        public string? FileName { get; set; }

        [JsonPropertyName("fileSize")]
        public long? FileSize { get; set; }

        [JsonPropertyName("chunkIndex")]
        public int? ChunkIndex { get; set; }

        [JsonPropertyName("totalChunks")]
        public int? TotalChunks { get; set; }
    }
}