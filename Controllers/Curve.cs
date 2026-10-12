using System.Numerics;

namespace RatingAPI.Controllers
{
    public class Curve
    {
        public List<Vector2> oldCurve = new()
        {
            new Vector2(1.0f, 7.424f),
            new Vector2(0.999f, 6.241f),
            new Vector2(0.9975f, 5.158f),
            new Vector2(0.995f, 4.010f),
            new Vector2(0.9925f, 3.241f),
            new Vector2(0.99f, 2.700f),
            new Vector2(0.9875f, 2.303f),
            new Vector2(0.985f, 2.007f),
            new Vector2(0.9825f, 1.786f),
            new Vector2(0.98f, 1.618f),
            new Vector2(0.9775f, 1.490f),
            new Vector2(0.975f, 1.392f),
            new Vector2(0.9725f, 1.315f),
            new Vector2(0.97f, 1.256f),
            new Vector2(0.965f, 1.167f),
            new Vector2(0.96f, 1.094f),
            new Vector2(0.955f, 1.039f),
            new Vector2(0.95f, 1.000f),
            new Vector2(0.94f, 0.931f),
            new Vector2(0.93f, 0.867f),
            new Vector2(0.92f, 0.813f),
            new Vector2(0.91f, 0.768f),
            new Vector2(0.9f, 0.729f),
            new Vector2(0.875f, 0.650f),
            new Vector2(0.85f, 0.581f),
            new Vector2(0.825f, 0.522f),
            new Vector2(0.8f, 0.473f),
            new Vector2(0.75f, 0.404f),
            new Vector2(0.7f, 0.345f),
            new Vector2(0.65f, 0.296f),
            new Vector2(0.6f, 0.256f),
            new Vector2(0.0f, 0.000f)
        };

        public static readonly float[] BaseCurveX =
        {
            1.0f,
            0.999f,
            0.9975f,
            0.995f,
            0.9925f,
            0.99f,
            0.9875f,
            0.985f,
            0.9825f,
            0.98f,
            0.9775f,
            0.975f,
            0.9725f,
            0.97f,
            0.965f,
            0.96f,
            0.955f,
            0.95f,
            0.94f,
            0.93f,
            0.92f,
            0.91f,
            0.9f,
            0.875f,
            0.85f,
            0.825f,
            0.8f,
            0.75f,
            0.7f,
            0.65f,
            0.6f,
            0.0f
        };

        public List<Vector2> GetBaseCurve()
        {
            var curve = new ModifiableCurve();

            return BaseCurveX
                .Select(x => new Vector2(x, (float)curve.GetValue(x)))
                .ToList();
        }

        // The base curve, shared by every map. Star ratings always use it (see ToStars), so the per-map curve only changes
        // how pp is rewarded around a map's predicted accuracy, not how difficult the map is rated.
        public static readonly List<Vector2> BaseCurve = new Curve().GetBaseCurve();

        // Finer grid for per-map curves: 0.1% steps from 100% to 95%, 1% steps to 60%, then 0.
        public static readonly float[] DynamicCurveX = Enumerable.Range(0, 51).Select(i => 1.0f - i * 0.001f)
            .Concat(Enumerable.Range(1, 35).Select(i => 0.95f - i * 0.01f))
            .Append(0.0f)
            .ToArray();

        public List<Vector2> GetCurve(LackMapCalculation lackRatings, double predictedAcc, double accRating)
        {
            return GetCurve(lackRatings, predictedAcc, accRating, CurveSettings.Current);
        }

        // Per-map acc curve. With the default settings (Lambda 0, no cap) this is exactly the base curve.
        //
        // 1. Anchor: the value at the predicted acc is always the base curve's value there, so AccRating keeps its meaning
        //    (a player hitting the predicted acc gets the same acc pp on every map) and nothing has to be re-rated.
        // 2. Relative accuracy (below the anchor only): accuracy is measured as a share of the map's expected misses,
        //    (1 - acc) / (1 - predictedAcc), and read off the base curve at a reference accuracy. Lambda blends (geometrically)
        //    between the raw base curve (0) and the fully relative curve (1): being 0.5 below on a 99% map then costs like being 1.5 below
        //    on a 97% map instead of much more. Above the predicted acc the base curve is kept.
        // 3. Cap: above predictedAcc + CapOffset, only CapKeep of the extra value is kept, so beating the AI on an easy map
        //    stops paying out disproportionately.
        // 4. Boost (Fill, TargetPp): lifts the value at the predicted acc so the map's pp there closes part of the gap to
        //    TargetPp. Computed from the map's ratings; maps already worth TargetPp or more get nothing, and the order between
        //    maps is kept as long as Fill < 1.
        // 5. Above (AboveStrength, AboveExponent): above the predicted acc, the gain depends on the share of the predicted
        //    misses removed, the same on every map, instead of the base curve's steep top end.
        // Only map data is used (predicted acc, ratings, Analyzer statistics); never scores.
        public List<Vector2> GetCurve(LackMapCalculation lackRatings, double predictedAcc, double accRating, CurveSettings settings)
        {
            if (settings.IsBase || predictedAcc <= 0.6 || predictedAcc >= 0.9995)
            {
                return BaseCurve.ToList();
            }

            var curve = new ModifiableCurve();
            double p = predictedAcc;
            double reference = settings.ReferenceAcc;
            double anchor = curve.GetValue(p);
            double referenceValue = curve.GetValue(reference);

            double Shape(double x)
            {
                // Above the predicted acc the base curve is kept (the cap below limits it); relative accuracy would make
                // SS on easy maps worth several times the anchor, since 100% always maps to 100%.
                if (x >= p) return curve.GetValue(x);
                // Share of the anchor value kept at x: on the base curve, and with relative accuracy at the reference acc.
                double baseRatio = curve.GetValue(x) / anchor;
                double y = Math.Clamp(1.0 - (1.0 - reference) * (1.0 - x) / (1.0 - p), 0.0, 1.0);
                double relativeRatio = curve.GetValue(y) / referenceValue;
                // Geometric blend, so any Lambda between 0 and 1 lands between the two curves
                return anchor * Math.Pow(Math.Max(baseRatio, 1e-9), 1.0 - settings.Lambda) * Math.Pow(Math.Max(relativeRatio, 1e-9), settings.Lambda);
            }

            // Boost: extra curve value at the predicted acc that closes Fill of the gap between the map's pp at its predicted
            // acc (from its ratings, no scores) and TargetPp. Maps already at or above the target get nothing.
            // Below the predicted acc the curve is scaled by the same factor; above it the boost is added as a constant,
            // so the part of the curve above the predicted acc (beating the AI) is not amplified. With AboveStrength, the gain
            // above is a % of the map's own (unboosted) value, the same % on every map for the same share of misses removed.
            double boost = settings.Fill > 0 ? AnchorBoost(lackRatings, p, accRating, anchor, settings) : 0;
            double scale = (anchor + boost) / anchor;
            double Value(double x)
            {
                if (x < p) return Shape(x) * scale;
                if (settings.AboveStrength <= 0) return Shape(x) + boost;
                // Relative gain above the prediction: removing a share u of the predicted misses is worth the same % on every
                // map, 1 + AboveStrength * u^AboveExponent (SS = u of 1). Beating the top players is rewarded equally
                // everywhere instead of exploding on easy maps or being cut off by a cap.
                double u = Math.Clamp((x - p) / (1.0 - p), 0.0, 1.0);
                // Taper: on very easy maps the few predicted misses are mostly luck, so removing them is worth less.
                // Full strength at AboveTaperAcc and below, proportional to the predicted misses above it.
                double strength = settings.AboveStrength;
                if (settings.AboveTaperAcc > 0 && p > settings.AboveTaperAcc)
                {
                    strength *= Math.Max(settings.AboveTaperMin, (1.0 - p) / (1.0 - settings.AboveTaperAcc));
                }
                // The boost is added, not multiplied: the % gain applies to the map's own value only
                return anchor * (1.0 + strength * Math.Pow(u, settings.AboveExponent)) + boost;
            }

            double? capX = settings.CapShare > 0 ? (double?)Math.Min(1.0, p + settings.CapShare * (1.0 - p))
                         : settings.CapOffset > 0 ? (double?)Math.Min(1.0, p + settings.CapOffset)
                         : null;
            double capValue = capX.HasValue ? Value(capX.Value) : 0;

            return DynamicCurveX
                .Select(x =>
                {
                    double v = Value(x);
                    if (capX.HasValue && x > capX.Value)
                    {
                        v = capValue + settings.CapKeep * (v - capValue);
                    }
                    return new Vector2(x, (float)v);
                })
                .ToList();
        }

        // pp at the predicted acc with the base curve, the same formula as ToStars / ReplayUtils.GetPp (no modifiers).
        public static double PpAt(double acc, double curveValue, double accRating, double passRating, double techRating)
        {
            double passPP = 15.2 * Math.Exp(Math.Pow(Math.Max(passRating, 0), 1 / 2.62)) - 30;
            if (double.IsNaN(passPP) || double.IsInfinity(passPP) || passPP < 0) passPP = 0;
            double accPP = curveValue * accRating * 34;
            double techPP = Math.Exp(1.9 * acc) * 1.08 * techRating;
            return Inflate(passPP + accPP + techPP);
        }

        private static double Inflate(double pp) => pp <= 0 ? 0 : 650 * Math.Pow(pp, 1.3) / Math.Pow(650, 1.3);
        private static double Deflate(double pp) => pp <= 0 ? 0 : Math.Pow(pp * Math.Pow(650, 1.3) / 650, 1 / 1.3);

        private static double AnchorBoost(LackMapCalculation ratings, double p, double accRating, double anchor, CurveSettings settings)
        {
            if (accRating <= 0) return 0;
            double basePp = PpAt(p, anchor, accRating, ratings.PassRating, ratings.TechRating);
            if (basePp >= settings.TargetPp) return 0;
            double targetPp = basePp + settings.Fill * (settings.TargetPp - basePp);
            // Raw (before inflation) pp needed, minus the parts the curve doesn't touch, gives the acc pp needed
            double rawNeeded = Deflate(targetPp);
            double rawNow = Deflate(basePp);
            double extraAccPp = rawNeeded - rawNow;
            return Math.Max(0, extraAccPp / (accRating * 34));
        }

        // Stars are computed on the base curve whatever curve the map gets (the curve parameter is kept for callers).
        public double ToStars(double acc, double accRating, LackMapCalculation ratings, List<Vector2> curve)
        {
            curve = BaseCurve;
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

        public double Curve2(double acc, List<Vector2> curve)
        {
            int i = 0;
            for (; i < curve.Count; i++)
            {
                if (curve[i].X <= acc)
                {
                    break;
                }
            }

            if (i == 0)
            {
                i = 1;
            }

            double middle_dis = (acc - curve[i - 1].X) / (curve[i].X - curve[i - 1].X);
            return (float)(curve[i - 1].Y + middle_dis * (curve[i].Y - curve[i - 1].Y));
        }

        public class ModifiableCurve
        {
            // Default curve parameters from Desmos https://www.desmos.com/calculator/hlhqzmtzon
            public double Y0 { get; set; } = 0.256;
            public double K { get; set; } = 4.86;
            public double W { get; set; } = 0.6;
            public double K2 { get; set; } = 2.88;
            public double P2 { get; set; } = 2.46;
            public double C { get; set; } = 1.0;
            public double A { get; set; } = 1.0;

            /// <summary>
            /// Computes the overall composite curve value g(x) for a given x input.
            /// </summary>
            public double GetValue(double x)
            {
                if (x <= 0.60)
                {
                    return (Y0 / 0.60) * x;
                }
                ;
                if (x <= 0.95)
                {
                    return GetF1(x);
                }
                if (x <= A)
                {
                    return GetF2(x);
                }

                return GetL(x);
            }

            /// <summary>
            /// Calculates the slope derivative M at x = 0.95.
            /// </summary>
            public double GetM()
            {
                double term1 = (1.0 - Y0) / 0.35;
                double term2 = (1.0 - W) + W * (K * Math.Exp(K)) / (Math.Exp(K) - 1.0);
                return term1 * term2;
            }

            /// <summary>
            /// Computes f1(x) for 0.60 < x <= 0.95.
            /// </summary>
            public double GetF1(double x)
            {
                double u = (x - 0.6) / 0.35;
                double blend = (1.0 - W) * u + W * (Math.Exp(K * u) - 1.0) / (Math.Exp(K) - 1.0);
                return Y0 + (1.0 - Y0) * blend;
            }

            /// <summary>
            /// Computes f2(x) for 0.95 < x <= a.
            /// </summary>
            public double GetF2(double x)
            {
                double m = GetM();
                double u2 = (x - 0.95) / 0.05;
                double expTerm = (Math.Exp(K2 * Math.Pow(u2, P2)) - 1.0) / (Math.Exp(K2) - 1.0);

                return 1.0 + m * (x - 0.95) + (6.424 - 0.05 * m) * expTerm;
            }

            /// <summary>
            /// Computes the High-Pass extension L(x) for x > a.
            /// </summary>
            /// <summary>
            /// Computes the High-Pass extension L(x) for x > a.
            /// </summary>
            public double GetL(double x)
            {
                double s = GetS();
                double s1 = s * (1.0 - C);
                double f2A = GetF2(A);

                if (Math.Abs(A - 1.0) < 1e-9)
                {
                    // Prevents division by zero when A = 1
                    return f2A + s1 * (x - A);
                }

                double expTerm = 1.0 - Math.Exp(-(x - A) / (1.0 - A));
                return f2A + s1 * (x - A) + (s - s1) * (1.0 - A) * expTerm;
            }

            /// <summary>
            /// Calculates the slope derivative S at x = a.
            /// </summary>
            public double GetS()
            {
                double m = GetM();
                double uA = (A - 0.95) / 0.05;

                // Handle uA = 0 or negative edge cases gracefully if a < 0.95
                double uAPower = uA > 0 ? Math.Pow(uA, P2) : 0;
                double uAPowerMinus1 = uA > 0 ? Math.Pow(uA, P2 - 1.0) : 0;

                double expTerm = Math.Exp(K2 * uAPower) / (Math.Exp(K2) - 1.0);
                double derivativeFactor = (K2 * P2 / 0.05) * uAPowerMinus1;

                return m + (6.424 - 0.05 * m) * expTerm * derivativeFactor;
            }
        }
    }

    // Shape of the per-map acc curves (see Curve.GetCurve). A new CurveSettings() gives the base curve on every map.
    // CurveSettings.Current is what RatingAPI and CurvesRefresh use; the curve experiments in portaBLe try other values.
    public class CurveSettings
    {
        public string Name { get; set; } = "Current";
        // 0 = base curve, 1 = fully relative to the map's predicted acc
        public double Lambda { get; set; } = 0;
        // Accuracy whose part of the base curve is used for every map's shape when Lambda = 1
        public double ReferenceAcc { get; set; } = 0.97;
        // Accuracy above the predicted acc where the cap starts (0 = no cap), e.g. 0.004 = 0.4 points above
        public double CapOffset { get; set; } = 0;
        // Cap placed at a share of the map's expected misses instead: predicted + CapShare * (1 - predicted).
        // 0.3 = +0.15 on a 99.5% map, +1.2 on a 96% map. Used instead of CapOffset when above 0.
        public double CapShare { get; set; } = 0;
        // Share of the extra curve value kept above the cap (1 = no cap)
        public double CapKeep { get; set; } = 1;
        // Boost: share of the gap between the map's pp at its predicted acc and TargetPp that is closed (0 = no boost)
        public double Fill { get; set; } = 0;
        public double TargetPp { get; set; } = 700;
        // Above the predicted acc: 0 = base curve; otherwise value * (1 + AboveStrength * u^AboveExponent), where u is the
        // share of the predicted misses removed (0.3 = 30% fewer misses than predicted, 1 = SS)
        public double AboveStrength { get; set; } = 0;
        public double AboveExponent { get; set; } = 1;
        // Taper of AboveStrength on very easy maps: full strength when the predicted acc is AboveTaperAcc or lower, then
        // proportional to the predicted misses (0.985: a 99.7% map gets 20%), never below AboveTaperMin. 0 = no taper.
        public double AboveTaperAcc { get; set; } = 0;
        public double AboveTaperMin { get; set; } = 0.1;

        public bool IsBase => Lambda == 0 && Fill <= 0 && AboveStrength <= 0 && ((CapOffset <= 0 && CapShare <= 0) || CapKeep >= 1);

        // The settings RatingAPI and CurvesRefresh use. Chosen from the curve experiments (round 5): boost fills half the gap to
        // 700pp at the predicted acc, and beating the prediction is worth +90% at SS, tapered on maps predicted above 98.5%.
        // Set to new() to go back to the base curve on every map.
        public static CurveSettings Current { get; set; } = new()
        {
            Name = "Fill 0.5 + tapered above 0.9",
            Fill = 0.5,
            TargetPp = 700,
            AboveStrength = 0.9,
            AboveExponent = 1,
            AboveTaperAcc = 0.985,
            AboveTaperMin = 0.1,
        };

        public override string ToString()
        {
            if (IsBase) return Name + " (base curve)";
            string cap = CapShare > 0 ? FormattableString.Invariant($"{CapShare * 100:0.#}% of misses keep {CapKeep:0.##}")
                       : CapOffset > 0 ? FormattableString.Invariant($"+{CapOffset * 100:0.##} keep {CapKeep:0.##}")
                       : "none";
            string fill = Fill > 0 ? FormattableString.Invariant($"fill {Fill:0.##} to {TargetPp:0}pp") : "no boost";
            string taper = AboveTaperAcc > 0 ? FormattableString.Invariant($", taper from {AboveTaperAcc * 100:0.#}%") : "";
            string above = AboveStrength > 0 ? FormattableString.Invariant($"above +{AboveStrength * 100:0}% at SS, exponent {AboveExponent:0.##}") + taper : "base above";
            return FormattableString.Invariant($"{Name} (lambda {Lambda}, reference {ReferenceAcc * 100:0.#}%, cap {cap}, {fill}, {above})");
        }
    }
}
