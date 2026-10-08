using Analyzer.BeatmapScanner.Data;
using beatleader_analyzer.BeatmapScanner.Data;
using Parser.Map.Difficulty.V3.Base;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RatingAPI.Controllers
{
    /// <summary>Where the predicted accuracy behind AccRating comes from.</summary>
    public enum AccSource
    {
        /// <summary>The ONNX sequence model (model_sleep_bl.onnx) + ScaleFarmability.</summary>
        ML,
        /// <summary>The ML-free linear model over analyzer features (AccDifficultyModel).</summary>
        Algorithm,
    }

    /// <summary>
    /// Map-level features for the algorithmic accuracy-difficulty model, computed from the analyzer's own output.
    /// Every feature is a plain aggregate of a <see cref="SwingData"/> quantity or of the map, so the model is a
    /// documented weighted sum (see acc_model.json for weights). Time-based quantities are expressed in played
    /// time: the analyzer scales swing speed / NJS by the speed modifier but not cube times or SwingFrequency, so
    /// frequency, density, length and BPM are multiplied (divided) by the timescale here.
    /// </summary>
    public static class AccDifficultyFeatures
    {
        private static readonly string[] SwingColumns =
            { "swing_speed", "frequency", "angle_strain", "reposition", "rotation", "hit_distance", "stress", "swing_diff", "swing_tech", "njs" };
        private static readonly string[] PeakColumns = { "swing_diff", "swing_speed", "swing_tech", "frequency" };
        private static readonly int[] PeakWindows = { 8, 32, 128 };
        private static readonly double[] Quantiles = { 0.5, 0.9, 0.99 };

        public static readonly string[] Names = BuildNames();

        private static string[] BuildNames()
        {
            var names = new List<string>();
            foreach (var c in SwingColumns)
            {
                names.Add($"{c}_mean");
                foreach (var q in Quantiles) names.Add($"{c}_p{(int)Math.Round(q * 100)}");
            }
            foreach (var c in PeakColumns)
                foreach (var w in PeakWindows) names.Add($"{c}_peak{w}");
            names.AddRange(new[] {
                "frac_parity", "frac_dot", "frac_chain", "frac_multi", "frac_stream", "frac_linear", "frac_bomb", "frac_forehand",
                "hand_balance", "frac_freq_gt4", "frac_freq_gt6", "frac_freq_gt8",
                "log_swings", "swing_density", "log_len", "log_notes", "note_density", "bpm",
                "pass", "tech", "low_note_nerf", "linear_pct", "multi_pct", "parity_errors",
                // layout and reading, added from the score/algorithm disagreement analysis (Analysis/py/a15*.py)
                "frac_horizontal", "frac_diagonal", "frac_cross", "frac_outer", "frac_top_row", "frac_bottom_row",
                "jump_distance", "reaction_time", "arcs_per_note", "wall_cover", "walls_per_s",
                // repetitive maps score better than their swing quantities imply (Analysis/py/a20_learning.py)
                "repetition_2", "repetition_4", "repetition_8",
                // One Saber characteristic (its difficulty level; skill sensitivity is AccModelSpec.ModeSkillScale)
                "one_saber",
            });
            return names.ToArray();
        }

        /// <summary>
        /// Half jump duration (beats) and jump distance (m) as the game spawns notes (BeatmapObjectSpawnMovementData).
        /// </summary>
        public static (double halfJumpBeats, double jumpDistance) HalfJump(double njs, double bpm, double offset)
        {
            double secondsPerBeat = 60.0 / Math.Max(bpm, 1e-3), hjd = 4.0;
            while (njs * secondsPerBeat * hjd > 17.999) hjd /= 2;
            hjd = Math.Max(hjd + offset, 0.25);
            return (hjd, njs * secondsPerBeat * hjd * 2);
        }

        /// <param name="infoNjs">the difficulty's NJS from Info.dat (0: median note NJS)</param>
        /// <param name="noteJumpOffset">the difficulty's note jump start beat offset from Info.dat</param>
        public static double[] Compute(Ratings ratings, DifficultyV3 mapdata, double bpm, double timescale, double njsMult,
            double infoNjs = 0, double noteJumpOffset = 0)
        {
            double ts = timescale <= 0 ? 1 : timescale;
            var swings = ratings.SwingData
                .Select((s, i) => (s, i))
                .OrderBy(x => x.s.Cubes[0].Seconds).ThenBy(x => x.s.Cubes[0].Type).ThenBy(x => x.i)
                .Select(x => x.s)
                .ToList();
            int n = swings.Count;
            var f = new Dictionary<string, double>(Names.Length);

            double[] Col(string name) => name switch
            {
                "swing_speed" => swings.Select(s => s.SwingSpeed).ToArray(),
                "frequency" => swings.Select(s => s.SwingFrequency * ts).ToArray(),
                "angle_strain" => swings.Select(s => s.AngleStrain).ToArray(),
                "reposition" => swings.Select(s => s.RepositioningDistance).ToArray(),
                "rotation" => swings.Select(s => s.RotationAmount).ToArray(),
                "hit_distance" => swings.Select(s => s.HitDistance).ToArray(),
                "stress" => swings.Select(s => s.Stress).ToArray(),
                "swing_diff" => swings.Select(s => s.SwingDiff).ToArray(),
                "swing_tech" => swings.Select(s => s.SwingTech).ToArray(),
                "njs" => swings.Select(s => (double)s.Cubes[0].Njs * ts * njsMult).ToArray(),
                _ => throw new ArgumentException(name),
            };

            var cols = SwingColumns.ToDictionary(c => c, Col);
            foreach (var c in SwingColumns)
            {
                var v = cols[c];
                f[$"{c}_mean"] = n > 0 ? v.Average() : 0;
                var sorted = v.OrderBy(x => x).ToArray();
                foreach (var q in Quantiles) f[$"{c}_p{(int)Math.Round(q * 100)}"] = Quantile(sorted, q);
            }
            foreach (var c in PeakColumns)
                foreach (var w in PeakWindows) f[$"{c}_peak{w}"] = RollingPeak(cols[c], w);

            double Frac(Func<SwingData, bool> pred) => n > 0 ? swings.Count(pred) / (double)n : 0;
            f["frac_parity"] = Frac(s => s.ParityErrors);
            f["frac_dot"] = Frac(s => s.Cubes[0].CutDirection == 8);
            f["frac_chain"] = Frac(s => s.Cubes[0].Chain);
            f["frac_multi"] = Frac(s => s.Cubes.Count > 1);
            f["frac_stream"] = Frac(s => s.IsStream);
            f["frac_linear"] = Frac(s => s.IsLinear);
            f["frac_bomb"] = Frac(s => s.BombAvoidance);
            f["frac_forehand"] = Frac(s => s.Forehand);
            f["hand_balance"] = n > 0 ? swings.Average(s => (double)s.Cubes[0].Type) : 0;
            var freq = cols["frequency"];
            f["frac_freq_gt4"] = n > 0 ? freq.Count(x => x > 4) / (double)n : 0;
            f["frac_freq_gt6"] = n > 0 ? freq.Count(x => x > 6) / (double)n : 0;
            f["frac_freq_gt8"] = n > 0 ? freq.Count(x => x > 8) / (double)n : 0;

            double swingSpan = n > 1 ? (swings[^1].Cubes[0].Seconds - swings[0].Cubes[0].Seconds) / ts : 0;
            f["log_swings"] = Math.Log(Math.Max(n, 1));
            f["swing_density"] = n / Math.Max(swingSpan, 1.0);

            int notes = mapdata.Notes.Count;
            double length = notes > 0 ? (mapdata.Notes.Max(x => x.Seconds) - mapdata.Notes.Min(x => x.Seconds)) / ts : 0;
            f["log_len"] = Math.Log(Math.Max(length, 5.0));
            f["log_notes"] = Math.Log(Math.Max(notes, 1));
            f["note_density"] = notes / Math.Max(length, 1.0);
            f["bpm"] = bpm * ts;

            f["pass"] = ratings.ClassicPassRating;                              // classic rating, whatever PassModel selects
            f["tech"] = ratings.TechRating;
            f["low_note_nerf"] = ratings.LowNoteNerf;
            f["linear_pct"] = ratings.LinearPercentage;
            f["multi_pct"] = ratings.MultiPercentage;
            f["parity_errors"] = ratings.Statistics.ParityErrors;

            // layout of each swing's first note (lane X 0-3, layer Y 0-2; note type 0 = left/red, 1 = right/blue)
            f["frac_horizontal"] = Frac(s => s.Cubes[0].CutDirection is 2 or 3);
            f["frac_diagonal"] = Frac(s => s.Cubes[0].CutDirection is >= 4 and <= 7);
            f["frac_cross"] = Frac(s => (s.Cubes[0].Type == 0 && s.Cubes[0].X == 3) || (s.Cubes[0].Type == 1 && s.Cubes[0].X == 0));
            f["frac_outer"] = Frac(s => s.Cubes[0].X is 0 or 3);
            f["frac_top_row"] = Frac(s => s.Cubes[0].Y == 2);
            f["frac_bottom_row"] = Frac(s => s.Cubes[0].Y == 0);

            // reading: jump distance and reaction time in played time (NJS and BPM scaled like the analyzer's NJS feature)
            double njs = infoNjs > 0 ? infoNjs : (n > 0 ? Quantile(swings.Select(s => (double)s.Cubes[0].Njs).OrderBy(x => x).ToArray(), 0.5) : 10);
            var (hjd, jd) = HalfJump(njs * ts * njsMult, bpm * ts, noteJumpOffset);
            f["jump_distance"] = jd;
            f["reaction_time"] = hjd * 60.0 / Math.Max(bpm * ts, 1e-3);

            // visual load: arcs, walls
            double spanBeats = notes > 1 ? mapdata.Notes.Max(x => x.Beats) - mapdata.Notes.Min(x => x.Beats) : 0;
            f["arcs_per_note"] = mapdata.Arcs.Count / (double)Math.Max(notes, 1);
            f["wall_cover"] = Math.Min(mapdata.Walls.Sum(w => (double)Math.Max(w.DurationInBeats, 0)) / Math.Max(spanBeats, 1.0), 5.0);
            f["walls_per_s"] = mapdata.Walls.Count / Math.Max(length, 1.0);

            // repetition: share of L-swing sequences (hand, lane, layer, cut direction of each swing's first note) seen earlier in the map
            var tokens = swings.Select(s => $"{s.Cubes[0].Type}{s.Cubes[0].X}{s.Cubes[0].Y}{s.Cubes[0].CutDirection}").ToArray();
            foreach (int len in new[] { 2, 4, 8 })
            {
                int sequences = tokens.Length - len + 1;
                if (sequences < 2) { f[$"repetition_{len}"] = 0; continue; }
                var seen = new HashSet<string>();
                for (int i = 0; i < sequences; i++) seen.Add(string.Join("|", tokens, i, len));
                f[$"repetition_{len}"] = 1 - seen.Count / (double)sequences;
            }
            f["one_saber"] = ratings.Characteristic == "OneSaber" ? 1 : 0;

            return Names.Select(name => f[name]).ToArray();
        }

        /// <summary>numpy.quantile default ("linear") on a sorted array.</summary>
        private static double Quantile(double[] sorted, double q)
        {
            if (sorted.Length == 0) return 0;
            double pos = q * (sorted.Length - 1);
            int lo = (int)Math.Floor(pos), hi = (int)Math.Ceiling(pos);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        /// <summary>Largest mean over any window of consecutive swings (whole-map mean when shorter than the window).</summary>
        private static double RollingPeak(double[] v, int window)
        {
            if (v.Length == 0) return 0;
            if (v.Length < window) return v.Average();
            double sum = 0;
            for (int i = 0; i < window; i++) sum += v[i];
            double best = sum;
            for (int i = window; i < v.Length; i++)
            {
                sum += v[i] - v[i - window];
                if (sum > best) best = sum;
            }
            return best / window;
        }
    }

    /// <summary>Weights and calibration of the algorithmic accuracy-difficulty model (acc_model.json).</summary>
    public class AccModelSpec
    {
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("features")] public List<string> Features { get; set; } = new();
        [JsonPropertyName("mean")] public double[] Mean { get; set; } = Array.Empty<double>();
        [JsonPropertyName("scale")] public double[] Scale { get; set; } = Array.Empty<double>();
        [JsonPropertyName("coef")] public double[] Coef { get; set; } = Array.Empty<double>();
        [JsonPropertyName("intercept")] public double Intercept { get; set; }
        /// <summary>Reference skill a_ref: predictedAcc = 1 - exp(difficulty' - a_ref).</summary>
        [JsonPropertyName("reference_skill")] public double ReferenceSkill { get; set; }
        /// <summary>
        /// Spread calibration: difficulty' = center + scale * (difficulty - center). 1 keeps the full score-implied spread
        /// (~1.2x the ML's); smaller values flatten the PP-versus-skill profile at the cost of fairness between maps
        /// (see portaBLe Analysis/ALGO_ACC_TEST.md, "Calibration").
        /// </summary>
        [JsonPropertyName("difficulty_scale")] public double DifficultyScale { get; set; } = 1.0;
        [JsonPropertyName("difficulty_center")] public double DifficultyCenter { get; set; } = 0.0;
        [JsonPropertyName("min_predicted_acc")] public double MinPredictedAcc { get; set; } = 0.5;
        [JsonPropertyName("max_predicted_acc")] public double MaxPredictedAcc { get; set; } = 0.9995;
        /// <summary>
        /// Calibration of speed modifiers: points (timescale, k). The modded difficulty is d_base + k * (d_mod - d_base),
        /// k interpolated linearly in timescale (fitted on SS/FS/SF scores; the raw feature response overstates the average shift).
        /// </summary>
        [JsonPropertyName("speed_shift_scale")] public double[][]? SpeedShiftScale { get; set; }
        /// <summary>
        /// Points (timescale, L): the calibrated shift k * (d_mod - d_base) is clamped to [-L, L], L interpolated in timescale.
        /// L is the range in which real SS/FS/SF scores confirmed the shift (99th percentile over maps with speed scores);
        /// beyond it the linear model only extrapolates (e.g. SF on the hardest maps, which nobody plays at that speed).
        /// </summary>
        [JsonPropertyName("speed_shift_limit")] public double[][]? SpeedShiftLimit { get; set; }
        /// <summary>
        /// Skill sensitivity per characteristic (default 1): log(1 - acc) = d - scale * skill. One Saber players' error rates follow their
        /// overall skill only ~0.91x as strongly (its specialists are better at it than their overall skill says), so its predicted
        /// accuracy at the reference skill is 1 - exp(d - scale * reference_skill); portaBLe steepens its PP curve by 1 / scale.
        /// </summary>
        [JsonPropertyName("mode_skill_scale")] public Dictionary<string, double>? ModeSkillScale { get; set; }
    }

    /// <summary>
    /// ML-free accuracy difficulty: a linear model, fitted on score data (Analysis/py/fit_acc_model.py), predicting the
    /// score-implied map difficulty d = log error rate of an average player from the analyzer features above.
    /// The prediction is turned into a predicted accuracy at a fixed reference skill, so the rest of the rating
    /// pipeline (AccRating, stars, PP) is unchanged.
    /// </summary>
    public class AccDifficultyModel
    {
        private readonly AccModelSpec _spec;
        private readonly int[] _index;

        public AccModelSpec Spec => _spec;

        private static readonly Lazy<AccDifficultyModel?> _default = new(() => LoadEmbedded("acc_model.json"));
        /// <summary>The model embedded in the assembly (null when no acc_model.json is embedded).</summary>
        public static AccDifficultyModel? Default => _default.Value;

        public AccDifficultyModel(AccModelSpec spec)
        {
            _spec = spec;
            var names = AccDifficultyFeatures.Names.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);
            _index = spec.Features.Select(f => names.TryGetValue(f, out var i) ? i : throw new InvalidDataException($"acc_model.json: unknown feature '{f}'")).ToArray();
            if (spec.Mean.Length != _index.Length || spec.Scale.Length != _index.Length || spec.Coef.Length != _index.Length)
                throw new InvalidDataException("acc_model.json: features/mean/scale/coef length mismatch");
        }

        public static AccDifficultyModel? LoadEmbedded(string name)
        {
            foreach (var asm in new[] { Assembly.GetExecutingAssembly(), Assembly.GetEntryAssembly() })
            {
                if (asm == null) continue;
                var resource = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
                if (resource == null) continue;
                using var stream = asm.GetManifestResourceStream(resource)!;
                var spec = JsonSerializer.Deserialize<AccModelSpec>(stream);
                return spec == null ? null : new AccDifficultyModel(spec);
            }
            return null;
        }

        public static AccDifficultyModel Load(string path) =>
            new AccDifficultyModel(JsonSerializer.Deserialize<AccModelSpec>(File.ReadAllText(path)) ?? throw new InvalidDataException(path));

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AccDifficultyModel> _loaded = new();
        /// <summary>Load once per path (controllers are created per request).</summary>
        public static AccDifficultyModel LoadCached(string path) => _loaded.GetOrAdd(Path.GetFullPath(path), Load);

        /// <summary>Predicted score-implied difficulty d (log error rate units) from a full feature vector.</summary>
        public double PredictDifficulty(double[] features)
        {
            double d = _spec.Intercept;
            for (int k = 0; k < _index.Length; k++)
            {
                double x = features[_index[k]];
                double scale = _spec.Scale[k] == 0 ? 1 : _spec.Scale[k];
                d += _spec.Coef[k] * (x - _spec.Mean[k]) / scale;
            }
            return d;
        }

        /// <summary>Skill sensitivity of a characteristic (1 unless the spec sets one).</summary>
        public double SkillScale(string? characteristic) =>
            characteristic != null && _spec.ModeSkillScale != null && _spec.ModeSkillScale.TryGetValue(characteristic, out var v) ? v : 1.0;

        public double PredictedAccFromDifficulty(double difficulty, string? characteristic = null)
        {
            double d = _spec.DifficultyCenter + _spec.DifficultyScale * (difficulty - _spec.DifficultyCenter);
            return Math.Clamp(1 - Math.Exp(d - SkillScale(characteristic) * _spec.ReferenceSkill), _spec.MinPredictedAcc, _spec.MaxPredictedAcc);
        }

        /// <summary>Speed-modifier shrink factor k(timescale) from the spec (1 when not calibrated).</summary>
        public double SpeedShiftScale(double timescale) => Interpolate(_spec.SpeedShiftScale, timescale) ?? 1.0;

        /// <summary>Largest allowed |shift| for a timescale (null = unlimited).</summary>
        public double? SpeedShiftLimit(double timescale) => Interpolate(_spec.SpeedShiftLimit, timescale);

        /// <summary>Piecewise-linear interpolation through (x, y) points, constant beyond the ends; null without points.</summary>
        private static double? Interpolate(double[][]? points, double x)
        {
            if (points == null || points.Length == 0) return null;
            var p = points.OrderBy(q => q[0]).ToArray();
            if (x <= p[0][0]) return p[0][1];
            if (x >= p[^1][0]) return p[^1][1];
            for (int i = 1; i < p.Length; i++)
            {
                if (x <= p[i][0])
                {
                    double t = (x - p[i - 1][0]) / (p[i][0] - p[i - 1][0]);
                    return p[i - 1][1] + t * (p[i][1] - p[i - 1][1]);
                }
            }
            return p[^1][1];
        }

        /// <summary>
        /// Predicted difficulty for a (possibly speed-modified) map. For modifiers pass the analyzer output of the
        /// unmodified map as <paramref name="baseRatings"/> so the calibrated shift can be applied.
        /// </summary>
        public double? Difficulty(Ratings ratings, DifficultyV3 mapdata, double bpm, double timescale, double njsMult, Ratings? baseRatings = null,
            double infoNjs = 0, double noteJumpOffset = 0)
        {
            if (ratings == null || ratings.SwingData.Count == 0) return null;
            double d = PredictDifficulty(AccDifficultyFeatures.Compute(ratings, mapdata, bpm, timescale, njsMult, infoNjs, noteJumpOffset));
            bool modded = Math.Abs(timescale - 1) > 1e-9 || Math.Abs(njsMult - 1) > 1e-9;
            if (modded && baseRatings != null && baseRatings.SwingData.Count > 0)
            {
                double d0 = PredictDifficulty(AccDifficultyFeatures.Compute(baseRatings, mapdata, bpm, 1, 1, infoNjs, noteJumpOffset));
                double shift = SpeedShiftScale(timescale) * (d - d0);
                if (SpeedShiftLimit(timescale) is { } limit) shift = Math.Clamp(shift, -limit, limit);
                d = d0 + shift;
            }
            return d;
        }

        /// <summary>Predicted accuracy at the reference skill, or null when the map has no swings.</summary>
        public double? PredictedAcc(Ratings ratings, DifficultyV3 mapdata, double bpm, double timescale, double njsMult, Ratings? baseRatings = null,
            double infoNjs = 0, double noteJumpOffset = 0)
        {
            var d = Difficulty(ratings, mapdata, bpm, timescale, njsMult, baseRatings, infoNjs, noteJumpOffset);
            return d == null ? null : PredictedAccFromDifficulty(d.Value, ratings.Characteristic);
        }
    }
}
