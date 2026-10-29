using RatingAPI.Controllers;
using System.Numerics;

namespace RatingAPI.Pp
{
    // A map's ratings for no modifier or one speed modifier, and its acc curve for them (Curve.GetCurve points, from 100%
    // down; null or fewer than 2 points: the base curve)
    public readonly record struct MapRatings(float AccRating, float PassRating, float TechRating, IReadOnlyList<Vector2>? Curve = null);

    // A map's ratings with what all of its scores' pp share: their stars and their curve's value at StarAccuracy
    // (PpCalculator.Prepare), so scoring many scores of one map computes them once
    public readonly record struct PreparedRatings(MapRatings Ratings, float Stars, float StarCurveValue);

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

            // The first point at or below acc (the points go down from 100%): binary search
            int i = 0, hi = curve.Count;
            while (i < hi)
            {
                int mid = (i + hi) >> 1;
                if (curve[mid].X <= acc) hi = mid;
                else i = mid + 1;
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
            double power = stars is float s ? formula.CurvePowerAt(s) : formula.AccCurvePower;
            return GetPp(accuracy, accRating, passRating, techRating, curve, power, power != 1 ? CurveValue(StarAccuracy, curve) : 0);
        }

        // GetPp with the acc curve power and the curve's value at StarAccuracy given
        private static (float pass, float acc, float tech) GetPp(float accuracy, float accRating, float passRating, float techRating, IReadOnlyList<Vector2>? curve, double power, float starCurveValue)
        {
            var formula = PpFormula.Current;
            float curveValue = CurveValue(accuracy, curve);
            if (power != 1) curveValue = formula.AccCurve(curveValue, starCurveValue, power);
            return (formula.PassPp(passRating), formula.AccPp(curveValue, accRating), formula.TechPp(accuracy, techRating));
        }

        // A map's ratings prepared for ScorePp (their stars and their curve's value at StarAccuracy)
        public static PreparedRatings Prepare(MapRatings ratings) =>
            new(ratings, ToStars(ratings), CurveValue(StarAccuracy, ratings.Curve));

        // The ranked modifier multipliers (ModifiersMap.RankedMap, read only)
        private static readonly ModifiersMap RankedModifiers = ModifiersMap.RankedMap();

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
            bool speedModifiersRated = false) =>
            ScorePp(accuracy, modifiers, Prepare(ratings), speedRatings is MapRatings speed ? Prepare(speed) : null, speedModifiersRated);

        // ScorePp with the ratings prepared (Prepare): the same pp, for scoring many scores of one map
        public static (float, float, float, float, float) ScorePp(
            float accuracy,
            string modifiers,
            PreparedRatings ratings,
            PreparedRatings? speedRatings,
            bool speedModifiersRated)
        {
            if (accuracy <= 0 || accuracy > 1) return (0, 0, 0, 0, 0);

            // No modifiers: 1 (none of them matches)
            float mp = modifiers.Length == 0 ? 1 : RankedModifiers.GetTotalMultiplier(modifiers, !speedModifiersRated);

            float rawPP = 0; float fullPP = 0; float passPP = 0; float accPP = 0; float techPP = 0; float increase = 0;
            if (!modifiers.Contains("NF"))
            {
                var formula = PpFormula.Current;
                // The low star bonus (PpFormula.LowStarMultiplier), the mid star top bonus (MidStarTopMultiplier) and the acc
                // curve power use the stars of the ratings each pp uses
                var map = ratings.Ratings;
                float stars = ratings.Stars;
                (passPP, accPP, techPP) = GetPp(accuracy, map.AccRating, map.PassRating, map.TechRating, map.Curve, formula.CurvePowerAt(stars), ratings.StarCurveValue);
                rawPP = Scale(passPP + accPP + techPP) * formula.LowStarMultiplier(stars) * formula.MidStarTopMultiplier(stars, accuracy);

                var prepared = speedRatings ?? ratings;
                var score = prepared.Ratings;
                float scoreStars = prepared.Stars;
                (passPP, accPP, techPP) = GetPp(accuracy, score.AccRating * mp, score.PassRating * mp, score.TechRating * mp, score.Curve, formula.CurvePowerAt(scoreStars), prepared.StarCurveValue);
                fullPP = Scale(passPP + accPP + techPP) * formula.LowStarMultiplier(scoreStars) * formula.MidStarTopMultiplier(scoreStars, accuracy);
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
