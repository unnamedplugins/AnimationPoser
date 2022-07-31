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
            _w = new float[n + 1];
            _p1 = new float[n + 1];
            _p2 = new float[n];
            _a = new float[n * 2];
            _b = new float[n * 2];
            _c = new float[n * 2];
            _d = new float[n * 2];
            _r = new float[n * 2];
            _p = new float[n * 2];
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

		private static float EvalBezier(float t, float c1, float? c2, float? c3, float c4) {
            if(c2 != null && c3 != null)
                return EvalBezierCubic(t, c1, c2 ??0, c3 ??0, c4);
            return EvalBezierLinear(t, c1, c4);
        }

		private static ControlTransform EvalBezier(float t, ControlTransform c1, ControlTransform c2, ControlTransform c3, ControlTransform c4) {
            if(c2 != null && c3 != null)
                return new ControlTransform(
                    EvalBezierCubic(t, c1.myPosition, c2.myPosition, c3.myPosition, c4.myPosition),
                    EvalBezierCubic(t, c1.myRotation, c2.myRotation, c3.myRotation, c4.myRotation)
                );
            else
                return new ControlTransform(
                    EvalBezierLinear(t, c1.myPosition, c4.myPosition),
                    EvalBezierLinear(t, c1.myRotation, c4.myRotation)
                );
        }

        private static float EvalBezierLinear(float t, float c1, float c2) {
            return Mathf.LerpUnclamped(c1, c2, t);
        }

        private static Vector3 EvalBezierLinear(float t, Vector3 c1, Vector3 c2) {
            return Vector3.LerpUnclamped(c1, c2, t);
        }

        private static Quaternion EvalBezierLinear(float t, Quaternion c1, Quaternion c2) {
            return Quaternion.SlerpUnclamped(c1, c2, t);
        }

        // evaluating using Bernstein polynomials
        private static float EvalBezierQuadratic(float t, float c1, float c2, float c3)
        {
            float s = 1.0f - t;
            return (s*s) * c1 + (2.0f*s*t) * c2 + (t*t) * c3;
        }

        private static Vector3 EvalBezierQuadratic(float t, Vector3 c1, Vector3 c2, Vector3 c3)
        {
            float s = 1.0f - t;
            return (s*s) * c1 + (2.0f*s*t) * c2 + (t*t) * c3;
        }

        // evaluating quadratic Bézier curve using de Casteljau's algorithm
        private static Quaternion EvalBezierQuadratic(float t, Quaternion c1, Quaternion c2, Quaternion c3)
        {
            Quaternion temp1 = Quaternion.SlerpUnclamped(c1, c2, t);
            Quaternion temp2 = Quaternion.SlerpUnclamped(c2, c3, t);
            return Quaternion.SlerpUnclamped(temp1, temp2, t);
        }

        // evaluating cubic Bézier curve using Bernstein polynomials
        private static float EvalBezierCubic(float t, float c1, float c2, float c3, float c4)
        {
            float s = 1.0f - t;
            float t2 = t*t;
            float s2 = s*s;
            return (s*s2) * c1 + (3.0f*s2*t) * c2 + (3.0f*s*t2) * c3 + (t*t2) * c4;
        }

        private static Vector3 EvalBezierCubic(float t, Vector3 c1, Vector3 c2, Vector3 c3, Vector3 c4)
        {
            float s = 1.0f - t;
            float t2 = t*t;
            float s2 = s*s;
            return (s*s2) * c1 + (3.0f*s2*t) * c2 + (3.0f*s*t2) * c3 + (t*t2) * c4;
        }

        // evaluating cubic Bézier curve using de Casteljau's algorithm
        private static Quaternion EvalBezierCubic(float t, Quaternion c1, Quaternion c2, Quaternion c3, Quaternion c4)
        {
            Quaternion temp1 = Quaternion.SlerpUnclamped(c1, c2, t);
            Quaternion temp2 = Quaternion.SlerpUnclamped(c2, c3, t);
            Quaternion temp3 = Quaternion.SlerpUnclamped(c3, c4, t);

            temp1 = Quaternion.SlerpUnclamped(temp1, temp2, t);
            temp2 = Quaternion.SlerpUnclamped(temp2, temp3, t);

            return Quaternion.SlerpUnclamped(temp1, temp2, t);
        }

        private static float Smooth(float a, float b, float d, float t)
        {
            d = Mathf.Max(d, 0.01f);
            t = Mathf.Clamp(t, 0.0f, d);
            if (a+b>d)
            {
                float scale = d/(a+b);
                a *= scale;
                b *= scale;
            }
            float n = d - 0.5f*(a+b);
            float s = d - t;

            // This is based on using the SmoothStep function (3x^2 - 2x^3) for velocity: https://en.wikipedia.org/wiki/Smoothstep
            // The result is a 3-piece curve consiting of a linear part in the middle and the integral of SmoothStep at both
            // ends. Additionally there is some scaling to connect the parts properly.
            // The resulting combined curve has smooth velocity and continuous acceleration/deceleration.
            float ta = t / a;
            float sb = s / b;
            if (t < a)
                return (a - 0.5f*t) * (ta*ta*ta/n);
            else if (s >= b)
                return (t - 0.5f*a) / n;
            else
                return (0.5f*s - b) * (sb*sb*sb/n) + 1.0f;
        }

        // private static float ArcLengthParametrization(float t)
        // {
        // 	if (myEntryCount <= 2 || myEntryCount > 4){
        // 		return t;
        // 	}

        // 	int numSamples = DISTANCE_SAMPLES[myEntryCount];
        // 	float numLines = (float)(numSamples+1);
        // 	float distance = 0.0f;
        // 	Vector3 previous = myCurve[0].myEntry.myPosition;
        // 	ourTempDistances[0] = 0.0f;

        // 	if (myEntryCount == 3)
        // 	{
        // 		for (int i=1; i<=numSamples; ++i)
        // 		{
        // 			Vector3 current = EvalBezierQuadraticPosition(i / numLines);
        // 			distance += Vector3.Distance(previous, current);
        // 			ourTempDistances[i] = distance;
        // 			previous = current;
        // 		}
        // 	}
        // 	else
        // 	{
        // 		for (int i=1; i<=numSamples; ++i)
        // 		{
        // 			Vector3 current = EvalBezierCubicPosition(i / numLines);
        // 			distance += Vector3.Distance(previous, current);
        // 			ourTempDistances[i] = distance;
        // 			previous = current;
        // 		}
        // 	}

        // 	distance += Vector3.Distance(previous, myCurve[myEntryCount-1].myEntry.myPosition);
        // 	ourTempDistances[numSamples+1] = distance;

        // 	t *= distance;

        // 	int idx = Array.BinarySearch(ourTempDistances, 0, numSamples+2, t);
        // 	if (idx < 0)
        // 	{
        // 		idx = ~idx;
        // 		if (idx == 0){
        // 			return 0.0f;
        // 		}
        // 		else if (idx >= numSamples+2){
        // 			return 1.0f;
        // 		}
        // 		t = Mathf.InverseLerp(ourTempDistances[idx-1], ourTempDistances[idx], t);
        // 		return Mathf.LerpUnclamped((idx-1) / numLines, idx / numLines, t);
        // 	}
        // 	else
        // 	{
        // 		return idx / numLines;
        // 	}
        // }
    }
}
