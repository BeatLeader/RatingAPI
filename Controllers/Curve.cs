namespace RatingAPI.Controllers
{
    public class Curve
    {
        // The base acc curve (Pp.PpCalculator.CurveValue): acc pp = curve(acc) * AccWeight * acc rating^AccRatingPower
        public List<(double x, double y)> baseCurve = BaseTable.ToList();

        // Low acc lift (24 Oct): +20% up to 80%, fading to 0 at 92% (0.91: x1.017 ... 0.85: x1.117, 0.825: x1.158; 0.6-0.8
        // were 0.256, 0.296, 0.345, 0.404, 0.473, then 0.522, 0.581, 0.650, 0.729, 0.768). Maps players score low on were worth
        // less for them than their other maps: Extratongue / 999 ExpertPlus had 46% / 52% of the top 5000's scores at
        // 70-85% (11-13 star maps 20%), worth -0.176 / -0.150 (Dev, no acc reweight). Stars (a 96% score) and acc ratings
        // (the curve at the effective acc, 95%+) don't move. Simulated (simplified PlayerAdjusted fit, Dev): worth spread
        // 0.048 -> 0.041, maps above +0.08 132 -> 75, below -0.05 497 -> 377, those four +2.2% to +2.5%, Chrome Vox
        // ExpertPlus +3.1%.
        public static readonly (double x, double y)[] BaseTable =
        {
                (1.0, 7.424),
                (0.999, 6.241),
                (0.9975, 5.158),
                (0.995, 4.010),
                (0.9925, 3.241),
                (0.99, 2.700),
                (0.9875, 2.303),
                (0.985, 2.007),
                (0.9825, 1.786),
                (0.98, 1.618),
                (0.9775, 1.490),
                (0.975, 1.392),
                (0.9725, 1.315),
                (0.97, 1.256),
                (0.965, 1.167),
                (0.96, 1.094),
                (0.955, 1.039),
                (0.95, 1.000),
                (0.94, 0.931),
                (0.93, 0.867),
                (0.92, 0.813),
                (0.91, 0.781),
                (0.9, 0.753),
                (0.875, 0.699),
                (0.85, 0.649),
                (0.825, 0.605),
                (0.8, 0.568),
                (0.75, 0.485),
                (0.7, 0.414),
                (0.65, 0.355),
                (0.6, 0.307),
                (0.0, 0.000)
        };

        // Base curve for every map (maps without a usable predicted acc)
        public List<Point> GetCurve(LackMapCalculation lackRatings)
        {
            double nerf = CurveSettings.Current.LowNoteMultiplier(lackRatings.NoteCount, lackRatings.NoteSpan);
            return BaseTable.Select(p => new Point(p.x, p.y * nerf)).ToList();
        }

        // Per-map acc curve: ModifiableCurve (from the AccCurvePerMap branch) with all its parameters (A, C, K, W, K2, P2)
        // read from CurveSettings.Table by the map's predicted acc:
        // - K, W shape the middle part (60-95%), K2, P2 the upper part (95% to A);
        // - above A, the high-pass extension L(x) leaves A with the curve's slope there, which then keeps growing (C < 0,
        //   still exponential) or fades (0 < C <= 1; 0 = straight line).
        // The parameters only act relative to the default ModifiableCurve: value = base table(x) * modified(x) / default(x)
        // (the table interpolated smoothly, SmoothBaseValue),
        // so default parameters give exactly today's curve (the default ModifiableCurve is 3-6% above the table at 97-99.9%,
        // using it directly would raise most acc pp). Never above the base curve. Only map data is used, never scores.
        public List<Point> GetCurve(LackMapCalculation lackRatings, double predictedAcc)
        {
            var curves = PerMapCurves(predictedAcc);
            double tilt = SliderTilt(lackRatings, predictedAcc);
            var settings = CurveSettings.Current;
            // The low note nerf: its uniform part and its cut of the gain above the predicted acc (0: none)
            double nerf = settings.LowNoteUniformShare * (1 - settings.LowNoteMultiplier(lackRatings.NoteCount, lackRatings.NoteSpan));
            double cut = predictedAcc > 0.6 && predictedAcc < 1 ? settings.LowNoteGainCutAt(lackRatings.NoteCount, lackRatings.NoteSpan) : 0;
            double bonus = settings.LowNoteBonusAt(lackRatings.NoteCount, lackRatings.NoteSpan);
            bool gainCut = cut != 0 || bonus != 0;
            if (curves == null && tilt == 0 && !gainCut)
            {
                return GetCurve(lackRatings);
            }
            double a = curves?.modified.A ?? 1.0;

            // Base table points, 0.5% steps from 60% to 95%, 0.05% steps from 95% to 100%, and A
            var xs = BaseTable.Select(p => p.x)
                .Concat(Enumerable.Range(0, 71).Select(i => 0.6 + i * 0.005))
                .Concat(Enumerable.Range(0, 101).Select(i => 0.95 + i * 0.0005))
                .Append(a)
                .Concat(gainCut ? new[] { predictedAcc } : Array.Empty<double>())
                .Select(x => Math.Round(x, 6))
                .Where(x => x >= 0 && x <= 1)
                .Distinct()
                .OrderByDescending(x => x);

            double Raw(double x)
            {
                double value = curves == null ? BaseValue(x) : PerMapValue(curves.Value, x);
                if (tilt != 0)
                {
                    // Slider tilt: steeper around the predicted acc (unchanged there), up to SliderTiltRange away from it
                    double distance = Math.Clamp(x - predictedAcc, -settings.SliderTiltRange, settings.SliderTiltRange);
                    value *= Math.Exp(tilt * distance * 100);
                }
                return value;
            }
            double atPredicted = gainCut ? Raw(predictedAcc) : 0;
            return xs.Select(x => new Point(x, settings.LowNoteValue(Raw(x), x, atPredicted, predictedAcc, nerf, cut, bonus))).ToList();
        }

        // Sliders and curved sliders per swing of a rated map
        public static double SliderPercentage(global::beatleader_analyzer.BeatmapScanner.Data.Ratings ratings) =>
            ratings.SwingData.Count > 0 ? (ratings.Statistics.Sliders + ratings.Statistics.CurvedSliders) / (double)ratings.SwingData.Count : 0;

        // Slider tilt per 1% acc of a map's curve (0: none)
        private static double SliderTilt(LackMapCalculation lackRatings, double predictedAcc)
        {
            var settings = CurveSettings.Current;
            if (!settings.Enabled || settings.SliderTilt == 0 || predictedAcc <= 0.6 || predictedAcc >= 0.9995) return 0;
            return settings.SliderTilt * lackRatings.SliderPercentage;
        }

        // Value of the per-map curve (GetCurve above) at acc, computed directly instead of from the sampled points.
        // Used by the acc rating (AccRating.Curve).
        public static double PerMapValue(double predictedAcc, double acc)
        {
            var curves = PerMapCurves(predictedAcc);
            return curves == null ? BaseValue(acc) : PerMapValue(curves.Value, acc);
        }

        // The modified and default ModifiableCurve of a map, null when it gets the base curve
        private static (ModifiableCurve modified, ModifiableCurve standard)? PerMapCurves(double predictedAcc)
        {
            var settings = CurveSettings.Current;
            if (!settings.Enabled || predictedAcc <= 0.6 || predictedAcc >= 0.9995)
            {
                return null;
            }
            var row = settings.ParamsAt(predictedAcc);
            if (row.IsDefault)
            {
                return null;
            }
            double a = Math.Min(Math.Max(row.A, 0.951), 1.0);
            return (new ModifiableCurve { A = a, C = row.C, K = row.K, W = row.W, K2 = row.K2, P2 = row.P2 }, new ModifiableCurve());
        }

        // The base it scales is SmoothBaseValue: with the linear table the slope jumped up at every table point and fell inside
        // each segment (the ratio falls with acc), so per-map curves were bumpy above ~98.5% (99.3-99.55% at 97% predicted:
        // 0.10, 0.07, 0.12 per 0.05%). Same values at the table points (96% included), up to 1.34% lower between them.
        private static double PerMapValue((ModifiableCurve modified, ModifiableCurve standard) curves, double x)
        {
            double baseValue = SmoothBaseValue(x);
            double reference = curves.standard.GetValue(x);
            double value = reference > 0 ? baseValue * curves.modified.GetValue(x) / reference : baseValue;
            return Math.Min(baseValue, value);
        }

        public static double BaseValue(double acc)
        {
            int i = 0;
            for (; i < BaseTable.Length; i++)
            {
                if (BaseTable[i].x <= acc) break;
            }
            if (i == 0) i = 1;
            if (i >= BaseTable.Length) i = BaseTable.Length - 1;
            double middle_dis = (acc - BaseTable[i - 1].x) / (BaseTable[i].x - BaseTable[i - 1].x);
            return BaseTable[i - 1].y + middle_dis * (BaseTable[i].y - BaseTable[i - 1].y);
        }

        // The base table with monotone cubic interpolation (Fritsch-Carlson) instead of linear: through every table point,
        // with a continuous slope. Per-map curves only (PerMapValue); the base curve stays linear.
        public static double SmoothBaseValue(double acc)
        {
            var (xs, ys, slopes) = Smooth.Value;
            if (acc <= xs[0]) return ys[0];
            if (acc >= xs[^1]) return ys[^1];
            int i = 0;
            while (xs[i + 1] < acc) i++;
            double h = xs[i + 1] - xs[i];
            double t = (acc - xs[i]) / h;
            double t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * slopes[i]
                + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * slopes[i + 1];
        }

        // Table points ascending and the slope at each (weighted harmonic mean of the neighbouring segments' slopes)
        private static readonly Lazy<(double[] xs, double[] ys, double[] slopes)> Smooth = new(() =>
        {
            var points = BaseTable.OrderBy(p => p.x).ToArray();
            int n = points.Length;
            double[] xs = points.Select(p => p.x).ToArray(), ys = points.Select(p => p.y).ToArray();
            double[] h = new double[n - 1], d = new double[n - 1], slopes = new double[n];
            for (int i = 0; i < n - 1; i++)
            {
                h[i] = xs[i + 1] - xs[i];
                d[i] = (ys[i + 1] - ys[i]) / h[i];
            }
            slopes[0] = d[0];
            slopes[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0) continue;
                double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                slopes[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
            return (xs, ys, slopes);
        });

        // From the AccCurvePerMap branch. Default curve parameters from Desmos https://www.desmos.com/calculator/hlhqzmtzon
        // Only GetF2 (to join the base curve at A) and the extension above A (GetL) are used here.
        public class ModifiableCurve
        {
            public double Y0 { get; set; } = 0.256;
            public double K { get; set; } = 4.86;
            public double W { get; set; } = 0.6;
            public double K2 { get; set; } = 2.88;
            public double P2 { get; set; } = 2.46;
            // Taper of the extension above A: 0 = keeps the slope at A (straight line), 1 = the slope fades out (flattens)
            public double C { get; set; } = 1.0;
            // Where the extension starts
            public double A { get; set; } = 1.0;

            public double GetValue(double x)
            {
                if (x <= 0.60) return (Y0 / 0.60) * x;
                if (x <= 0.95) return GetF1(x);
                if (x <= A) return GetF2(x);
                return GetL(x);
            }

            // Slope at x = 0.95
            public double GetM()
            {
                double term1 = (1.0 - Y0) / 0.35;
                double term2 = (1.0 - W) + W * (K * Math.Exp(K)) / (Math.Exp(K) - 1.0);
                return term1 * term2;
            }

            // 0.60 < x <= 0.95
            public double GetF1(double x)
            {
                double u = (x - 0.6) / 0.35;
                double blend = (1.0 - W) * u + W * (Math.Exp(K * u) - 1.0) / (Math.Exp(K) - 1.0);
                return Y0 + (1.0 - Y0) * blend;
            }

            // 0.95 < x <= A
            public double GetF2(double x)
            {
                double m = GetM();
                double u2 = (x - 0.95) / 0.05;
                double expTerm = (Math.Exp(K2 * Math.Pow(u2, P2)) - 1.0) / (Math.Exp(K2) - 1.0);
                return 1.0 + m * (x - 0.95) + (6.424 - 0.05 * m) * expTerm;
            }

            // High-pass extension above A: starts with the slope S at A, which decays towards S * (1 - C)
            public double GetL(double x)
            {
                double s = GetS();
                double s1 = s * (1.0 - C);
                double f2A = GetF2(A);

                if (Math.Abs(A - 1.0) < 1e-9)
                {
                    return f2A + s1 * (x - A);
                }

                double expTerm = 1.0 - Math.Exp(-(x - A) / (1.0 - A));
                return f2A + s1 * (x - A) + (s - s1) * (1.0 - A) * expTerm;
            }

            // Slope at x = A
            public double GetS()
            {
                double m = GetM();
                double uA = (A - 0.95) / 0.05;
                double uAPower = uA > 0 ? Math.Pow(uA, P2) : 0;
                double uAPowerMinus1 = uA > 0 ? Math.Pow(uA, P2 - 1.0) : 0;
                double expTerm = Math.Exp(K2 * uAPower) / (Math.Exp(K2) - 1.0);
                double derivativeFactor = (K2 * P2 / 0.05) * uAPowerMinus1;
                return m + (6.424 - 0.05 * m) * expTerm * derivativeFactor;
            }
        }
    }

    // One row of CurveSettings.Table: ModifiableCurve parameters for a predicted acc
    public class CurveRow
    {
        public double PredictedAcc { get; set; }
        public double A { get; set; } = 1.0;
        public double C { get; set; } = 0;
        public double K { get; set; } = 4.86;
        public double W { get; set; } = 0.6;
        public double K2 { get; set; } = 2.88;
        public double P2 { get; set; } = 2.46;

        // A at 100% and the default shape: the base curve
        public bool IsDefault => A >= 0.9995 && K == 4.86 && W == 0.6 && K2 == 2.88 && P2 == 2.46;
    }

    // Settings of the per-map curves (Curve.GetCurve): ModifiableCurve parameters by predicted acc, linearly interpolated
    // between the rows; the first / last row applies below / above them.
    // Goals (Loloppe): 1. 9-12* maps (predicted 96-97.5%) gave too much for scores about +1% above the predicted acc
    // (the 800pp+ plays): K, W, K2, P2 reshape the curve around 96%, back to the default at 95.5% and from 97%.
    // 2. true acc (<2*) and acc maps (<5*) gave too much for tiny jumps around 99%: less steep there, hugging the base
    // curve more tightly while cutting the extreme tail, without nerfing the overall amount.
    // The 99.5% row isn't from the first values: without it the 99% row also applied above 99%, which cut the curve value
    // at the predicted acc of <2* maps by 19%; with it, by 6%.
    // Table values updated 2026-10-16 (A, C and the shape K, W, K2, P2 on every row; the 96-97.5% rows now ease back to the
    // default shape by 97.5-98.5% instead of 97%). The simulation below is for the previous table.
    // Simulated on all ranked scores (without the 99.5% row): scores above 800pp 246 -> 31, #1 vs #2 on the top maps +7.7%
    // -> +5.1%, the #1 player -7%, the top 100 -1.4%, map worth unchanged from 96.5% to 99% predicted acc.
    public class CurveSettings
    {
        public bool Enabled { get; set; } = true;

        public List<CurveRow> Table { get; set; } = new()
        {
            new CurveRow { PredictedAcc = 0.955, A = 1.0,     C = -5.30, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.960, A = 0.99,    C = -3.00, K = 4.30, W = 0.66, K2 = 4.80, P2 = 1.85 },
            new CurveRow { PredictedAcc = 0.965, A = 0.988,   C = -2.40, K = 4.50, W = 0.64, K2 = 4.50, P2 = 1.95 },
            new CurveRow { PredictedAcc = 0.970, A = 0.986,   C = -2.20, K = 4.70, W = 0.62, K2 = 4.20, P2 = 2.05 },
            new CurveRow { PredictedAcc = 0.975, A = 0.985,   C = -1.80, K = 4.86, W = 0.60, K2 = 3.80, P2 = 2.15 },
            new CurveRow { PredictedAcc = 0.980, A = 0.985,   C = -1.40, K = 4.86, W = 0.60, K2 = 3.35, P2 = 2.30 },
            new CurveRow { PredictedAcc = 0.985, A = 0.985,   C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.990, A = 0.9915,  C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
            new CurveRow { PredictedAcc = 0.995, A = 0.9915,  C = -1.20, K = 4.86, W = 0.60, K2 = 2.88, P2 = 2.46 },
        };

        // C never goes above 1 (the curve would go down)
        public double MaxC { get; set; } = 1;

        // Slider tilt: maps with many sliders (and curved sliders) were worth more than others of their stars (Megametric75
        // +0.17 for the 10% with the most, +0.48 for the top 3%; You Are Mine Normal, 49% sliders, +2.6), while their top
        // scores sit as close to the predicted acc as on other maps (~0.5% under). Their curve is multiplied by
        // e^(SliderTilt x slider share x (acc - predicted acc) in %), up to SliderTiltRange from the predicted acc: higher
        // above it, lower under it, unchanged at it (the acc rating doesn't use it; the stars do, as scores at 96%). 0 = off.
        public double SliderTilt { get; set; } = 0.2;
        public double SliderTiltRange { get; set; } = 0.015;

        // Low note nerf: maps with few notes (under LowNoteStart color notes, LackMapCalculation.NoteCount; 0 = unknown, no
        // nerf) are worth too much for how easily they're grinded to 100%. The whole curve is multiplied by LowNoteMultiplier
        // (x0.80 at 20 notes, never under LowNoteMinMultiplier, times the span factor: x0.80 for Quiet Water Easy, 20 notes
        // over 18 s, x0.74 with SF, x0.82 with SS; x1 for RTX 20000 Easy, 20 notes over 111 s, then its length bonus +5.5%),
        // so the curve stays smooth: LowNoteUniformShare 1, LowNoteGainCut 0. The gain cut below (0.4) bent the curve at the
        // predicted acc (its slope x0.6 above it, a kink in the acc curve): LowNoteGainCut of what the curve gained over its
        // value there was cut (all of it under LowNoteGainFullBelow notes, fading to none at LowNoteStart on ln(notes),
        // LowNoteShare, times the span factor), so scores under the predicted acc kept their pp (Dev, 24 Oct, 100% nomod / SS
        // / FS / SF: Quiet Water Easy 625 / 634 / 611 / 592, RTX 20000 Easy 680 / 694 / 664 / 646, the 40-60 note maps
        // 900-970 nomod, 770-860 SF). LowNoteShare still scales the length bonus (LowNoteBonusAt).
        // Speed modifiers don't raise these maps' ratings (LowNoteSpeedBlend): faster is a shorter grind, not a harder map.
        // Pass and tech pp aren't nerfed. LowNoteStart 0 = off.
        public double LowNoteStart { get; set; } = 115;
        public double LowNoteRefNotes { get; set; } = 20;
        public double LowNoteRefMultiplier { get; set; } = 0.8;
        public double LowNoteMinMultiplier { get; set; } = 0.5;
        public double LowNoteUniformShare { get; set; } = 1;
        public double LowNoteGainCut { get; set; } = 0;
        public double LowNoteGainFullBelow { get; set; } = 70;

        // Span factor: a long map with few notes is as easy to play as a short one but takes longer to grind (RTX 20000 Easy,
        // 20 notes over 111 s, against Quiet Water Easy's 20 over 18 s: its top 10 at 99.4-99.7% paid 456 pp, Quiet Water's at
        // 99.9% 509). 1 - LowNoteSpanWeight x ln(span / LowNoteSpanStart) / ln(LowNoteSpanRef / LowNoteSpanStart), between
        // LowNoteSpanMinFactor and LowNoteSpanMaxFactor: all of the nerf at LowNoteSpanStart, none at LowNoteSpanRef, more
        // under LowNoteSpanStart. The span is the first to the last color note in real time (song time / the speed modifier's,
        // LackMapCalculation.NoteSpan; 0 = unknown, factor 1), so FS and SF, shorter grinds, get more of the nerf and SS less.
        public double LowNoteSpanStart { get; set; } = 18;
        public double LowNoteSpanRef { get; set; } = 75;
        public double LowNoteSpanWeight { get; set; } = 1;
        public double LowNoteSpanMinFactor { get; set; } = 0;
        public double LowNoteSpanMaxFactor { get; set; } = 1.5;
        // Length bonus: past LowNoteSpanRef (the span factor under 0, unclamped), the whole curve x 1 + LowNoteSpanBonus per
        // unit of it, up to LowNoteSpanBonusMax, times LowNoteShare (RTX 20000 Easy +5.5%). 0 = off.
        public double LowNoteSpanBonus { get; set; } = 0.2;
        public double LowNoteSpanBonusMax { get; set; } = 0.1;

        // The gain cut of a map (notes: color notes; span: real time, seconds)
        public double LowNoteGainCutAt(int notes, double span)
        {
            if (LowNoteGainCut <= 0) return 0;
            return LowNoteGainCut * LowNoteShare(notes) * LowNoteSpanFactor(span);
        }

        // The length bonus of a map
        public double LowNoteBonusAt(int notes, double span)
        {
            if (LowNoteSpanBonus <= 0 || span <= 0 || LowNoteSpanRef <= LowNoteSpanStart) return 0;
            double raw = 1 - LowNoteSpanWeight * Math.Log(span / LowNoteSpanStart) / Math.Log(LowNoteSpanRef / LowNoteSpanStart);
            return raw >= 0 ? 0 : Math.Min(LowNoteSpanBonus * -raw, LowNoteSpanBonusMax) * LowNoteShare(notes);
        }

        // How much of the gain cut and the length bonus a map gets by its notes: all under LowNoteGainFullBelow, none from
        // LowNoteStart (on ln(notes))
        public double LowNoteShare(int notes)
        {
            if (LowNoteStart <= 0 || notes <= 0 || notes >= LowNoteStart) return 0;
            return notes <= LowNoteGainFullBelow ? 1 : Math.Log(LowNoteStart / notes) / Math.Log(LowNoteStart / LowNoteGainFullBelow);
        }

        // The curve's value at x with the low note nerf: value, the value without it; atPredicted, that at the predicted acc;
        // nerf, the uniform part (LowNoteUniformShare x (1 - LowNoteMultiplier)); cut, the gain cut (LowNoteGainCutAt); bonus,
        // the length bonus (LowNoteBonusAt)
        public double LowNoteValue(double value, double x, double atPredicted, double predictedAcc, double nerf, double cut, double bonus = 0)
        {
            if (cut != 0 && x > predictedAcc && predictedAcc > 0.6)
                value = atPredicted + (value - atPredicted) * Math.Max(1 - cut, 0);
            return value * Math.Max(1 - nerf, 0) * (1 + bonus);
        }

        // notes: color notes; span: the first to the last of them in real time, in seconds (0 = unknown)
        public double LowNoteMultiplier(int notes, double span = 0)
        {
            if (LowNoteStart <= 0 || notes <= 0 || notes >= LowNoteStart) return 1;
            double nerf = (1 - LowNoteRefMultiplier) * Math.Log(LowNoteStart / notes) / Math.Log(LowNoteStart / LowNoteRefNotes);
            return 1 - (1 - Math.Max(1 - nerf, LowNoteMinMultiplier)) * LowNoteSpanFactor(span);
        }

        // The span factor of a map (span: real time, seconds)
        public double LowNoteSpanFactor(double span)
        {
            if (LowNoteSpanWeight <= 0 || span <= 0 || LowNoteSpanRef <= LowNoteSpanStart) return 1;
            return Math.Clamp(1 - LowNoteSpanWeight * Math.Log(span / LowNoteSpanStart) / Math.Log(LowNoteSpanRef / LowNoteSpanStart),
                LowNoteSpanMinFactor, LowNoteSpanMaxFactor);
        }

        // Low note speed blend: under LowNoteStart color notes, a speed modifier's pass, tech and acc ratings keep (notes /
        // LowNoteStart)^2 of their difference to the nomod ones (RatingsController.BlendLowNoteSpeeds, RatingsRefresh.Refresh):
        // speed makes these maps faster to grind, not harder, so it shouldn't raise their ratings (Quiet Water Easy's SF acc
        // rating was 7.6% over its nomod one), and the span factor then gives the faster modifiers less. With the square,
        // 47 notes keep 17% (notes / LowNoteStart: 41%, and SF still paid more than nomod there). notes: color notes (0 =
        // unknown: unchanged).
        public double LowNoteSpeedBlend(double nomod, double modifier, int notes)
        {
            if (notes <= 0 || LowNoteStart <= 0 || notes >= LowNoteStart) return modifier;
            double kept = notes / LowNoteStart;
            return nomod + (modifier - nomod) * kept * kept;
        }

        public CurveRow ParamsAt(double predictedAcc)
        {
            var rows = Table.OrderBy(r => r.PredictedAcc).ToList();
            if (rows.Count == 0) return new CurveRow { PredictedAcc = predictedAcc };
            CurveRow Clamp(CurveRow r) => new CurveRow { PredictedAcc = predictedAcc, A = r.A, C = Math.Min(r.C, MaxC), K = r.K, W = r.W, K2 = r.K2, P2 = r.P2 };
            if (predictedAcc <= rows[0].PredictedAcc) return Clamp(rows[0]);
            for (int i = 1; i < rows.Count; i++)
            {
                if (predictedAcc <= rows[i].PredictedAcc)
                {
                    var p = rows[i - 1]; var n = rows[i];
                    double t = (predictedAcc - p.PredictedAcc) / (n.PredictedAcc - p.PredictedAcc);
                    double L(double x, double y) => x + t * (y - x);
                    return Clamp(new CurveRow { A = L(p.A, n.A), C = L(p.C, n.C), K = L(p.K, n.K), W = L(p.W, n.W), K2 = L(p.K2, n.K2), P2 = L(p.P2, n.P2) });
                }
            }
            return Clamp(rows[rows.Count - 1]);
        }

        // A and C only (Leaderboard page)
        public (double a, double c) At(double predictedAcc)
        {
            var row = ParamsAt(predictedAcc);
            return (row.A, row.C);
        }

        public static CurveSettings Current { get; set; } = new();
    }
}
