using Analyzer.BeatmapScanner.Data;
using System.Text.Json.Serialization;

namespace RatingAPI.Controllers
{
    // Speed / tech / acc difficulty of a map over time with a speed modifier, one point per second of real time (song
    // seconds / timescale) from Start:
    // - speed: average Analyzer SwingDiff of the swings within Window seconds around it
    // - tech: average Analyzer SwingTech of those swings
    // - acc: average acc lost (1 - the Acc AI's predicted acc of a note, of its 15 accuracy points) of the notes there
    // A window with fewer swings (notes for acc) than the map's typical one (median of its windows that have any) counts in
    // proportion, so a lone swing in a sparse part or the cut window at the map's ends isn't a peak on its own.
    // Raw values, each in its own unit. Built by RatingsController.GetBLRatings from the swings and note predictions the
    // ratings come from (RatingResult.Breakdown).
    public class RatingBreakdown
    {
        public const double Window = 4;

        [JsonPropertyName("start")]
        public double Start { get; set; }
        [JsonPropertyName("speed")]
        public double[] Speed { get; set; } = [];
        [JsonPropertyName("tech")]
        public double[] Tech { get; set; } = [];
        [JsonPropertyName("acc")]
        public double[] Acc { get; set; } = [];

        // accs and noteTimes: InferPublish.PredictHitsForMap's (song seconds)
        public static RatingBreakdown? Compute(List<SwingData> swingData, List<float> accs, List<double> noteTimes, double timescale)
        {
            if (swingData.Count == 0) return null;
            var swings = swingData
                .Select(s => (Time: s.Cubes[0].Seconds / timescale, s.SwingDiff, s.SwingTech))
                .OrderBy(s => s.Time).ToArray();
            var notes = accs.Select((a, i) => (Time: noteTimes[i] / timescale, Lost: 1 - (double)a)).OrderBy(n => n.Time).ToArray();

            double start = Math.Floor(swings[0].Time), end = Math.Ceiling(swings[^1].Time);
            int count = (int)(end - start) + 1;
            var speed = new double[count];
            var tech = new double[count];
            var acc = new double[count];
            var swingCounts = new int[count];
            var noteCounts = new int[count];
            int sLo = 0, sHi = 0, nLo = 0, nHi = 0;
            for (int i = 0; i < count; i++)
            {
                double t = start + i, lo = t - Window / 2, hi = t + Window / 2;
                while (sLo < swings.Length && swings[sLo].Time < lo) sLo++;
                while (sHi < swings.Length && swings[sHi].Time <= hi) sHi++;
                while (nLo < notes.Length && notes[nLo].Time < lo) nLo++;
                while (nHi < notes.Length && notes[nHi].Time <= hi) nHi++;
                swingCounts[i] = sHi - sLo;
                noteCounts[i] = nHi - nLo;
                if (sHi > sLo)
                {
                    for (int j = sLo; j < sHi; j++) { speed[i] += swings[j].SwingDiff; tech[i] += swings[j].SwingTech; }
                    speed[i] /= sHi - sLo;
                    tech[i] /= sHi - sLo;
                }
                if (nHi > nLo)
                {
                    for (int j = nLo; j < nHi; j++) acc[i] += notes[j].Lost;
                    acc[i] /= nHi - nLo;
                }
            }
            static double Typical(int[] counts)
            {
                var filled = counts.Where(c => c > 0).OrderBy(c => c).ToArray();
                return filled.Length > 0 ? filled[filled.Length / 2] : 1;
            }
            double typicalSwings = Typical(swingCounts), typicalNotes = Typical(noteCounts);
            for (int i = 0; i < count; i++)
            {
                double swingFill = Math.Min(1, swingCounts[i] / typicalSwings), noteFill = Math.Min(1, noteCounts[i] / typicalNotes);
                speed[i] *= swingFill;
                tech[i] *= swingFill;
                acc[i] *= noteFill;
            }
            return new RatingBreakdown { Start = start, Speed = speed, Tech = tech, Acc = acc };
        }
    }
}
