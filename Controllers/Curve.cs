namespace RatingAPI.Controllers
{
    public class Curve
    {
        // The base acc curve (same values as portaBLe's ReplayUtils pointList2): acc pp = curve(acc) * acc rating * 34
        public List<(double x, double y)> baseCurve = BaseTable.ToList();

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
                (0.91, 0.768),
                (0.9, 0.729),
                (0.875, 0.650),
                (0.85, 0.581),
                (0.825, 0.522),
                (0.8, 0.473),
                (0.75, 0.404),
                (0.7, 0.345),
                (0.65, 0.296),
                (0.6, 0.256),
                (0.0, 0.000)
        };

        // Base curve for every map (stars, and maps without a usable predicted acc)
        public List<Point> GetCurve(LackMapCalculation lackRatings)
        {
            return BaseTable.Select(p => new Point(p.x, p.y)).ToList();
        }

        // Per-map acc curve: ModifiableCurve (from the AccCurvePerMap branch) with all its parameters (A, C, K, W, K2, P2)
        // read from CurveSettings.Table by the map's predicted acc:
        // - K, W shape the middle part (60-95%), K2, P2 the upper part (95% to A);
        // - above A, the high-pass extension L(x) leaves A with the curve's slope there, which then keeps growing (C < 0,
        //   still exponential) or fades (0 < C <= 1; 0 = straight line).
        // The parameters only act relative to the default ModifiableCurve: value = base table(x) * modified(x) / default(x),
        // so default parameters give exactly today's curve (the default ModifiableCurve is 3-6% above the table at 97-99.9%,
        // using it directly would raise most acc pp). Never above the base curve. Only map data is used, never scores.
        public List<Point> GetCurve(LackMapCalculation lackRatings, double predictedAcc)
        {
            var settings = CurveSettings.Current;
            if (!settings.Enabled || predictedAcc <= 0.6 || predictedAcc >= 0.9995)
            {
                return GetCurve(lackRatings);
            }
            var row = settings.ParamsAt(predictedAcc);
            if (row.IsDefault)
            {
                return GetCurve(lackRatings);
            }
            double a = Math.Max(row.A, 0.951);
            var modified = new ModifiableCurve { A = Math.Min(a, 1.0), C = row.C, K = row.K, W = row.W, K2 = row.K2, P2 = row.P2 };
            var standard = new ModifiableCurve();

            // Base table points, 0.5% steps from 60% to 95%, 0.05% steps from 95% to 100%, and A
            var xs = BaseTable.Select(p => p.x)
                .Concat(Enumerable.Range(0, 71).Select(i => 0.6 + i * 0.005))
                .Concat(Enumerable.Range(0, 101).Select(i => 0.95 + i * 0.0005))
                .Append(Math.Min(a, 1.0))
                .Select(x => Math.Round(x, 6))
                .Where(x => x >= 0 && x <= 1)
                .Distinct()
                .OrderByDescending(x => x);

            return xs.Select(x =>
            {
                double baseValue = BaseValue(x);
                double reference = standard.GetValue(x);
                double value = reference > 0 ? baseValue * modified.GetValue(x) / reference : baseValue;
                return new Point(x, Math.Min(baseValue, value));
            }).ToList();
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

        // Stars always use the base curve, whatever curve the map gets: the per-map curve changes how scores above the
        // predicted acc are rewarded, not how difficult the map is rated. (The curve parameter is kept for callers.)
        public double ToStars(double acc, double accRating, LackMapCalculation ratings, List<Point> curve)
        {
            curve = GetCurve(ratings);
            double passPP = 15.2f * MathF.Exp(MathF.Pow((float)ratings.PassRating, 1 / 2.62f)) - 30f;
            if (double.IsInfinity(passPP) || double.IsNaN(passPP) || double.IsNegativeInfinity(passPP) || passPP < 0)
            {
                passPP = 0;
            }
            double accPP = Curve2(acc, curve) * accRating * 34f;
            double techPP = MathF.Exp((float)(1.9 * acc)) * 1.08f * ratings.TechRating;

            double pp = 650f * MathF.Pow((float)(passPP + accPP + techPP), 1.3f) / MathF.Pow(650f, 1.3f);

            return pp / 52;
        }

        public double Curve2(double acc, List<Point> curve)
        {
            int i = 0;
            for (; i < curve.Count; i++)
            {
                if (curve[i].x <= acc)
                {
                    break;
                }
            }

            if (i == 0)
            {
                i = 1;
            }

            double middle_dis = (acc - curve[i - 1].x) / (curve[i].x - curve[i - 1].x);
            return (float)(curve[i - 1].y + middle_dis * (curve[i].y - curve[i - 1].y));
        }

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
    // Simulated on all ranked scores (without the 99.5% row): scores above 800pp 246 -> 31, #1 vs #2 on the top maps +7.7%
    // -> +5.1%, the #1 player -7%, the top 100 -1.4%, map worth unchanged from 96.5% to 99% predicted acc.
    public class CurveSettings
    {
        public bool Enabled { get; set; } = true;

        public List<CurveRow> Table { get; set; } = new()
        {
            new CurveRow { PredictedAcc = 0.955, A = 1.0,    C = -5.3 },
            new CurveRow { PredictedAcc = 0.960, A = 0.98,   C = -5.3, K = 4.1, W = 0.7, K2 = 6, P2 = 1.4 },
            new CurveRow { PredictedAcc = 0.965, A = 0.976,  C = -5.3, K2 = 4, P2 = 2.37 },
            new CurveRow { PredictedAcc = 0.970, A = 0.968,  C = -2 },
            new CurveRow { PredictedAcc = 0.975, A = 0.97,   C = -1.7 },
            new CurveRow { PredictedAcc = 0.980, A = 0.977,  C = -1.4 },
            new CurveRow { PredictedAcc = 0.985, A = 0.98,   C = -1.4 },
            new CurveRow { PredictedAcc = 0.990, A = 0.985,  C = -0.6 },
            new CurveRow { PredictedAcc = 0.995, A = 0.9915, C = -0.6 },
        };

        // C never goes above 1 (the curve would go down)
        public double MaxC { get; set; } = 1;

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
