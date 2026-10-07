using System.Numerics;

namespace RatingAPI.Controllers
{
    public class Curve
    {
        public List<Vector2> baseCurve = new()
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

        public List<Vector2> GetCurve(LackMapCalculation lackRatings)
        {
            List<Vector2> curve = new(baseCurve);

            // TODO: Implement logic to modify curve per map here

            return curve;
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

        private double GetSmoothBuffStrength(double x, double startTransition, double endTransition)
        {
            if (x <= startTransition)
            {
                return 0.0;
            }
            if (x >= endTransition)
            {
                return 1.0;
            }
            
            double t = (x - startTransition) / (endTransition - startTransition);
            return t * t * (3.0 - 2.0 * t);
        }
    }
}
