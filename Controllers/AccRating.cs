namespace RatingAPI.Controllers
{
    public class AccRating
    {
        // acc rating = ACC_SCALE / Curve(EffectiveAcc(predictedAcc) + ACC_OFFSET). portaBLe's balance tuner (Balance Tuning page) suggests
        // ACC_OFFSET; ACC_SCALE is its reference. portaBLe's ReplayUtils.AccRating uses this class, so change them here only.
        public const double ACC_SCALE = 15.1;
        public const double ACC_OFFSET = 0.0019;

        // Hard map correction. Below HARD_MAP_KNEE predicted acc, top players fall further behind the Acc AI's prediction
        // the harder the map gets (after removing each player's own level, top 100 players: -0.4% at 96.65%, -1.1% at 96%,
        // -1.5% at 95.4%, beyond a smooth trend that holds from 96.8% to 99%). Their pp stopped rising past about 12 stars.
        // The acc rating uses a predicted acc lowered by HARD_MAP_SLOPE times the distance below the knee, capped at
        // HARD_MAP_FLOOR (the edge of the fitted data). Map data only at rating time: the constants were fitted offline on
        // scores (rating-fit-scores + rating-fit-modifiers exports, Refresh/Tools/hard_map_offset_fit.py). The stored
        // predicted acc and the per-map curves are unchanged. Set HARD_MAP_SLOPE to 0 to turn it off.
        public const double HARD_MAP_KNEE = 0.9675;
        public const double HARD_MAP_SLOPE = 1.24;
        public const double HARD_MAP_FLOOR = 0.95;

        // The predicted acc the acc rating uses (see the hard map correction above)
        public static double EffectiveAcc(double predictedAcc) =>
            predictedAcc - HARD_MAP_SLOPE * Math.Max(0, HARD_MAP_KNEE - Math.Max(predictedAcc, HARD_MAP_FLOOR));

        public double GetRating(double? predictedAcc, double? passRating, double? techRating)
        {
            double difficulty_to_acc;
            if (predictedAcc > 0)
            {
                difficulty_to_acc = ACC_SCALE / Curve(predictedAcc ?? 0, EffectiveAcc(predictedAcc ?? 0) + ACC_OFFSET);
            }
            else
            {
                double tiny_tech = 0.0208f * (techRating ?? 0) + 1.1284f;
                difficulty_to_acc = (-Math.Pow(tiny_tech, -(passRating ?? 0)) + 1) * 8 + 2 + 0.01f * (techRating ?? 0) * (passRating ?? 0);
            }
            if (double.IsInfinity(difficulty_to_acc) || double.IsNaN(difficulty_to_acc) || double.IsNegativeInfinity(difficulty_to_acc))
            {
                difficulty_to_acc = 0;
            }
            return difficulty_to_acc;
        }

        // The map's own per-map acc curve (ModifiableCurve, see Curve.GetCurve), picked by its predicted acc, at acc
        public static float Curve(double predictedAcc, double acc) => (float)Controllers.Curve.PerMapValue(predictedAcc, acc);
    }
}
