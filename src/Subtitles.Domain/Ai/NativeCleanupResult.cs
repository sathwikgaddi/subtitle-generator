namespace Subtitles.Domain.Ai;

/// <summary>
/// Structured output of the NativeCleanup stage. Word indices reference positions in the
/// original transcript's word list (as given to the LLM), not literal timestamps — the LLM is
/// asked for cue boundaries and cleaned text only; exact millisecond timing is derived
/// afterward from the real ASR word timestamps, since asking an LLM to invent precise
/// timestamps is unreliable. See docs/Architecture.md §2.3.
/// </summary>
public sealed record NativeCleanupResult(IReadOnlyList<NativeCleanupCue> Cues)
{
    public static readonly JsonSchemaSpec Schema = new(
        "native_cleanup_result",
        """
        {
          "type": "object",
          "properties": {
            "cues": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "startWordIndex": { "type": "integer" },
                  "endWordIndex": { "type": "integer" },
                  "text": { "type": "string" }
                },
                "required": ["startWordIndex", "endWordIndex", "text"],
                "additionalProperties": false
              }
            }
          },
          "required": ["cues"],
          "additionalProperties": false
        }
        """);
}

public sealed record NativeCleanupCue(int StartWordIndex, int EndWordIndex, string Text);
