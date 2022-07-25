using UnityEngine;
using UnityEngine.Events;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
        private static float[] _p;
        private static float[] _d;
        private static float[] _w; // Weights
        private static float[] _p1; // Out
        private static float[] _p2; // In
        private static float[] _r; // rhs vector
        private static float[] _a;
        private static float[] _b;
        private static float[] _c;

        private static List<ControlPoint> AutoComputeControlPoints(List<float> keys, List<float> times)
        {
            // Original implementation: https://www.particleincell.com/wp-content/uploads/2012/06/bezier-spline.js
            // Based on: https://www.particleincell.com/2012/bezier-splines/
            // Using improvements on near keyframes: http://www.jacos.nl/jacos_html/spline/
            // Looped version: http://www.jacos.nl/jacos_html/spline/circular/index.html
            var n = keys.Count - 1;
            InitializeArrays(n);
            Weighting(times, n);
            InternalSegments(keys, n);
            ThomasAlgorithm();
            Rearrange(n);
            return AssignControlPoints(keys, n);
        }

        private static void InitializeArrays(int n)
        {
            if (_w == null || _w.Length < n + 1)
            {
                _w = new float[n + 1];
                _p1 = new float[n + 1];
                _p2 = new float[n];
                // rhs vector
                // TODO: *2 only for non-looping?
                _a = new float[n * 2];
                _b = new float[n * 2];
                _c = new float[n * 2];
                _d = new float[n * 2];
                _r = new float[n * 2];
                _p = new float[n * 2];
            }
        }

        private static void Weighting(List<float> times, int n)
        {
            for (var i = 0; i < n; i++)
            {
                _w[i] = times[i] - times[i+1];
            }
            _w[n] = _w[n-1];
        }

        private static void InternalSegments(List<float> keys, int n)
        {
            // left most segment
            var idx = 0;
            _a[idx] = 0; // outside the matrix
            _b[idx] = 2;
            _c[idx] = -1;
            _d[idx] = 0;
            _r[idx] = keys[0] + 0;// add curvature at K0

            // internal segments
            for (var i = 1; i < n; i++)
            {
                idx = 2 * i - 1;
                _a[idx] = 1 * _w[i] * _w[i];
                _b[idx] = -2 * _w[i] * _w[i];
                _c[idx] = 2 * _w[i - 1] * _w[i - 1];
                _d[idx] = -1 * _w[i - 1] * _w[i - 1];
                _r[idx] = keys[i] * (-_w[i] * _w[i] + _w[i - 1] * _w[i - 1]);

                idx = 2 * i;
                _a[idx] = _w[i];
                _b[idx] = _w[i - 1];
                _c[idx] = 0;
                _d[idx] = 0; // note: d[2n-2] is already outside the matrix
                _r[idx] = (_w[i - 1] + _w[i]) * keys[i];

            }

            // right segment
            idx = 2 * n - 1;
            _a[idx] = -1;
            _b[idx] = 2;
            _r[idx] = keys[n]; // curvature at last point
            _c[idx] = 0; // outside the matrix
            _d[2 * n - 2] = 0; // outside the matrix
            _d[idx] = 0; // outside the matrix
        }

        private static void ThomasAlgorithm()
        {
            var n = _r.Length;

            // the following array elements are not in the original matrix, so they should not have an effect
            _a[0] = 0; // outside the matrix
            _c[n - 1] = 0; // outside the matrix
            _d[n - 2] = 0; // outside the matrix
            _d[n - 1] = 0; // outside the matrix

            /* solves Ax=b with the Thomas algorithm (from Wikipedia) */
            /* adapted for a 4-diagonal matrix. only the a[i] are under the diagonal, so the Gaussian elimination is very similar */
            for (var i = 1; i < n; i++)
            {
                var m = _a[i] / _b[i - 1];
                _b[i] = _b[i] - m * _c[i - 1];
                _c[i] = _c[i] - m * _d[i - 1];
                _r[i] = _r[i] - m * _r[i - 1];
            }

            _p[n - 1] = _r[n - 1] / _b[n - 1];
            _p[n - 2] = (_r[n - 2] - _c[n - 2] * _p[n - 1]) / _b[n - 2];
            for (var i = n - 3; i >= 0; --i)
            {
                _p[i] = (_r[i] - _c[i] * _p[i + 1] - _d[i] * _p[i + 2]) / _b[i];
            }
        }

        private static void Rearrange(int n)
        {
            for (var i = 0; i < n; i++)
            {
                _p1[i] = _p[2 * i];
                _p2[i] = _p[2 * i + 1];
            }
        }

		private struct ControlPoint
		{
			public float In;
			public float Out;
		}

        private static List<ControlPoint> AssignControlPoints(List<float> keys, int n)
        {
            List<ControlPoint> controlPoints = new List<ControlPoint>();

            ControlPoint firstControlPoint = new ControlPoint();
            var key0 = keys[0];
            firstControlPoint.Out = _p1[0];
            controlPoints.Add(firstControlPoint);

            for (var i = 1; i < n; i++)
            {
                ControlPoint controlPoint = new ControlPoint();
                var keyi = keys[i];
                controlPoint.In = _p2[i - 1];
                controlPoint.Out = _p1[i];
                controlPoints.Add(controlPoint);
            }

            ControlPoint lastControlPoint = new ControlPoint();
            var keyn = keys[n];
            lastControlPoint.In = _p2[n - 1];
            controlPoints.Add(lastControlPoint);

            return controlPoints;
        }
    }

}
