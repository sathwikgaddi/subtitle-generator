namespace Subtitles.Domain.Ai;

/// <summary>
/// Structured output of the TranslateToEnglish stage. English cues inherit timing and
/// sequence_number directly from the Native cues they were translated from (docs/Database.md
/// §2.6) — the model only ever returns translated text, never timing, one entry per input cue
/// in the same order.
/// </summary>
public sealed record TranslateToEnglishResult(IReadOnlyList<string> Translations)
{
    public static readonly JsonSchemaSpec Schema = new(
        "translate_to_english_result",
        """
        {
          "type": "object",
          "properties": {
            "translations": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": ["translations"],
          "additionalProperties": false
        }
        """);
}
