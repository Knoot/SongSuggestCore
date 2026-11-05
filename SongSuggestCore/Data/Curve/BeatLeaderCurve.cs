using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BeatLeader;
using SongLibraryNS;

namespace Curve
{
    // Adapted from BeatLeader RatingAPI / website; see THIRD-PARTY-NOTICES.md.
    public static class BeatLeaderCurve
    {
        public const string ModelVersion = "ppv4.1-2026-11-05";
        private static readonly ConcurrentDictionary<string, double[][]> Curves = new ConcurrentDictionary<string, double[][]>();
        private static readonly double[][] Base = {
            new[]{0.0,0.0}, new[]{0.6,0.297}, new[]{0.65,0.343}, new[]{0.7,0.4},
            new[]{0.75,0.469}, new[]{0.8,0.549}, new[]{0.825,0.584}, new[]{0.85,0.627},
            new[]{0.875,0.675}, new[]{0.9,0.727}, new[]{0.91,0.76}, new[]{0.92,0.804},
            new[]{0.93,0.861}, new[]{0.94,0.926}, new[]{0.95,1.0}, new[]{0.955,1.039},
            new[]{0.96,1.094}, new[]{0.965,1.167}, new[]{0.97,1.256}, new[]{0.9725,1.315},
            new[]{0.975,1.392}, new[]{0.9775,1.49}, new[]{0.98,1.618}, new[]{0.9825,1.786},
            new[]{0.985,2.007}, new[]{0.9875,2.303}, new[]{0.99,2.7}, new[]{0.9925,3.241},
            new[]{0.995,4.01}, new[]{0.9975,5.158}, new[]{0.999,6.241}, new[]{1.0,7.2}
        };
        // predictedAcc, A, C, K, W, K2, P2
        private static readonly double[][] Rows = {
            new[]{0.955,1.0,-5.3,4.86,0.6,2.88,2.46}, new[]{0.96,0.99,-3.0,4.3,0.66,4.8,1.85},
            new[]{0.965,0.988,-2.4,4.5,0.64,4.5,1.95}, new[]{0.97,0.986,-2.2,4.7,0.62,4.2,2.05},
            new[]{0.975,0.983,0.0,4.86,0.6,3.8,2.15}, new[]{0.98,0.983,0.0,4.86,0.6,3.35,2.3},
            new[]{0.985,0.985,-0.6,4.86,0.6,2.88,2.46}, new[]{0.99,0.99,0.0,4.86,0.6,2.88,2.46},
            new[]{0.995,0.99,0.0,4.86,0.6,2.88,2.46}
        };
        private static readonly double[] Slopes = BuildSlopes();
        public static bool IsFinite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        private static double Clamp(double n, double low, double high) => Math.Min(high, Math.Max(low, n));

        public static double PP(double accuracy, BeatLeaderMapRating r)
        {
            if (!IsFinite(accuracy) || accuracy < 0 || accuracy > 1) throw new ArgumentOutOfRangeException(nameof(accuracy));
            if (r == null || !r.IsComplete) throw new InvalidOperationException("Incomplete BeatLeader PPv4 metadata.");
            if (accuracy == 0) return 0;
            var curve = Curves.GetOrAdd(r.CurveKey, _ => BuildCurve(r));
            if (Curves.Count > 5000) Curves.Clear();
            double stars = Stars(r, curve);
            double value = Interpolate(accuracy, curve), at96 = Interpolate(0.96, curve), power = CurvePower(stars);
            if (value > 0 && at96 > 0 && power != 1) value = at96 * Math.Pow(value / at96, power);
            double passPp = PassPp(r.PassRating.Value) * PassAccMultiplier(accuracy, r.PredictedAcc.Value);
            double accPp = value * 34.519 * r.AccRating.Value * MidStarTopMultiplier(stars, accuracy, r.PredictedAcc.Value);
            double result = 1.0128 * (passPp + accPp + TechPp(r.TechRating.Value)) * LowStarMultiplier(stars);
            if (!IsFinite(result)) throw new InvalidOperationException("Non-finite PPv4 result.");
            return result;
        }

        private static double CurvePower(double stars)
        {
            if (stars < 5.3718) return stars <= 3.478 ? 1.08 : 1.08 - 0.08 * (stars - 3.478) / (5.3718 - 3.478);
            if (stars <= 7.1529) return 1;
            return stars < 9.6894 ? 1 - 0.1 * (stars - 7.1529) / (9.6894 - 7.1529) : 0.9;
        }

        public static double Stars(BeatLeaderMapRating r)
        {
            if (r == null || !r.IsComplete) throw new InvalidOperationException("Incomplete BeatLeader PPv4.1 metadata.");
            return Stars(r, BuildCurve(r));
        }

        private static double Stars(BeatLeaderMapRating r, double[][] curve)
        {
            // Stars use the unbonused 96% parts, then solve the implicit low-star bonus.
            // The mid-star score bonus is deliberately excluded from this calculation.
            double basis = (PassPp(r.PassRating.Value) * PassAccMultiplier(0.96, r.PredictedAcc.Value) +
                Interpolate(0.96, curve) * 34.519 * r.AccRating.Value + TechPp(r.TechRating.Value)) / 52;
            if (basis <= 0 || basis >= 8.8584) return basis;
            double lo = basis, hi = 8.8584;
            while (hi - lo > 1e-6)
            {
                double mid = (lo + hi) / 2;
                if (mid < basis * LowStarMultiplier(mid)) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        private static double PassPp(double pass)
        {
            if (pass > 13) pass = 13 + (pass - 13) * 0.5;
            return Math.Max(0, 12.3 * Math.Exp(0.85966 * Math.Pow(pass, 1.0 / 2.62)) - 30);
        }

        private static double PassAccMultiplier(double accuracy, double predicted) => predicted <= 0 ? 1 :
            Clamp(Math.Exp(6 * (accuracy - (predicted - (1 - predicted)))), 0.8, 3);

        private static double TechPp(double tech) => 5.7887 * (tech > 6.2754 ? 6.2754 + (tech - 6.2754) * 0.7 : tech);
        private static double LowStarMultiplier(double stars) => stars >= 8.8584 ? 1 :
            1 + 0.007593 * Math.Pow(8.8584 - Math.Max(stars, 0), 1.98);
        private static double Ease(double value) { double t = Clamp(value, 0, 1); return t * t * (3 - 2 * t); }

        private static double MidStarTopMultiplier(double stars, double accuracy, double predicted)
        {
            if (predicted <= 0 || stars <= 4.2508 || stars >= 10.5 || accuracy <= predicted - 0.03) return 1;
            double bump = stars < 6.3762 ? Ease((stars - 4.2508) / (6.3762 - 4.2508)) :
                stars > 8 ? Ease((10.5 - stars) / (10.5 - 8)) : 1;
            return 1 + 0.05 * bump * Ease((accuracy - (predicted - 0.03)) / 0.03);
        }

        private static double[] Params(double predicted)
        {
            if (predicted <= Rows[0][0]) return Rows[0];
            for (int i = 1; i < Rows.Length; i++)
            {
                if (predicted > Rows[i][0]) continue;
                double t = (predicted - Rows[i - 1][0]) / (Rows[i][0] - Rows[i - 1][0]);
                return Rows[i - 1].Zip(Rows[i], (a, b) => a + t * (b - a)).ToArray();
            }
            return Rows[Rows.Length - 1];
        }

        private static double Modified(double x, double[] p)
        {
            double a = p[1], c = Math.Min(p[2], 1), k = p[3], w = p[4], k2 = p[5], p2 = p[6];
            double m = (1 - 0.256) / 0.35 * (1 - w + w * k * Math.Exp(k) / (Math.Exp(k) - 1));
            Func<double, double> f2 = z => 1 + m * (z - 0.95) + (6.424 - 0.05 * m) *
                (Math.Exp(k2 * Math.Pow((z - 0.95) / 0.05, p2)) - 1) / (Math.Exp(k2) - 1);
            if (x <= 0.6) return 0.256 / 0.6 * x;
            if (x <= 0.95)
            {
                double u = (x - 0.6) / 0.35;
                return 0.256 + (1 - 0.256) * ((1 - w) * u + w * (Math.Exp(k * u) - 1) / (Math.Exp(k) - 1));
            }
            if (x <= a) return f2(x);
            double ua = (a - 0.95) / 0.05;
            double slope = m + (6.424 - 0.05 * m) * Math.Exp(k2 * Math.Pow(ua, p2)) / (Math.Exp(k2) - 1) * k2 * p2 / 0.05 * Math.Pow(ua, p2 - 1);
            double s1 = slope * (1 - c);
            return Math.Abs(a - 1) < 1e-9 ? f2(a) + s1 * (x - a) :
                f2(a) + s1 * (x - a) + (slope - s1) * (1 - a) * (1 - Math.Exp(-(x - a) / (1 - a)));
        }

        private static double[][] BuildCurve(BeatLeaderMapRating r)
        {
            double predicted = r.PredictedAcc.Value;
            var p = Params(predicted);
            bool dynamic = predicted > 0.6 && predicted < 0.9995;
            bool modified = dynamic && !(p[1] >= 0.9995 && p[3] == 4.86 && p[4] == 0.6 && p[5] == 2.88 && p[6] == 2.46);
            double tilt = dynamic ? 0.2 * r.SliderPercentage.Value : 0;
            double nerf = Factor(r.Notes.Value, 115, 20, 0.8031) * Factor(r.NoteSpan.Value, 75, 12, 0.9);
            if (!modified && tilt == 0) return Base.Select(v => new[] { v[0], v[1] * nerf }).ToArray();
            var xs = new HashSet<double>(Base.Select(v => v[0]));
            for (int i = 0; i < 71; i++) xs.Add(Math.Round(0.6 + i * 0.005, 6));
            for (int i = 0; i < 101; i++) xs.Add(Math.Round(0.95 + i * 0.0005, 6));
            xs.Add(Math.Round(p[1], 6));
            var standard = new[] { 0.0, 1.0, 1.0, 4.86, 0.6, 2.88, 2.46 };
            return xs.OrderBy(x => x).Select(x => {
                double value = Interpolate(x, Base);
                if (modified)
                {
                    double factor = TableFactor(x, standard);
                    if (p[1] < 0.9995 && x > p[1])
                    {
                        const double h = 1e-5;
                        double atA = TableFactor(p[1], standard);
                        factor = atA + (atA - TableFactor(p[1] - h, standard)) / h * (x - p[1]);
                    }
                    double smooth = Smooth(x);
                    value = Math.Min(smooth, factor > 0 ? Modified(x, p) * factor : smooth);
                }
                value *= Math.Exp(tilt * Clamp(x - predicted, -0.015, 0.015) * 100);
                return new[] { x, value * nerf };
            }).ToArray();
        }

        private static double Factor(double n, double start, double reference, double minimum) =>
            n <= 0 || n >= start ? 1 : Clamp(1 - (1 - minimum) * Math.Log(start / n) / Math.Log(start / reference), minimum, 1);

        private static double TableFactor(double x, double[] standard)
        {
            double reference = Modified(x, standard);
            return reference > 0 ? Smooth(x) / reference : 0;
        }

        private static double Interpolate(double x, double[][] points)
        {
            int lo = 0, hi = points.Length - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (points[mid][0] < x) lo = mid; else hi = mid; }
            double t = (x - points[lo][0]) / (points[hi][0] - points[lo][0]);
            return points[lo][1] + t * (points[hi][1] - points[lo][1]);
        }

        private static double[] BuildSlopes()
        {
            var d = new double[Base.Length - 1]; var h = new double[d.Length]; var s = new double[Base.Length];
            for (int i = 0; i < d.Length; i++) { h[i] = Base[i + 1][0] - Base[i][0]; d[i] = (Base[i + 1][1] - Base[i][1]) / h[i]; }
            s[0] = d[0]; s[s.Length - 1] = d[d.Length - 1];
            for (int i = 1; i < s.Length - 1; i++)
            {
                if (d[i - 1] * d[i] <= 0) continue;
                double w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1];
                s[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]);
            }
            return s;
        }

        private static double Smooth(double x)
        {
            if (x <= 0) return 0; if (x >= 1) return Base[Base.Length - 1][1];
            int i = 0; while (Base[i + 1][0] < x) i++;
            double h = Base[i + 1][0] - Base[i][0], t = (x - Base[i][0]) / h, t2 = t * t, t3 = t2 * t;
            return (2 * t3 - 3 * t2 + 1) * Base[i][1] + (t3 - 2 * t2 + t) * h * Slopes[i] +
                (-2 * t3 + 3 * t2) * Base[i + 1][1] + (t3 - t2) * h * Slopes[i + 1];
        }
    }
}
