namespace Subtitles.Domain.Ai;

/// <summary>
/// Structured output of the GenerateHighlights stage. Per cue, the model sees all three
/// renderings (Native/English/Romanized) of that cue side by side — since they express the
/// same content, the model does the cross-language alignment itself by returning which word
/// indices (into each rendering's own word list) represent the same important concepts, rather
/// than this code trying to align translated word positions after the fact (unreliable — see
/// docs/Database.md §2.7's note on why English/Romanized word timing is null for the same
/// underlying reason).
/// </summary>
public sealed record GenerateHighlightsResult(IReadOnlyList<CueHighlights> Cues)
{
    public static readonly JsonSchemaSpec Schema = new(
        "generate_highlights_result",
        """
        {
          "type": "object",
          "properties": {
            "cues": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "sequenceNumber": { "type": "integer" },
                  "highlightedNativeWordIndices": { "type": "array", "items": { "type": "integer" } },
                  "highlightedEnglishWordIndices": { "type": "array", "items": { "type": "integer" } },
                  "highlightedRomanizedWordIndices": { "type": "array", "items": { "type": "integer" } }
                },
                "required": [
                  "sequenceNumber",
                  "highlightedNativeWordIndices",
                  "highlightedEnglishWordIndices",
                  "highlightedRomanizedWordIndices"
                ],
                "additionalProperties": false
              }
            }
          },
          "required": ["cues"],
          "additionalProperties": false
        }
        """);
}

public sealed record CueHighlights(
    int SequenceNumber,
    IReadOnlyList<int> HighlightedNativeWordIndices,
    IReadOnlyList<int> HighlightedEnglishWordIndices,
    IReadOnlyList<int> HighlightedRomanizedWordIndices);
