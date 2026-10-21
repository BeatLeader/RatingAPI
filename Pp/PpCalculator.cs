using RatingAPI.Controllers;
using System.Numerics;

namespace RatingAPI.Pp
{
    // A map's ratings for no modifier or one speed modifier, and its acc curve for them (Curve.GetCurve points, from 100%
    // down; null or fewer than 2 points: the base curve)
    public readonly record struct MapRatings(float AccRating, float PassRating, float TechRating, IReadOnlyList<Vector2>? Curve = null);

    // pp of scores and stars of maps, with PpFormula.Current. Everything the pp and the stars need is here (portaBLe's
    // ReplayUtils only calls it).
    public static class PpCalculator
    {
        // Stars are what a score at this accuracy is worth
        public const float StarAccuracy = 0.96f;

        // Value of a map's acc curve at acc (points from 100% down, see Curve.GetCurve). Null or fewer than 2 points: the
        // base curve.
        public static float CurveValue(float acc, IReadOnlyList<Vector2>? curve)
        {
            if (curve == null || curve.Count < 2)
            {
                return CurveValue(acc);
            }

            int i = 0;
            for (; i < curve.Count; i++)
            {
                if (curve[i].X <= acc) {
                    break;
                }
            }

            if (i == 0) {
                i = 1;
            }
            if (i >= curve.Count) {
                i = curve.Count - 1;
            }

            double middle_dis = (acc - curve[i - 1].X) / (double)(curve[i].X - curve[i - 1].X);
            return (float)(curve[i - 1].Y + middle_dis * (curve[i].Y - curve[i - 1].Y));
        }

        // Value of the base curve (Curve.BaseTable) at acc
        public static float CurveValue(float acc) => (float)Curve.BaseValue(acc);

        // Curve.GetCurve's points as CurveValue reads them
        public static List<Vector2> ToCurve(IEnumerable<Point>? points) =>
            points?.Select(p => new Vector2((float)p.x, (float)p.y)).ToList() ?? new List<Vector2>();

        // Pass, acc and tech pp at accuracy, before PpMultiplier. curve: the map's acc curve (null: the base curve). stars: the
        // map's, for the acc curve power (PpFormula.CurvePowerAt; null: AccCurvePower).
        public static (float pass, float acc, float tech) GetPp(float accuracy, float accRating, float passRating, float techRating, IReadOnlyList<Vector2>? curve = null, float? stars = null)
        {
            var formula = PpFormula.Current;
            float curveValue = CurveValue(accuracy, curve);
            double power = stars is float s ? formula.CurvePowerAt(s) : formula.AccCurvePower;
            if (power != 1) curveValue = formula.AccCurve(curveValue, CurveValue(StarAccuracy, curve), power);
            return (formula.PassPp(passRating), formula.AccPp(curveValue, accRating), formula.TechPp(accuracy, techRating));
        }

        // pp from the sum of pass, acc and tech pp (PpMultiplier), before the low star bonus
        public static float Scale(float pp) => PpFormula.Current.Scale(pp);

        // Stars: what a StarAccuracy score without modifiers is worth (ScorePp's pp, with the map's curve and the low star
        // bonus), on the star scale (PpFormula.StarsFromPp). With a speed modifier's ratings and curve: that modifier's stars.
        public static float ToStars(float accRating, float passRating, float techRating, IReadOnlyList<Vector2>? curve = null)
        {
            var formula = PpFormula.Current;
            (float passPP, float accPP, float techPP) = GetPp(StarAccuracy, accRating, passRating, techRating, curve);
            return formula.StarsFromPp(passPP + accPP + techPP);
        }

        public static float ToStars(MapRatings ratings) => ToStars(ratings.AccRating, ratings.PassRating, ratings.TechRating, ratings.Curve);

        // pp of a score: (pp, pp gained from the modifiers, pass pp, acc pp, tech pp).
        // ratings: the map's without modifiers. speedRatings: the map's for the score's speed modifier (SS / FS / SF), null
        // when it has none. speedModifiersRated: the map has speed modifier ratings (false: the speed modifiers count in the
        // modifier multiplier instead).
        public static (float, float, float, float, float) ScorePp(
            float accuracy,
            string modifiers,
            MapRatings ratings,
            MapRatings? speedRatings = null,
            bool speedModifiersRated = false)
        {
            if (accuracy <= 0 || accuracy > 1) return (0, 0, 0, 0, 0);

            float mp = ModifiersMap.RankedMap().GetTotalMultiplier(modifiers, !speedModifiersRated);

            float rawPP = 0; float fullPP = 0; float passPP = 0; float accPP = 0; float techPP = 0; float increase = 0;
            if (!modifiers.Contains("NF"))
            {
                // The low star bonus (PpFormula.LowStarMultiplier) and the acc curve power use the stars of the ratings each pp uses
                float stars = ToStars(ratings);
                (passPP, accPP, techPP) = GetPp(accuracy, ratings.AccRating, ratings.PassRating, ratings.TechRating, ratings.Curve, stars);
                rawPP = Scale(passPP + accPP + techPP) * PpFormula.Current.LowStarMultiplier(stars);

                var score = speedRatings ?? ratings;
                float scoreStars = speedRatings == null ? stars : ToStars(score);
                (passPP, accPP, techPP) = GetPp(accuracy, score.AccRating * mp, score.PassRating * mp, score.TechRating * mp, score.Curve, scoreStars);
                fullPP = Scale(passPP + accPP + techPP) * PpFormula.Current.LowStarMultiplier(scoreStars);
                if (passPP + accPP + techPP > 0) {
                    increase = fullPP / (passPP + accPP + techPP);
                }
            }

            if (float.IsInfinity(rawPP) || float.IsNaN(rawPP) || float.IsNegativeInfinity(rawPP)) {
                rawPP = 0;
            }

            if (float.IsInfinity(fullPP) || float.IsNaN(fullPP) || float.IsNegativeInfinity(fullPP)) {
                fullPP = 0;
            }

            return (fullPP, fullPP - rawPP, passPP * increase, accPP * increase, techPP * increase);
        }
    }
}
