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
        public const string ModelVersion = "ppv4-2026-10-27";
        private static readonly ConcurrentDictionary<string, double[][]> Curves = new ConcurrentDictionary<string, double[][]>();
        private static readonly double[][] Base = {
            new[]{0.0,0.0}, new[]{0.6,0.307}, new[]{0.65,0.355}, new[]{0.7,0.414},
            new[]{0.75,0.485}, new[]{0.8,0.568}, new[]{0.825,0.605}, new[]{0.85,0.649},
            new[]{0.875,0.699}, new[]{0.9,0.753}, new[]{0.91,0.781}, new[]{0.92,0.813},
            new[]{0.93,0.867}, new[]{0.94,0.931}, new[]{0.95,1.0}, new[]{0.955,1.039},
            new[]{0.96,1.094}, new[]{0.965,1.167}, new[]{0.97,1.256}, new[]{0.9725,1.315},
            new[]{0.975,1.392}, new[]{0.9775,1.49}, new[]{0.98,1.618}, new[]{0.9825,1.786},
            new[]{0.985,2.007}, new[]{0.9875,2.303}, new[]{0.99,2.7}, new[]{0.9925,3.241},
            new[]{0.995,4.01}, new[]{0.9975,5.158}, new[]{0.999,6.241}, new[]{1.0,7.424}
        };
        // predictedAcc, A, C, K, W, K2, P2
        private static readonly double[][] Rows = {
            new[]{0.955,1.0,-5.3,4.86,0.6,2.88,2.46}, new[]{0.96,0.99,-3.0,4.3,0.66,4.8,1.85},
            new[]{0.965,0.988,-2.4,4.5,0.64,4.5,1.95}, new[]{0.97,0.986,-2.2,4.7,0.62,4.2,2.05},
            new[]{0.975,0.985,-1.8,4.86,0.6,3.8,2.15}, new[]{0.98,0.985,-1.4,4.86,0.6,3.35,2.3},
            new[]{0.985,0.985,-1.2,4.86,0.6,2.88,2.46}, new[]{0.99,0.9915,-1.2,4.86,0.6,2.88,2.46},
            new[]{0.995,0.9915,-1.2,4.86,0.6,2.88,2.46}
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
            double value = Interpolate(accuracy, curve), at96 = Interpolate(0.96, curve), power = CurvePower(r.Stars.Value);
            if (value > 0 && at96 > 0 && power != 1) value = at96 * Math.Pow(value / at96, power);
            double pass = r.PassRating.Value;
            if (pass > 13) pass = 13 + (pass - 13) * 0.5;
            double passPp = Math.Max(0, 12.3 * Math.Exp(0.85966 * Math.Pow(pass, 1.0 / 2.62)) - 30);
            double accPp = value * 21.584 * Math.Pow(r.AccRating.Value, 1.2163);
            double tech = r.TechRating.Value;
            if (tech > 12) tech = 12 + (tech - 12) * 0.33;
            double techPp = Math.Exp(3.5 * accuracy) * 0.1612 * Math.Pow(tech, 1.2477);
            double bonus = r.Stars.Value >= 8.6454 ? 1 : 1 + 0.0063371 * Math.Pow(8.6454 - Math.Max(r.Stars.Value, 0), 1.36);
            double result = 1.023 * (passPp + accPp + techPp) * bonus;
            if (!IsFinite(result)) throw new InvalidOperationException("Non-finite PPv4 result.");
            return result;
        }

        private static double CurvePower(double stars)
        {
            if (stars < 4.803) return stars <= 2.8818 ? 1.08 : 1.08 - 0.08 * (stars - 2.8818) / (4.803 - 2.8818);
            if (stars <= 6.7242) return 1;
            return stars < 9.606 ? 1 - 0.1 * (stars - 6.7242) / (9.606 - 6.7242) : 0.9;
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
            double nerf = Factor(r.Notes.Value, 115, 20, 0.95) * Factor(r.NoteSpan.Value, 75, 12, 0.9);
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
                    double smooth = Smooth(x), reference = Modified(x, standard);
                    value = Math.Min(smooth, reference > 0 ? smooth * Modified(x, p) / reference : smooth);
                }
                value *= Math.Exp(tilt * Clamp(x - predicted, -0.015, 0.015) * 100);
                return new[] { x, value * nerf };
            }).ToArray();
        }

        private static double Factor(double n, double start, double reference, double minimum) =>
            n <= 0 || n >= start ? 1 : Clamp(1 - (1 - minimum) * Math.Log(start / n) / Math.Log(start / reference), minimum, 1);

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
