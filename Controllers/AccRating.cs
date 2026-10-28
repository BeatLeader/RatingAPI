namespace RatingAPI.Controllers
{
    public class AccRating
    {
        // acc rating = ACC_SCALE x Curve(EffectiveAcc(predictedAcc, pass, hit distance) + ACC_OFFSET) ^ -ACC_EXPONENT.
        // portaBLe's balance tuner (Balance Tuning page) suggests ACC_OFFSET; ACC_SCALE is its reference. portaBLe's
        // ReplayUtils.AccRating uses this class, so change them here only.
        // ACC_EXPONENT: 1 / Curve's top end is steeper than prod's acc ratings; fitted on the quantiles of prod's ranked acc
        // ratings. Acc pp takes the acc rating to the inverse power (PpFormula.AccRatingPower), so acc pp scales with the
        // unflattened rating. ACC_SCALE = 1.4528 x 15.1^0.8222 (the old 15.1 / Curve and its display curve combined).
        public const double ACC_SCALE = 13.5382;
        public const double ACC_EXPONENT = 0.8222;
        public const double ACC_OFFSET = 0.0019;

        // Hard map correction. Below HARD_MAP_KNEE predicted acc, top players fall further behind the Acc AI's prediction
        // the harder the map gets, beyond the smooth trend that holds from 96.8% to 99%.
        // The acc rating uses a predicted acc lowered by HARD_MAP_SLOPE times the distance below the knee, capped at
        // HARD_MAP_FLOOR (the edge of the fitted data). Map data only at rating time: the constants were fitted offline on
        // scores (Refresh/Tools/hard_map_offset_fit.py). The stored predicted acc and the per-map curves are unchanged.
        // HARD_MAP_SLOPE 0 = off.
        public const double HARD_MAP_KNEE = 0.9675;
        public const double HARD_MAP_SLOPE = 1.24;
        public const double HARD_MAP_FLOOR = 0.95;

        // Movement and high pass corrections. The Acc AI predicts the accuracy of the notes players cut (its targets leave
        // misses and bad cuts out), and where the hands travel far, or the pass is very high, players lose more to misses and
        // bad cuts. Fitted on the top 300 players' scores after removing each player's own level: HIT_DISTANCE_SLOPE acc per
        // meter of mean hit distance (Analyzer Ratings.HitDistance) above HIT_DISTANCE_KNEE (the median map), and
        // HIGH_PASS_SLOPE per pass point above HIGH_PASS_KNEE.
        // The hit distance one is phased out between 97% and 97.5% predicted acc: the acc rating is much more sensitive to
        // the effective acc on easier maps (steeper curve), so applied everywhere it overpaid easy maps with a lot of
        // movement. Hit distance is capped at 0.8 m, where the shortfall stops growing (extreme spacing got too much).
        // No tech term: tech pp already pays for the acc players lose on tech maps.
        public const double HIT_DISTANCE_KNEE = 0.62;
        public const double HIT_DISTANCE_SLOPE = 0.030;
        public const double HIT_DISTANCE_CAP = 0.80;
        public const double HIT_DISTANCE_FULL_BELOW = 0.97;
        public const double HIT_DISTANCE_NONE_ABOVE = 0.975;
        public const double HIGH_PASS_KNEE = 14.52;
        public const double HIGH_PASS_SLOPE = 0.00214;

        // Very high pass buff, on top of the high pass correction: HIGH_PASS_BUFF less effective acc from HIGH_PASS_BUFF_FULL
        // pass, ramped in linearly from HIGH_PASS_BUFF_START (no step at the threshold; flat above, so the 17 pass maps don't
        // gain more than the 14.5 ones). Each score's pp relative to the player's top pp was ~4% under maps of the same stars
        // on pass 14+ maps at every rank range (weighted worth hid it); their best scores sit under the predicted acc where
        // other maps' are over. HIGH_PASS_BUFF 0 = off.
        public const double HIGH_PASS_BUFF_START = 13.5;
        public const double HIGH_PASS_BUFF_FULL = 14.5;
        public const double HIGH_PASS_BUFF = 0.0055;

        // The predicted acc the acc rating uses (see the corrections above). passRating: the pass rating; hitDistance:
        // Ratings.HitDistance. 0 leaves that correction out.
        public static double EffectiveAcc(double predictedAcc, double passRating = 0, double hitDistance = 0) =>
            predictedAcc - HARD_MAP_SLOPE * Math.Max(0, HARD_MAP_KNEE - Math.Max(predictedAcc, HARD_MAP_FLOOR))
            - HIT_DISTANCE_SLOPE * Math.Max(0, Math.Min(hitDistance, HIT_DISTANCE_CAP) - HIT_DISTANCE_KNEE)
                * Math.Clamp((HIT_DISTANCE_NONE_ABOVE - predictedAcc) / (HIT_DISTANCE_NONE_ABOVE - HIT_DISTANCE_FULL_BELOW), 0, 1)
            - HIGH_PASS_SLOPE * Math.Max(0, passRating - HIGH_PASS_KNEE)
            - HIGH_PASS_BUFF * Math.Clamp((passRating - HIGH_PASS_BUFF_START) / (HIGH_PASS_BUFF_FULL - HIGH_PASS_BUFF_START), 0, 1);

        // Hard swing correction: maps whose hardest swings are scattered over the map (Analyzer Ratings.HardSwingRatio above 1)
        // were worth less than others of their stars, most on pass 11+ maps: the pass rating averages the hardest windows, so
        // isolated hard swings count little, and the Acc AI predicts the accuracy of cut notes, so the misses and bad cuts on
        // them don't count either. The acc rating is multiplied by 1 + HARD_SWING_SLOPE x the excess of (ratio - 1) x pass
        // over HARD_SWING_KNEE, up to HARD_SWING_CAP. A multiplier, not a lower effective acc: the same acc pp change at
        // every predicted acc (the curve is much steeper on easy maps). A steeper slope over-concentrates top plays on the
        // raised maps. One hand maps (ratio 0) aren't changed. HARD_SWING_SLOPE 0 = off.
        public const double HARD_SWING_KNEE = 2.0;
        public const double HARD_SWING_SLOPE = 0.02;
        public const double HARD_SWING_CAP = 0.12;

        // The hard swing correction's multiplier of the acc rating. hardSwingRatio: Ratings.HardSwingRatio (0: none)
        public static double HardSwingMultiplier(double passRating, double hardSwingRatio) =>
            hardSwingRatio > 0
                ? 1 + Math.Min(HARD_SWING_SLOPE * Math.Max(0, (hardSwingRatio - 1) * passRating - HARD_SWING_KNEE), HARD_SWING_CAP)
                : 1;

        public double GetRating(double? predictedAcc, double? passRating, double? techRating, double hitDistance = 0, double hardSwingRatio = 0)
        {
            double difficulty_to_acc;
            if (predictedAcc > 0)
            {
                double effective = EffectiveAcc(predictedAcc ?? 0, passRating ?? 0, hitDistance);
                difficulty_to_acc = ACC_SCALE * Math.Pow(Curve(predictedAcc ?? 0, effective + ACC_OFFSET), -ACC_EXPONENT)
                    * HardSwingMultiplier(passRating ?? 0, hardSwingRatio);
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
