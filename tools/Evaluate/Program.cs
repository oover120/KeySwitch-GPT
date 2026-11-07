using System.Diagnostics;
using System.Text.Json;
using KeySwitch.Core;
using KeySwitch.Data;

var profiles = ModelLoader.Load(); var detector = new LanguageDetector(profiles);
var holdout = args.Contains("--holdout");
var rows = new List<object>(); var passed = true;
foreach (var profile in profiles)
{
    var other = profiles.Single(p => p != profile);
    var corpus = holdout ? File.ReadLines(Path.Combine(AppContext.BaseDirectory, "Corpus", profile.Layout.Id + "-holdout.tsv"))
        .Select(line => line.Split('\t')).Select(a => (Word: a[0], Count: int.Parse(a[1])))
        : profile.Frequencies.OrderByDescending(p => p.Value).Take(10000).Select(p => (Word: p.Key, Count: 1));
    long total = 0, correct = 0, falseSwitches = 0, weightedTotal = 0, weightedCorrect = 0;
    var latencies = new List<double>();
    var errors = new List<string>();
    foreach (var (word, count) in corpus.Where(p => p.Word.Length >= 3))
    {
        total++; weightedTotal += count;
        if (detector.Detect(word, profile.Layout.Id) is not null) falseSwitches++;
        var wrong = profile.Layout.ConvertTo(word, other.Layout);
        var watch = Stopwatch.StartNew(); var detection = detector.Detect(wrong, other.Layout.Id); watch.Stop(); latencies.Add(watch.Elapsed.TotalMilliseconds);
        if (detection?.Replacement == word) { correct++; weightedCorrect += count; }
        else if (args.Contains("--errors") && errors.Count < 60) errors.Add(word + " | " + wrong + " | " + (detection?.Replacement ?? "skip") + " | sourceKnown=" + other.Knows(wrong) + " sourceCoverage=" + other.Coverage(wrong).ToString("F2") + " targetKnown=" + profile.Knows(word) + " targetCoverage=" + profile.Coverage(word).ToString("F2"));
    }
    latencies.Sort(); var recall = total == 0 ? 0 : (double)correct / total; var fpr = total == 0 ? 0 : (double)falseSwitches / total;
    var pass = recall >= 0.98 && fpr <= 0.005; passed &= pass;
    rows.Add(new
    {
        language = profile.Layout.Id,
        corpus = holdout ? "UD r2.15 held-out test, all alphabetic words >=3" : "top 10000 dictionary entries >=3 (in-vocabulary)",
        total,
        correct,
        recall,
        falseSwitches,
        falsePositiveRate = fpr,
        tokenWeightedRecall = weightedTotal == 0 ? 0 : (double)weightedCorrect / weightedTotal,
        detectionP95Milliseconds = latencies.Count == 0 ? 0 : latencies[(int)(latencies.Count * 0.95)],
        passed = pass,
        errors
    });
}
Console.WriteLine(JsonSerializer.Serialize(new { thresholds = new { recall = 0.98, falsePositiveRate = 0.005 }, results = rows, passed }, new JsonSerializerOptions { WriteIndented = true }));
return passed ? 0 : 2;
