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

        public List<Vector2> GetCurve(LackMapCalculation lackRatings, double predictedAcc, double accRating)
        {
            List<Vector2> baseCurve = GetBaseCurve();

            return baseCurve;
        }

        public double ToStars(double acc, double accRating, LackMapCalculation ratings, List<Vector2> curve)
        {
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
}
