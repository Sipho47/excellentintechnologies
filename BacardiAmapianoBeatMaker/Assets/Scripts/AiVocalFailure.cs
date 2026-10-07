using System;
using System.Text.RegularExpressions;

public sealed class AiVocalFailure : Exception {
    public readonly string Stage;
    public readonly int HttpStatus;
    public AiVocalFailure(string stage, int status, string detail, Exception inner = null)
        : base(Clean(detail), inner) { Stage = stage; HttpStatus = status; }
    public static string Clean(string value) {
        string text = value ?? "Unknown failure";
        foreach (string name in new[] { "ELEVENLABS_API_KEY", "BACARDI_VOCAL_PROXY_TOKEN" }) {
            string secret = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(secret)) text = text.Replace(secret, "[REDACTED]");
        }
        text = Regex.Replace(text, @"(?i)(authorization|xi-api-key|api[_-]?key)\s*[""']?\s*[:=]\s*[""']?[^\r\n,}""']+", "$1: [REDACTED]");
        text = Regex.Replace(text, @"\bsk_[A-Za-z0-9_-]+", "[REDACTED]");
        return text;
    }
    public string UiMessage {
        get {
            string reason;
            switch (HttpStatus) {
                case 401: reason = "Invalid ElevenLabs API key"; break;
                case 402: reason = "Insufficient credits"; break;
                case 400: reason = "Invalid request"; break;
                case 429: reason = "Rate limit reached"; break;
                default: reason = Message.Replace('\n', ' ').Replace('\r', ' '); break;
            }
            if (reason.Length > 180) reason = reason.Substring(0, 180) + "…";
            return "ERROR" + (HttpStatus > 0 ? " " + HttpStatus : "") + ": " + reason + " (" + Stage + ")";
        }
    }
    public string Diagnostic { get { return "REAL AI vocal failure; stage=" + Stage + "; HTTP=" + (HttpStatus > 0 ? HttpStatus.ToString() : "N/A") + "; " + Message + (InnerException == null ? "" : "; " + Clean(InnerException.ToString())); } }
}
