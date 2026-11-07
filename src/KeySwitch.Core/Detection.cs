using System.Collections.Frozen;
namespace KeySwitch.Core;

public sealed class LanguageProfile
{
    public KeyboardLayout Layout { get; }
    public IReadOnlyDictionary<string, int> Frequencies { get; }
    private readonly IReadOnlyDictionary<string, int> ngrams;
    private readonly HashSet<string> protectedWords;
    public LanguageProfile(KeyboardLayout layout, IReadOnlyDictionary<string, int> frequencies, IReadOnlyDictionary<string, int> grams)
    {
        Layout = layout; Frequencies = frequencies is PackedLexicon ? frequencies : frequencies.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        ngrams = grams is PackedLexicon ? grams : grams.ToFrozenDictionary(StringComparer.Ordinal);
        protectedWords = (frequencies is PackedLexicon packed ? packed.MostFrequent(100) : frequencies.OrderByDescending(p => p.Value).Take(100).Select(p => p.Key)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
    public bool IsProtected(string word) => protectedWords.Contains(word);
    public bool Knows(string word) => Frequencies.ContainsKey(word);
    public int Frequency(string word) => Frequencies.GetValueOrDefault(word);
    public double Coverage(string word)
    {
        var value = "^" + word.ToLowerInvariant() + "$";
        double present = 0, total = 0;
        for (var n = 3; n <= 4; n++) for (var i = 0; i <= value.Length - n; i++)
            { total++; if (ngrams.TryGetValue(value.Substring(i, n), out var count) && count >= 2) present++; }
        return total == 0 ? 0 : present / total;
    }
    public double Plausibility(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.StartsWith('ъ') || lower.StartsWith('ь') || lower.Contains("ъь", StringComparison.Ordinal)) return 0;
        return Knows(lower) ? 1 : Coverage(lower) * 0.7;
    }
}

public sealed record Detection(string Replacement, string TargetLanguage, double Confidence);

public sealed class DetectionEvidence
{
    public double? ScoreA { get; internal set; }
    public double? ScoreB { get; internal set; }
    public double? Confidence { get; internal set; }
    public string Reason { get; internal set; } = "no-plausible-alternative";
}

public sealed class LanguageDetector(IEnumerable<LanguageProfile> profiles)
{
    public const double DefaultThreshold = 0.88;
    public IReadOnlyList<LanguageProfile> Profiles { get; } = profiles.ToArray();
    public Detection? Detect(string input, string sourceId, double threshold = DefaultThreshold, bool early = false,
        IReadOnlyDictionary<string, string>? learned = null, DetectionEvidence? evidence = null)
    {
        if (input.Length < 3) return Reject(evidence, "word-too-short");
        if (input.Length > WordBuffer.Capacity) return Reject(evidence, "word-too-long");
        if (PasswordHeuristic.IsSensitive(input)) return Reject(evidence, "password-heuristic");
        var source = Profiles.FirstOrDefault(p => p.Layout.Id == sourceId);
        if (source is null) return Reject(evidence, "unsupported-layout");
        if (!early && learned?.TryGetValue(sourceId + ":" + input.ToLowerInvariant(), out var confirmedTarget) == true)
        {
            var confirmed = Profiles.FirstOrDefault(p => p.Layout.Id == confirmedTarget);
            if (confirmed is not null && confirmed != source)
            {
                var replacement = source.Layout.ConvertTo(input, confirmed.Layout);
                Record(evidence, source, confirmed, input, replacement, 1, "learned");
                return new(replacement, confirmedTarget, 1);
            }
        }
        var original = input.TrimEnd('.', ',', '!', '?', ':', ';', '"', '\'', ')', ']', '}');
        if (source.Knows(input) || source.Knows(original))
        {
            if (evidence is not null)
            {
                evidence.ScoreA = 1;
                evidence.ScoreB = Profiles.Where(p => p != source)
                    .Select(p => p.Plausibility(source.Layout.ConvertTo(original, p.Layout))).DefaultIfEmpty().Max();
            }
            return Reject(evidence, "source-in-dictionary");
        }
        if (input.Contains('@') || input.Contains("://", StringComparison.Ordinal) || input.Contains('_') || input.Any(char.IsDigit))
            return Reject(evidence, "identifier-like");
        Detection? best = null;
        foreach (var target in Profiles.Where(p => p != source))
        {
            Detection? fallback = null;
            // Full candidate first: EN punctuation keys may be RU letters, including the final key.
            for (var cut = 0; cut <= input.Length - original.Length; cut++)
            {
                var part = input[..(input.Length - cut)];
                if (part.Length < 3) continue;
                var mapped = source.Layout.ConvertTo(part, target.Layout);
                if (!mapped.All(char.IsLetter)) continue;
                var a = source.Plausibility(part);
                var coverage = target.Coverage(mapped);
                if (!target.Knows(mapped) && a >= 0.70) continue;
                var confidence = target.Knows(mapped) ? 0.90 + 0.09 * coverage
                    : mapped.Length >= 4 && coverage >= 0.5 && source.Coverage(part) <= coverage - 0.25 ? 0.88 + 0.10 * coverage : 0;
                if (early && (part.Length < 5 || target.Frequency(mapped) < 10000 || a > 0.12)) continue;
                var limit = early ? Math.Max(0.985, threshold) : threshold;
                if (evidence is not null && (evidence.Confidence is null || confidence > evidence.Confidence))
                    Record(evidence, source, target, part, mapped, confidence, confidence > 0 ? "below-threshold" : "no-plausible-alternative");
                if (confidence >= limit && (best is null || confidence > best.Confidence))
                {
                    var candidate = new Detection(mapped + input[(input.Length - cut)..], target.Layout.Id, confidence);
                    if (target.Knows(mapped)) { best = candidate; break; }
                    fallback ??= candidate;
                }
            }
            best ??= fallback;
        }
        if (best is not null && evidence is not null)
        {
            var target = Profiles.First(p => p.Layout.Id == best.TargetLanguage);
            var mapped = best.Replacement.TrimEnd('.', ',', '!', '?', ':', ';', '"', '\'', ')', ']', '}');
            Record(evidence, source, target, input[..mapped.Length], mapped, best.Confidence, "accepted");
        }
        return best;
    }

    private static Detection? Reject(DetectionEvidence? evidence, string reason)
    {
        if (evidence is not null) evidence.Reason = reason;
        return null;
    }

    private static void Record(DetectionEvidence? evidence, LanguageProfile source, LanguageProfile target,
        string original, string mapped, double confidence, string reason)
    {
        if (evidence is null) return;
        evidence.ScoreA = source.Plausibility(original);
        evidence.ScoreB = target.Plausibility(mapped);
        evidence.Confidence = confidence;
        evidence.Reason = reason;
    }
}
