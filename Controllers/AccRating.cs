namespace RatingAPI.Controllers
{
    public class AccRating
    {
        // acc rating = ACC_SCALE / Curve(EffectiveAcc(predictedAcc, hit distance) + ACC_OFFSET) x HardSwingMultiplier, linear
        // (no power to look like prod's).
        // portaBLe's balance tuner (Balance Tuning page) suggests ACC_OFFSET; ACC_SCALE is its reference. portaBLe's
        // ReplayUtils.AccRating uses this class, so change them here only.
        // ACC_SCALE 14.8727: the same median acc rating (8.765) as with the old power 0.8222 (13.5382 with it).
        public const double ACC_SCALE = 14.8727;
        public const double ACC_OFFSET = 0.0019;

        // The corrections' constants below are settable for experiments (portaBLe reacc); change their defaults here.
        // Removed 2026-11-07 with the Acc AI trained on whole-leaderboard anchored targets, which predicts these maps' shortfall
        // itself: the hard map correction (predicted acc lowered under 96.75%), the high pass correction and buff (lowered
        // above pass 14.52 / from 13.5) and their speed modifier variant (the nomod pass). Without them worth by predicted acc
        // and by pass was flat (95-96.75% -0.5..+0.6%, pass 9+ -1.8..+1.5%).

        // Movement correction. The Acc AI predicts the accuracy of the notes players cut (its targets leave misses and bad cuts
        // out), and where the hands travel far players lose more to misses and bad cuts. Fitted on the top 300 players' scores
        // after removing each player's own level: HIT_DISTANCE_SLOPE acc per meter of mean hit distance (Analyzer
        // Ratings.HitDistance) above HIT_DISTANCE_KNEE (the median map).
        // It is phased out between 97% and 97.5% predicted acc: the acc rating is much more sensitive to
        // the effective acc on easier maps (steeper curve), so applied everywhere it overpaid easy maps with a lot of
        // movement. Hit distance is capped at 0.8 m, where the shortfall stops growing (extreme spacing got too much).
        // No tech term: tech pp already pays for the acc players lose on tech maps.
        // HIT_DISTANCE_SLOPE 0.015 (was 0.030) with the current pass, tech and pp formula: on the P75 worth, 0.7 m+ maps were
        // 0.8-1.2% over others of their stars at every predicted acc with 0.030, and 0.8-1.6% under on hard maps without
        // the correction; with 0.015 every band is within 0.3% (2026-11-07, exp-hd-*.db).
        public static double HIT_DISTANCE_KNEE { get; set; } = 0.62;
        // 0.010 (was 0.015) with the 3-seed anchor 1.0 Acc AI (2026-11-08): 0.8 m+ maps were 0.65% over, 0.7-0.8 m 0.3%.
        public static double HIT_DISTANCE_SLOPE { get; set; } = 0.010;
        public static double HIT_DISTANCE_CAP { get; set; } = 0.80;
        public static double HIT_DISTANCE_FULL_BELOW { get; set; } = 0.97;
        public static double HIT_DISTANCE_NONE_ABOVE { get; set; } = 0.975;
        // Share of the hit distance correction kept above HIT_DISTANCE_NONE_ABOVE (0: phased out entirely). 0.2: on the P75
        // worth, easy maps (97.5%+) over 0.7 m were 1.1% under the others with none (-0.0111 -> -0.0009 with 0.2; 0.4 and 0.8
        // overshot to +0.009 / +0.028). 0 since 2026-11-07 (with HIT_DISTANCE_SLOPE 0.015): 0.2 put those maps 1.2% over,
        // none leaves them even (-0.0002).
        public static double HIT_DISTANCE_EASY_SHARE { get; set; } = 0;
        // The predicted acc the acc rating uses (see the correction above). hitDistance: Ratings.HitDistance (0 leaves the
        // correction out).
        public static double EffectiveAcc(double predictedAcc, double hitDistance = 0) =>
            predictedAcc - HIT_DISTANCE_SLOPE * Math.Max(0, Math.Min(hitDistance, HIT_DISTANCE_CAP) - HIT_DISTANCE_KNEE)
                * Math.Max(HIT_DISTANCE_EASY_SHARE, Math.Clamp((HIT_DISTANCE_NONE_ABOVE - predictedAcc) / (HIT_DISTANCE_NONE_ABOVE - HIT_DISTANCE_FULL_BELOW), 0, 1));

        // Hard swing correction: maps whose hardest swings are scattered over the map (Analyzer Ratings.HardSwingRatio above 1)
        // were worth less than others of their stars, most on pass 11+ maps: the pass rating averages the hardest windows, so
        // isolated hard swings count little, and the Acc AI predicts the accuracy of cut notes, so the misses and bad cuts on
        // them don't count either. The acc rating is multiplied by 1 + HARD_SWING_SLOPE x the excess of (ratio - 1) x pass
        // over HARD_SWING_KNEE, up to HARD_SWING_CAP. A multiplier, not a lower effective acc: the same acc pp change at
        // every predicted acc (the curve is much steeper on easy maps). A steeper slope over-concentrates top plays on the
        // raised maps. One hand maps (ratio 0) aren't changed. HARD_SWING_SLOPE 0 = off. HARD_SWING_KNEE 1 (was 2): on the P75
        // worth the maps just past the knee were still 0.3-0.7% under (pass 9+: corrected -0.0073 against +0.0052 without); with
        // 1 and the lower TechAccExponent both are about 0, and the 700pp+ scores spread over more maps (447 -> 484, at most 26
        // -> 15 on one map). A slope of 0.03 instead left the maps just past the knee under.
        // Slope and cap x1.2163 (were 0.02 and 0.12 with acc pp following the acc rating to the power 1.2163): the same pp.
        public static double HARD_SWING_KNEE { get; set; } = 1.0;
        // 0.006 (was 0.0243) with the anchored Acc AI: 0.0243 put ratio x pass 2-6 maps +2.3..+5.6% over, none -1.0%
        // 0.008 (was 0.006) with the 3-seed anchor 1.0 Acc AI (2026-11-08): ratio x pass 1-2 / 2-4 were 0.2 / 0.45% under.
        public static double HARD_SWING_SLOPE { get; set; } = 0.008;
        public static double HARD_SWING_CAP { get; set; } = 0.148;

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
                double effective = EffectiveAcc(predictedAcc ?? 0, hitDistance);
                difficulty_to_acc = ACC_SCALE / Curve(predictedAcc ?? 0, effective + ACC_OFFSET)
                    * HardSwingMultiplier(passRating ?? 0, hardSwingRatio);
            }
            else
            {
                // No predicted acc: prod's old formula, then from its scale (13.5382 x curve^-0.8222) to the linear one
                double tiny_tech = 0.0208f * (techRating ?? 0) + 1.1284f;
                double old = (-Math.Pow(tiny_tech, -(passRating ?? 0)) + 1) * 8 + 2 + 0.01f * (techRating ?? 0) * (passRating ?? 0);
                difficulty_to_acc = old > 0 ? ACC_SCALE * Math.Pow(old / 13.5382, 1 / 0.8222) : old;
            }
            if (double.IsInfinity(difficulty_to_acc) || double.IsNaN(difficulty_to_acc) || double.IsNegativeInfinity(difficulty_to_acc))
            {
                difficulty_to_acc = 0;
            }
            return difficulty_to_acc;
        }

        // The map's own per-map acc curve (ModifiableCurve, see Curve.GetCurve), picked by its predicted acc, at acc; with the
        // curve rows' RatingC (CurveRow.RatingC)
        public static float Curve(double predictedAcc, double acc) => (float)Controllers.Curve.PerMapValue(predictedAcc, acc, forRating: true);
    }
}
