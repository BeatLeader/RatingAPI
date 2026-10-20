namespace RatingAPI.Controllers
{
    public class AccRating
    {
        // acc rating = ACC_SCALE x Curve(EffectiveAcc(predictedAcc, pass, hit distance) + ACC_OFFSET) ^ -ACC_EXPONENT.
        // portaBLe's balance tuner (Balance Tuning page) suggests ACC_OFFSET; ACC_SCALE is its reference. portaBLe's
        // ReplayUtils.AccRating uses this class, so change them here only.
        // ACC_EXPONENT: 1 / Curve's top end is steeper than prod's acc ratings; fitted on the quantiles of prod's ranked acc
        // ratings (median 8.54 vs 8.68, p95 11.93 vs 11.89, p99 13.00 vs 12.63). portaBLe's acc pp takes the acc rating to the
        // inverse power (PpFormula.AccRatingPower), so acc pp is what it was with the power 1. (Was 15.1 / Curve with a display
        // curve 1.4528 x rating^0.8222 on top: 13.5382 = 1.4528 x 15.1^0.8222.)
        public const double ACC_SCALE = 13.5382;
        public const double ACC_EXPONENT = 0.8222;
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

        // Movement and high pass corrections. The Acc AI predicts the accuracy of the notes players cut (its targets leave
        // misses and bad cuts out), and where the hands travel far, or the pass is very high, players lose more to misses and
        // bad cuts: top players fall further behind the prediction there than the hard map correction expects. Fitted on the
        // top 300 players' scores after removing each player's own level: -3% acc per meter of mean hit distance (Analyzer
        // Ratings.HitDistance) above 0.62 m (the median map; t -8 on the 434 maps under 97% predicted acc, t -18 on all),
        // and -0.214% per pass point above 14.52 (t -5; fitted as -0.32% per point above 11.5 on the pass display of the
        // time). The Purple Dimension (0.75 m, players -2.5% against the prediction) was too low for what players
        // can get on it.
        // The hit distance one is phased out between 97% and 97.5% predicted acc: the acc rating is much more sensitive to
        // the effective acc on easier maps (steeper curve), and applied to every map it made easy maps with a lot of movement
        // worth far too much (worth spread 0.49 -> 0.56). Hit distance is capped at 0.8 m: the shortfall stops growing there
        // (-1.0% at 0.75-0.85 m, -0.9% above), and without the cap maps with extreme spacing (You Are Mine Normal 1.04 m,
        // toromi hearts 2 0.94 m) got too much.
        // Pass alone fitted on the raw top 10 looked stronger, but that's who plays those maps: at 0.54% Godspeed became
        // overvalued. Top players also lose more acc on tech maps, but tech pp already pays for that: adding tech here made
        // tech maps worth far too much.
        public const double HIT_DISTANCE_KNEE = 0.62;
        public const double HIT_DISTANCE_SLOPE = 0.030;
        public const double HIT_DISTANCE_CAP = 0.80;
        public const double HIT_DISTANCE_FULL_BELOW = 0.97;
        public const double HIT_DISTANCE_NONE_ABOVE = 0.975;
        public const double HIGH_PASS_KNEE = 14.52;
        public const double HIGH_PASS_SLOPE = 0.00214;

        // The predicted acc the acc rating uses (see the corrections above). passRating: the pass rating; hitDistance:
        // Ratings.HitDistance. 0 leaves that correction out.
        public static double EffectiveAcc(double predictedAcc, double passRating = 0, double hitDistance = 0) =>
            predictedAcc - HARD_MAP_SLOPE * Math.Max(0, HARD_MAP_KNEE - Math.Max(predictedAcc, HARD_MAP_FLOOR))
            - HIT_DISTANCE_SLOPE * Math.Max(0, Math.Min(hitDistance, HIT_DISTANCE_CAP) - HIT_DISTANCE_KNEE)
                * Math.Clamp((HIT_DISTANCE_NONE_ABOVE - predictedAcc) / (HIT_DISTANCE_NONE_ABOVE - HIT_DISTANCE_FULL_BELOW), 0, 1)
            - HIGH_PASS_SLOPE * Math.Max(0, passRating - HIGH_PASS_KNEE);

        public double GetRating(double? predictedAcc, double? passRating, double? techRating, double hitDistance = 0)
        {
            double difficulty_to_acc;
            if (predictedAcc > 0)
            {
                double effective = EffectiveAcc(predictedAcc ?? 0, passRating ?? 0, hitDistance);
                difficulty_to_acc = ACC_SCALE * Math.Pow(Curve(predictedAcc ?? 0, effective + ACC_OFFSET), -ACC_EXPONENT);
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
