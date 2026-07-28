namespace Subtitles.Domain.Ai;

/// <summary>
/// Structured output of the Romanize stage. Like TranslateToEnglish, Romanized cues inherit
/// timing and sequence_number directly from the Native cues they were transliterated from
/// (docs/Database.md §2.6) — the model returns text only, one entry per input cue, in order.
/// </summary>
public sealed record RomanizeResult(IReadOnlyList<string> Transliterations)
{
    public static readonly JsonSchemaSpec Schema = new(
        "romanize_result",
        """
        {
          "type": "object",
          "properties": {
            "transliterations": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": ["transliterations"],
          "additionalProperties": false
        }
        """);
}
