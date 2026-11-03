namespace RatingAPI.Pp
{
    // The pp formula's parameters (used by PpCalculator.GetPp, ToStars, ScorePp). PpFormula.Current is what PpCalculator
    // uses; portaBLe's formula sweeps (BalanceTuner.RunPpFormula) swap it temporarily to try other values.
    //   pass pp = PassScale * e^(PassInner * pass^(1/PassExponent)) - PassOffset   (0 when negative; above PassTopKnee
    //             each point counts PassTopSlope)
    //   acc pp  = curve(acc) * AccWeight * accRating   (the map's own curve, Curve.GetCurve)
    //   tech pp = e^(TechAccExponent * acc) * TechWeight * tech   (above TechTopKnee each point counts TechTopSlope)
    //   pp      = (pass + acc + tech) * PpMultiplier   (Scale)
    //   pp      = pp * (1 + LowStarBonus * (LowStarBonusEnd - stars)^LowStarBonusPower)   (only under LowStarBonusEnd stars)
    //   stars   = final pp of a 96% score / PpMultiplier / StarDivisor, low star bonus included (StarsFromPp). PpMultiplier
    //             doesn't move stars. Linear: no power to look like prod's (stars were 1.1653 x (pp / 83.6925)^1.17507; the
    //             star thresholds below were converted from those, new = (old / 1.1653)^(1 / 1.17507) x 83.6925 / StarDivisor,
    //             so pp is about the same).
    public class PpFormula
    {
        public double PassScale { get; set; } = 12.3;
        public double PassExponent { get; set; } = 2.62;
        public double PassOffset { get; set; } = 30;
        // 1.4862^(-1 / 2.62): the pass formula was written for a pass 1.4862 times smaller (before the pass display curve
        // was folded into AnalyzeMap.PASS_CALIBRATION_FACTOR), so the same map gets the same pass pp
        public double PassInner { get; set; } = 0.85966;
        // Acc and tech pp are linear in the acc and tech ratings, which are linear themselves (no power to look like prod's).
        // 34.519 (was 21.584 with the rating to the power 1.2163 and AccRating.ACC_SCALE 13.5382): the same acc pp.
        public double AccWeight { get; set; } = 34.519;
        // How steeply acc pp follows the curve around 96%: curve(acc) is replaced by curve(96%) x (curve(acc) / curve(96%))^
        // AccCurvePower, so stars (read at 96%) don't move. 1 = the curve as it is. Below 1 evens pass-heavy maps with
        // others of their stars.
        public double AccCurvePower { get; set; } = 0.9;
        // The acc curve power on low-star maps (CurvePowerAt): LowStarCurvePower up to LowStarCurveStart stars, then linearly
        // to AccCurvePower at LowStarCurveEnd. Equal to AccCurvePower = off. Top players score far above 96% on easier maps,
        // so AccCurvePower < 1 would cut their best scores there most. Doesn't move stars (read at 96%).
        public double LowStarCurvePower { get; set; } = 1.0;
        // (was 6.7242 and 9.606 on the old prod-scale stars, 1.1653 x (pp / 83.6925)^1.17507)
        public double LowStarCurveStart { get; set; } = 6.7309;
        public double LowStarCurveEnd { get; set; } = 9.1178;
        // The acc curve power on the lowest-star maps (CurvePowerAt), before the ramp above: VeryLowStarCurvePower up to
        // VeryLowStarCurveStart stars, then linearly to LowStarCurvePower at VeryLowStarCurveEnd. Equal to LowStarCurvePower
        // = off. Lets the best plays on the easiest maps reach those of higher stars; the per-map curve rows (CurveSettings.
        // Table) can't do this since they can only lower the curve. Doesn't move stars (read at 96%).
        public double VeryLowStarCurvePower { get; set; } = 1.08;
        // (was 2.8818 and 4.803 on the old prod-scale stars, 1.1653 x (pp / 83.6925)^1.17507)
        public double VeryLowStarCurveStart { get; set; } = 3.2728;
        public double VeryLowStarCurveEnd { get; set; } = 5.0549;
        // Tuned with BalanceTuner so tech-heavy maps pay the same as others of their stars (tech share slope near 0).
        // 0.587 with the Analyzer's BALANCED_TECH_SCALER 10 (0.13657 with 43.948, after the power 1.2477 was removed): the
        // balance tuner (TargetTolerance 0.002) from 0.54628, tech share slope -0.0046 -> 0, worth spread 0.03168 -> 0.03156.
        // 0.47 with TechTopKnee 4.5 (see there): about the same tech pp on the median map; 0.455 and 0.5045 moved the P75
        // worth by tech little (stars take most of a TechWeight change), 0.47 had the highest tech maps closest to 0.
        // 0.28 with the linear strain + rotation tech (BALANCED_TECH_SCALER 5.933, median tech 2.15 -> 4.03): 0.25 kept the
        // median map's tech pp, 0.28 has the tech-heavy maps for their stars 0.3-0.5% closer to 0 (0.31 only 0.2% more, with
        // more pp inflation).
        // 0.2417 (0.28 / 1.1584) with BALANCED_TECH_SCALER 6.917 (was 5.971): the same tech pp.
        // 0.2677 (0.2417 / 0.90294) with BALANCED_TECH_SCALER 6.2457 (was 6.917): the same tech pp.
        // 5.7887 (0.2677 x e^(3.2 x 0.96)) with TechAccExponent 0: tech pp flat in the acc, as pass pp, the same tech pp at 96%.
        public double TechWeight { get; set; } = 5.7887;
        // 3.27 (was 3.5): on the P75 worth (each player's better efforts), tech 9-13 maps were 1.3-1.6% over others of their
        // stars and tech pp grew too fast with acc; 3.27 halves it (tech share slope +0.0062 -> about 0), as TechWeight 0.131
        // would, with a slightly lower worth spread. 3.2 with the rotation^1.5 / soft capped strain tech (with TechTopKnee 9).
        // 0 (was 3.2): flat tech pp (TechWeight x tech, the acc dependence left to the acc pp and the acc relative pass pp).
        // On the P75 worth the spread 0.0330 -> 0.0319 (1.6: 0.0326, 5: 0.0336); every tech percentile and tech / stars bin
        // within 0.4% with TechTopSlope 0.7, slow / midspeed tech +0.29 / +0.07% (were +0.09 / -0.17%). The acc relative tech
        // multiplier (TechRelExponent) on top raised the spread (0.0335-0.0345).
        public double TechAccExponent { get; set; } = 0;
        // Stars = pp of a 96% score (before PpMultiplier) / StarDivisor. 54 (was 83.6925 when stars went linear, which put
        // 7 prod stars at 4.6): about prod's star numbers again (7 -> 7.1). The star thresholds below are on 54; those on
        // 83.6925 were x 83.6925 / 54 = 1.54986 (the low star bonus x 1 / 1.54986^1.98).
        // 49.4 (was 54): the top end of the stars nearer the Dev database's (p95 / p99 12.10 / 13.44 vs 11.93 / 13.64, was
        // 11.07 / 12.30). The star thresholds here, in RatingsController (speed nerf), the acc reweighter and the worth were
        // x 54 / 49.4 = 1.09312 (the low star bonus x 1 / 1.09312^1.98), so pp is the same.
        // 48.61 (was 49.4): the top map's stars on Dev Acc's (15.58, The Purple Dimension; ours, Unwelcome School, was 15.33), thresholds x 49.4 / 48.61.
        // 55.26 (was 48.61): median stars on Dev Acc's (6.98) with the acc relative pass pp, thresholds x 48.61 / 55.26.
        public double StarDivisor { get; set; } = 55.26;
        // Keeps the top 1000 players' average pp at prod's; retune when the formula changes it. 1.0299 (was 1.0199): the tech
        // refit (TechTopKnee 9, TechAccExponent 3.2) lowered players' pp about 1%.
        // 1.0190 (was 1.0299) with the acc relative pass pp (PassAccExponent 7) and TechTopSlope 1: the top 1000's average pp
        // as before.
        // 1.0173 with the flat tech pp (TechAccExponent 0, TechTopSlope 0.7) and the stronger low note nerf.
        // 1.0238 with the pass pp floor, PassAccExponent 6, no mid star top bonus and LowStarSpeedMaxShare 0.8.
        public double PpMultiplier { get; set; } = 1.0238;

        // Low star bonus: maps under LowStarBonusEnd stars pay more. Top players farm the easiest maps to 99%+, but stars are
        // measured at 96%, so pp vs stars fell off steeply at low stars. Stars include the bonus (StarsFromPp), and its
        // input is those stars. The power puts the bonus on the lowest stars without inflating 700 pp+ scores at 2-8 stars.
        // Worth compares each map with others of its stars, so the acc reweighter doesn't undo it. 0 = off.
        // 0.01948 x (5.504 - stars)^1.98: the old 0.00382 x (8.6454 - stars)^2.3 on the old prod-scale stars, refit on the
        // linear stars (RMS 0.006 of a bonus up to 0.545); 0.008181 x (8.5304 - stars)^1.98 on StarDivisor 54 (the same bonus)
        public double LowStarBonus { get; set; } = 0.008564;
        public double LowStarBonusEnd { get; set; } = 8.3358;
        public double LowStarBonusPower { get; set; } = 1.98;

        // Mid star top bonus: 7-8 star maps pay top players less than on prod (matched scores ~1-3% under, their share of
        // the top 1000's weighted pp 5.8% -> 3.7%). pp x (1 + MidStarTopBonus x bump(stars) x ramp(acc)): bump is 1 from
        // MidStarTopFull to MidStarTopFullEnd stars, easing (smoothstep) to 0 at MidStarTopStart / MidStarTopEnd; ramp eases
        // from 0 at MidStarTopAccStart to 1 at MidStarTopAccFull. The acc ratings aren't touched (a curve change near the
        // predicted acc would cancel through them), and stars (read at 96%, where ramp is 0) don't move. Top players score
        // 96.5-98% on these maps, players ranked 2000+ almost never above 96%. stars: those of the version played (a speed
        // modifier's own), like the low star bonus. 0 = off.
        // Stars (was 6, 7, 8 and 9.5 on the old prod-scale stars, 1.1653 x (pp / 83.6925)^1.17507)
        // 0 (was 0.04): with the acc relative pass pp and flat tech, every star band 4-11 within 0.3% on the P75 worth and the
        // top 100 / top 1000 / ~10000 worth without it (7-8 stars for the top 1000 +0.06 -> +0.03%); their share of the top
        // 1000's weighted pp 2.7 -> 1.5%.
        public double MidStarTopBonus { get; set; } = 0;
        public double MidStarTopStart { get; set; } = 6.1089;
        public double MidStarTopFull { get; set; } = 6.9651;
        public double MidStarTopFullEnd { get; set; } = 7.8033;
        public double MidStarTopEnd { get; set; } = 9.0322;
        public double MidStarTopAccStart { get; set; } = 0.96;
        public double MidStarTopAccFull { get; set; } = 0.975;

        public static PpFormula Current { get; set; } = new();

        public float MidStarTopMultiplier(float stars, float accuracy)
        {
            if (MidStarTopBonus == 0 || !(accuracy > MidStarTopAccStart)) return 1;
            double bump = MidStarTopBump(stars);
            if (bump == 0) return 1;
            double ramp = Ease((accuracy - MidStarTopAccStart) / (MidStarTopAccFull - MidStarTopAccStart));
            return 1 + (float)(MidStarTopBonus * bump * ramp);
        }

        private double MidStarTopBump(float stars)
        {
            if (!(stars > MidStarTopStart && stars < MidStarTopEnd)) return 0;
            return stars < MidStarTopFull ? Ease((stars - MidStarTopStart) / (MidStarTopFull - MidStarTopStart))
                : stars > MidStarTopFullEnd ? Ease((MidStarTopEnd - stars) / (MidStarTopEnd - MidStarTopFullEnd)) : 1;
        }

        private static double Ease(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

        // Tech pp top compression: above TechTopKnee tech, each point counts TechTopSlope. 1 = off. Tech share was only
        // overpaid on the highest-tech maps (13+), so this trims them without touching tech 11-13. 9 (was 12) with the rotation^1.5
        // / soft capped strain tech, whose tech 9-14 maps were 1.2-2.2% over others of their stars on the P75 worth (0.2-0.9%
        // with 9 and TechAccExponent 3.2; the worth spread 0.0298 -> 0.0295). In tech units, so it follows the Analyzer's
        // BALANCED_TECH_SCALER: 2.207 with 10 (9.7 with 43.948). Fitted on the linear tech from the hardest half of the swings
        // with no strain soft cap: the balance tuner's sweep had the tech share slope -0.010 at 8, +0.0085 at 11.1 and +0.037
        // without the compression (worth spread 0.0314 / 0.0317 / 0.0344), all with the 43.948 scale.
        // 4.5 (was 2.207, about the median tech, so half the maps were compressed): on the P75 worth the top 10% of tech
        // were -0.6 to -0.8% (players ranked ~10000: -1.6 to -2.7%) and the 50-75th percentile +0.6%. With 4.5 (about the 89th
        // percentile) and TechWeight 0.47: top 10% -0.4 to +0.5% (top 100 about 0, ~10000 -0.5% at the 90-97th), top tech
        // for stars about 0; the 75-90th percentile +0.8%, under the median -0.5% (TechWeight doesn't move those); worth
        // RMS 0.0308 -> 0.0312. 3.5 left the top 10% -0.3 to -0.5%; 6 overpaid them (+0.7 to +1.2%, top 100 +0.7%).
        // Knee 6 (about the 89th percentile) and slope 1.6, an expansion, with the linear strain + rotation tech, whose top is
        // compressed: with knee 6.1 / slope 0.33 the 90-97th / 97-99th / 99th+ percentiles were -1.0 / -2.6 / -4.3% on the
        // P75 worth, -0.7 / -1.4 / -2.3% with no knee; slope 1.5 -0.4 / -0.1 / -0.4%, 1.75 -0.2 / +0.2 / +0.3% (TechWeight 0.28).
        // 1.2 with the swing tech's powers back (strain^1.25, rotation^1.5), which spread the top: 1.6 overpaid the 97-99th /
        // 99th+ percentiles +1.0 / +2.2%; 1.2 +0.2 / +0.7% (no expansion -0.2 / -0.2%, but tech-heavy maps for their stars
        // -0.4%), the 90-97th -0.3%.
        // 6.95 (6 x 1.1584) with BALANCED_TECH_SCALER 6.917 (was 5.971), the same tech.
        // 6.2754 (6.95 x 0.90294) with BALANCED_TECH_SCALER 6.2457 (was 6.917), the same tech.
        public double TechTopKnee { get; set; } = 6.2754;
        // 1 (was 1.2) with the acc relative pass pp (PassAccExponent 7): 1.2 overpaid the 97-99th / 99th+ percentiles +0.9 /
        // +1.5% (tech-heavy maps for their stars +1.0%), 1 +0.35 / +0.43% (+0.3%).
        // 0.7 with the flat tech pp (TechAccExponent 0), which raised the 97-99th / 99th+ percentiles to +1.2 / +1.7%: now +0.2 /
        // +0.1% (0.5: -0.5 / -1.1%).
        public double TechTopSlope { get; set; } = 0.7;

        public PpFormula Clone() => (PpFormula)MemberwiseClone();

        // Pass pp top compression: above PassTopKnee pass, each point counts PassTopSlope (1 = off). High-pass maps' pp is
        // uneven between players (their 30pp+ top plays are mostly from players ranked ~1000-3000), so no pass pp value
        // suits all of them; the knee trims the outliers without cutting pass 10.4-13 maps.
        public double PassTopKnee { get; set; } = 13;
        public double PassTopSlope { get; set; } = 0.5;

        public float PassPp(float passRating)
        {
            float pass = passRating;
            if (PassTopSlope != 1 && pass > PassTopKnee) pass = (float)(PassTopKnee + (pass - PassTopKnee) * PassTopSlope);
            float passPP = (float)PassScale * MathF.Exp((float)PassInner * MathF.Pow(pass, 1 / (float)PassExponent)) - (float)PassOffset;
            if (float.IsInfinity(passPP) || float.IsNaN(passPP) || passPP < 0) passPP = 0;
            return passPP;
        }

        // The acc curve power of a map with these stars (AccCurvePower, LowStarCurvePower, VeryLowStarCurvePower)
        public double CurvePowerAt(float stars)
        {
            if (VeryLowStarCurvePower != LowStarCurvePower && stars < VeryLowStarCurveEnd)
            {
                if (stars <= VeryLowStarCurveStart) return VeryLowStarCurvePower;
                return VeryLowStarCurvePower + (LowStarCurvePower - VeryLowStarCurvePower) * (stars - VeryLowStarCurveStart) / (VeryLowStarCurveEnd - VeryLowStarCurveStart);
            }
            if (LowStarCurvePower == AccCurvePower || !(stars < LowStarCurveEnd)) return AccCurvePower;
            if (stars <= LowStarCurveStart) return LowStarCurvePower;
            return LowStarCurvePower + (AccCurvePower - LowStarCurvePower) * (stars - LowStarCurveStart) / (LowStarCurveEnd - LowStarCurveStart);
        }

        // The curve value acc pp uses, from the curve at the score's accuracy and at 96% (power: CurvePowerAt)
        public float AccCurve(float curveValue, float curveAt96, double power)
        {
            if (power == 1 || !(curveValue > 0) || !(curveAt96 > 0)) return curveValue;
            return curveAt96 * MathF.Pow(curveValue / curveAt96, (float)power);
        }

        public float AccPp(float curveValue, float accRating) =>
            accRating > 0 ? curveValue * (float)AccWeight * accRating : 0;

        // Acc relative pass pp: pass pp x e^(PassAccExponent x (acc - the map's predicted acc)), so beating the Acc AI's
        // prediction pays more pass pp and falling short of it less (1 at the predicted acc). 0 = off (pass pp doesn't follow
        // the acc). Maps without a predicted acc: 1.
        // 10 with PassAccErrorOffset 1 (the reference is predicted acc - its error): speed maps +0.8% for the top 100 against
        // their other plays (players ranked 5000+ -3.7 to -5.3%), maps predicted 99%+ -3% for the top 100, FS / SF scores 3%+
        // under the predicted acc -1.7% for the top 1000. It spreads players' pp (top 100 +3%, ranked 5000+ -6%): PpMultiplier.
        // 7 (was 10) once the balance tuner's worth used it too: on the P75 worth the pass / stars tilt -0.15 / -0.05% at the
        // 20-40th / 80%+ (was -0.42 / +0.39% without, -0.04 / -0.26% with 10), maps predicted 98%+ with high pass -0.2% (were
        // +0.9%), slow tech +0.1%; the worth spread 0.0308 -> 0.0330 (0.0336 with 10, 0.0325 with 5, which loses the high pass
        // nerf).
        // 6 with the floor (PassAccMinMultiplier 0.8): the top 100's pass / stars 80%+ maps +1.69 -> +1.64%, 700pp+ plays fewer.
        public double PassAccExponent { get; set; } = 6;
        // Cap of that multiplier (and its inverse as the floor), so far-off scores don't run away
        public double PassAccMaxMultiplier { get; set; } = 3;
        // Below the reference: this exponent instead of PassAccExponent (negative = the same), and this floor instead of
        // 1 / PassAccMaxMultiplier (0 = that), so players below their maps' reference can lose less pass pp than the top gains
        public double PassAccExponentBelow { get; set; } = -1;
        // 0.8: players far below their maps' reference lose at most 20% of the pass pp. On the P75 worth the spread 0.0329 ->
        // 0.0314; pass-heavy speed maps for players ranked ~10000 -6.0 -> -4.5%, linear speed maps -5.1 -> -3.7%; the top unchanged.
        public double PassAccMinMultiplier { get; set; } = 0.8;

        // On, the gap is relative to the map's predicted error: (acc - predicted acc) x PassAccReferenceError / (1 - predicted
        // acc), so missing by 1% counts 3 times more on a map predicted at 99% than on one predicted at 96% (with 0.03)
        public double PassAccRelativeError { get; set; } = 0;
        public double PassAccReferenceError { get; set; } = 0.03;

        // The reference acc is lowered by this share of the map's predicted error: predicted acc - PassAccErrorOffset x (1 -
        // predicted acc). Top players fall short of the Acc AI's prediction more on maps that are harder to acc (0.9% at
        // 95-97% predicted, 0.2% at 98-99%), so with 0 the multiplier favors the easy to acc maps.
        public double PassAccErrorOffset { get; set; } = 1;

        public float PassAccMultiplier(float accuracy, float predictedAcc)
        {
            if (PassAccExponent == 0 || !(predictedAcc > 0)) return 1;
            double gap = accuracy - (predictedAcc - PassAccErrorOffset * (1 - predictedAcc));
            if (PassAccRelativeError != 0) gap *= PassAccReferenceError / Math.Max(1 - predictedAcc, 0.001);
            double exponent = gap < 0 && PassAccExponentBelow >= 0 ? PassAccExponentBelow : PassAccExponent;
            double m = Math.Exp(exponent * gap);
            return (float)Math.Clamp(m, PassAccMinMultiplier > 0 ? PassAccMinMultiplier : 1 / PassAccMaxMultiplier, PassAccMaxMultiplier);
        }

        // Acc relative tech pp: tech pp's acc term follows acc - TechAccRelative x (predicted acc - TechAccReference): 0 = the
        // plain acc (as before), 1 = only how far the score is from the map's predicted acc (at TechAccReference's level)
        public double TechAccRelative { get; set; } = 0;
        public double TechAccReference { get; set; } = 0.97;

        public float TechPp(float accuracy, float techRating, float predictedAcc = 0)
        {
            if (!(techRating > 0)) return 0;
            float tech = techRating;
            if (tech > TechTopKnee) tech = (float)(TechTopKnee + (tech - TechTopKnee) * TechTopSlope);
            float acc = TechAccRelative != 0 && predictedAcc > 0 ? accuracy - (float)(TechAccRelative * (predictedAcc - TechAccReference)) : accuracy;
            return MathF.Exp((float)TechAccExponent * acc) * (float)TechWeight * tech * TechAccMultiplier(accuracy, predictedAcc);
        }

        // Acc relative tech pp, as the pass one (PassAccMultiplier): tech pp x e^(TechRelExponent x (acc - (predicted acc -
        // TechAccErrorOffset x (1 - predicted acc)))), capped at TechAccMaxMultiplier (and its inverse). 0 = off.
        public double TechRelExponent { get; set; } = 0;
        public double TechAccErrorOffset { get; set; } = 1;
        public double TechAccMaxMultiplier { get; set; } = 3;

        public float TechAccMultiplier(float accuracy, float predictedAcc)
        {
            if (TechRelExponent == 0 || !(predictedAcc > 0)) return 1;
            double m = Math.Exp(TechRelExponent * (accuracy - (predictedAcc - TechAccErrorOffset * (1 - predictedAcc))));
            return (float)Math.Clamp(m, 1 / TechAccMaxMultiplier, TechAccMaxMultiplier);
        }

        // Stars from pp at 96% (pass + acc + tech, before PpMultiplier), without the low star bonus
        // 1.05 power to scale the stars like old prod
        public float Stars(float ppAt96) => (float)(Math.Pow(ppAt96 / StarDivisor, 1.05));

        // Stars from pp at 96% before the low star bonus (before PpMultiplier): the stars s with
        // s = Stars(ppAt96 * LowStarMultiplier(s)), so they match the final pp of a 96% score, bonus included. The bonus
        // falls as stars rise, so there's exactly one s, between the stars without the bonus and LowStarBonusEnd.
        public float StarsFromPp(float ppAt96)
        {
            float stars = Stars(ppAt96);
            if (!(ppAt96 > 0) || LowStarBonus <= 0 || stars >= LowStarBonusEnd) return stars;

            double lo = stars, hi = LowStarBonusEnd;
            while (hi - lo > 1e-6)
            {
                double mid = (lo + hi) / 2;
                if (mid < Stars(ppAt96 * LowStarMultiplier((float)mid))) lo = mid; else hi = mid;
            }
            return (float)((lo + hi) / 2);
        }

        public float LowStarMultiplier(float stars)
        {
            if (LowStarBonus <= 0 || float.IsNaN(stars) || stars >= LowStarBonusEnd) return 1;
            return 1 + (float)(LowStarBonus * Math.Pow(LowStarBonusEnd - Math.Max(stars, 0), LowStarBonusPower));
        }

        // pp from the sum of pass, acc and tech pp, before the low star bonus
        public float Scale(float pp) => (float)PpMultiplier * pp;

        public override string ToString() => FormattableString.Invariant(
            $"pass {PassScale}*e^({PassInner}*p^(1/{PassExponent}))-{PassOffset} (x{PassTopSlope} above {PassTopKnee}), acc x{AccWeight}*a, tech e^({TechAccExponent}a)*{TechWeight}*t (x{TechTopSlope} above {TechTopKnee}), x{PpMultiplier}, stars pp/{StarDivisor}, low star bonus {LowStarBonus}*({LowStarBonusEnd}-stars)^{LowStarBonusPower}, mid star top bonus {MidStarTopBonus} ({MidStarTopStart}-{MidStarTopFull}-{MidStarTopFullEnd}-{MidStarTopEnd} stars, {MidStarTopAccStart}-{MidStarTopAccFull} acc)");
    }
}
