namespace RatingAPI.Pp
{
    // The pp formula's parameters (used by PpCalculator.GetPp, ToStars, ScorePp). PpFormula.Current is what PpCalculator
    // uses; portaBLe's formula sweeps (BalanceTuner.RunPpFormula) swap it temporarily to try other values.
    //   pass pp = PassScale * e^(PassInner * pass^(1/PassExponent)) - PassOffset   (0 when negative; above PassTopKnee
    //             each point counts PassTopSlope)
    //   acc pp  = curve(acc) * AccWeight * accRating^AccRatingPower   (the map's own curve, Curve.GetCurve)
    //   tech pp = e^(TechAccExponent * acc) * TechWeight * tech^TechRatingPower
    //   pp      = (pass + acc + tech) * PpMultiplier   (Scale)
    //   pp      = pp * (1 + LowStarBonus * (LowStarBonusEnd - stars)^LowStarBonusPower)   (only under LowStarBonusEnd stars)
    //   stars   = StarScale * (final pp of a 96% score / PpMultiplier / StarDivisor)^StarExponent, low star bonus
    //             included (StarsFromPp). PpMultiplier doesn't move stars.
    public class PpFormula
    {
        public double PassScale { get; set; } = 12.3;
        public double PassExponent { get; set; } = 2.62;
        public double PassOffset { get; set; } = 30;
        // 1.4862^(-1 / 2.62): the pass formula was written for a pass 1.4862 times smaller (before the pass display curve
        // was folded into AnalyzeMap.PASS_CALIBRATION_FACTOR), so the same map gets the same pass pp
        public double PassInner { get; set; } = 0.85966;
        // AccRatingPower / TechRatingPower: inverses of AccRating.ACC_EXPONENT (0.8222) and Analyzer Ratings.TechExponent
        // (0.8015), so acc and tech pp scale with the unflattened ratings (prod-scale ratings are flatter at the top).
        // With pp linear in the ratings, acc-heavy maps lost pp against pass-heavy ones of the same stars.
        public double AccWeight { get; set; } = 21.584;
        public double AccRatingPower { get; set; } = 1.2163;
        // How steeply acc pp follows the curve around 96%: curve(acc) is replaced by curve(96%) x (curve(acc) / curve(96%))^
        // AccCurvePower, so stars (read at 96%) don't move. 1 = the curve as it is. Below 1 evens pass-heavy maps with
        // others of their stars.
        public double AccCurvePower { get; set; } = 0.9;
        // The acc curve power on low-star maps (CurvePowerAt): LowStarCurvePower up to LowStarCurveStart stars, then linearly
        // to AccCurvePower at LowStarCurveEnd. Equal to AccCurvePower = off. Top players score far above 96% on easier maps,
        // so AccCurvePower < 1 would cut their best scores there most. Doesn't move stars (read at 96%).
        public double LowStarCurvePower { get; set; } = 1.0;
        // x0.9606 to match StarScale (7, 10 stars before it)
        public double LowStarCurveStart { get; set; } = 6.7242;
        public double LowStarCurveEnd { get; set; } = 9.606;
        // The acc curve power on the lowest-star maps (CurvePowerAt), before the ramp above: VeryLowStarCurvePower up to
        // VeryLowStarCurveStart stars, then linearly to LowStarCurvePower at VeryLowStarCurveEnd. Equal to LowStarCurvePower
        // = off. Lets the best plays on the easiest maps reach those of higher stars; the per-map curve rows (CurveSettings.
        // Table) can't do this since they can only lower the curve. Doesn't move stars (read at 96%).
        public double VeryLowStarCurvePower { get; set; } = 1.08;
        // x0.9606 to match StarScale (3, 5 stars before it)
        public double VeryLowStarCurveStart { get; set; } = 2.8818;
        public double VeryLowStarCurveEnd { get; set; } = 4.803;
        // Tuned with BalanceTuner so tech-heavy maps pay the same as others of their stars (tech share slope near 0).
        public double TechWeight { get; set; } = 0.1612;
        public double TechRatingPower { get; set; } = 1.2477;
        // 3.27 (was 3.5): on the P75 worth (each player's better efforts), tech 9-13 maps were 1.3-1.6% over others of their
        // stars and tech pp grew too fast with acc; 3.27 halves it (tech share slope +0.0062 -> about 0), as TechWeight 0.131
        // would, with a slightly lower worth spread. 3.2 with the rotation^1.5 / soft capped strain tech (with TechTopKnee 9).
        public double TechAccExponent { get; set; } = 3.2;
        // Stars and players' pp on prod's scale. StarScale / StarExponent: fitted on the quantiles of prod's ranked stars
        // (pp at 96% is steeper at the top than prod's stars). StarScale includes x0.9606 so Dev's star distribution matches
        // prod's; the star thresholds pp reads (curve power ramps, LowStarBonusEnd) are scaled with it so pp is unchanged.
        public double StarDivisor { get; set; } = 83.6925;
        public double StarScale { get; set; } = 1.1653;
        public double StarExponent { get; set; } = 1.17507;
        // Keeps the top 1000 players' average pp at prod's; retune when the formula changes it. 1.0299 (was 1.0199): the tech
        // refit (TechTopKnee 9, TechAccExponent 3.2) lowered players' pp about 1%.
        public double PpMultiplier { get; set; } = 1.0299;

        // Low star bonus: maps under LowStarBonusEnd stars pay more. Top players farm the easiest maps to 99%+, but stars are
        // measured at 96%, so pp vs stars fell off steeply at low stars. Stars include the bonus (StarsFromPp), and its
        // input is those stars. The power puts the bonus on the lowest stars without inflating 700 pp+ scores at 2-8 stars.
        // Worth compares each map with others of its stars, so the acc reweighter doesn't undo it. 0 = off.
        public double LowStarBonus { get; set; } = 0.00382;
        public double LowStarBonusEnd { get; set; } = 8.6454;
        public double LowStarBonusPower { get; set; } = 2.3;

        // Mid star top bonus: 7-8 star maps pay top players less than on prod (matched scores ~1-3% under, their share of
        // the top 1000's weighted pp 5.8% -> 3.7%). pp x (1 + MidStarTopBonus x bump(stars) x ramp(acc)): bump is 1 from
        // MidStarTopFull to MidStarTopFullEnd stars, easing (smoothstep) to 0 at MidStarTopStart / MidStarTopEnd; ramp eases
        // from 0 at MidStarTopAccStart to 1 at MidStarTopAccFull. The acc ratings aren't touched (a curve change near the
        // predicted acc would cancel through them), and stars (read at 96%, where ramp is 0) don't move. Top players score
        // 96.5-98% on these maps, players ranked 2000+ almost never above 96%. stars: those of the version played (a speed
        // modifier's own), like the low star bonus. 0 = off.
        public double MidStarTopBonus { get; set; } = 0.04;
        public double MidStarTopStart { get; set; } = 6;
        public double MidStarTopFull { get; set; } = 7;
        public double MidStarTopFullEnd { get; set; } = 8;
        public double MidStarTopEnd { get; set; } = 9.5;
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
        // with 9 and TechAccExponent 3.2; the worth spread 0.0298 -> 0.0295).
        public double TechTopKnee { get; set; } = 9;
        public double TechTopSlope { get; set; } = 0.33;

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
            accRating > 0 ? curveValue * (float)AccWeight * MathF.Pow(accRating, (float)AccRatingPower) : 0;

        public float TechPp(float accuracy, float techRating)
        {
            if (!(techRating > 0)) return 0;
            // The Analyzer's top compression of the rating undone first (display only), then the pp one
            float tech = (float)global::beatleader_analyzer.BeatmapScanner.Data.Ratings.UncompressTech(techRating);
            if (tech > TechTopKnee) tech = (float)(TechTopKnee + (tech - TechTopKnee) * TechTopSlope);
            return MathF.Exp((float)TechAccExponent * accuracy) * (float)TechWeight * MathF.Pow(tech, (float)TechRatingPower);
        }

        // Stars from pp at 96% (pass + acc + tech, before PpMultiplier), without the low star bonus
        public float Stars(float ppAt96)
        {
            double basis = ppAt96 / StarDivisor;
            return basis > 0 ? (float)(StarScale * Math.Pow(basis, StarExponent)) : (float)basis;
        }

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
            $"pass {PassScale}*e^({PassInner}*p^(1/{PassExponent}))-{PassOffset} (x{PassTopSlope} above {PassTopKnee}), acc x{AccWeight}*a^{AccRatingPower}, tech e^({TechAccExponent}a)*{TechWeight}*t^{TechRatingPower} (x{TechTopSlope} above {TechTopKnee}), x{PpMultiplier}, stars {StarScale}*(pp/{StarDivisor})^{StarExponent}, low star bonus {LowStarBonus}*({LowStarBonusEnd}-stars)^{LowStarBonusPower}, mid star top bonus {MidStarTopBonus} ({MidStarTopStart}-{MidStarTopFull}-{MidStarTopFullEnd}-{MidStarTopEnd} stars, {MidStarTopAccStart}-{MidStarTopAccFull} acc)");
    }
}
