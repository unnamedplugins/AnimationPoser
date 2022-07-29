using UnityEngine;
using UnityEngine.Events;
using System;
using System.Linq;
using System.Collections.Generic;
using SimpleJSON;

namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private class ControlTimeline : Timeline {
			public ControlEntryAnchored myStartEntry;
			public ControlEntryAnchored myEndEntry;
			public ControlCapture myControlCapture;

			public ControlTimeline(ControlCapture controlCapture) {
				myControlCapture = controlCapture;
			}

			public void SetEndpoints(ControlEntryAnchored startControlEntry,
									 ControlEntryAnchored endControlEntry) {
				myStartEntry = startControlEntry;
				myEndEntry = endControlEntry;

				if(myKeyframes.FirstOrDefault(k=>k.myIsFirst) == null) {
					ControlEntry startEntry = new ControlEntry(myControlCapture);
					startEntry.myTransform = ControlTransform.Identity();
					AddKeyframe(new ControlKeyframe("first", startEntry));
				}

				if(myKeyframes.FirstOrDefault(k=>k.myIsLast) == null) {
					ControlEntry endEntry = new ControlEntry(myControlCapture);
					endEntry.myTransform = ControlTransform.Identity();
					AddKeyframe(new ControlKeyframe("last", endEntry));
				}

				ComputeControlPoints();
			}

			public ControlTransform GetVirtualAnchor(float time) {
				return new ControlTransform(myStartEntry.myTransform, myEndEntry.myTransform, time);
			}

			public ControlEntryAnchored GenerateEntry(float time) {
				ControlEntryAnchored entry;
				entry = new ControlEntryAnchored(myControlCapture);
				myControlCapture.CaptureEntry(entry);
				entry.Initialize();
				entry.myTransform = GetVirtualAnchor(time).Inverse().Compose(entry.myTransform);
				return entry;
			}

			public override void CaptureKeyframe(float time) {
				ControlEntryAnchored entry = GenerateEntry(time);
				AddKeyframe(new ControlKeyframe(time, entry));
			}

			public override void UpdateKeyframe(Keyframe keyframe) {
				RemoveKeyframe(keyframe);
				CaptureKeyframe(keyframe.myTime);
			}

			public override void ComputeControlPoints() {
				myKeyframes = new List<Keyframe>(myKeyframes.OrderBy(k => k.myTime));
				List<float> ts = new List<float>();
				List<float> xs = new List<float>();
				List<float> ys = new List<float>();
				List<float> zs = new List<float>();
				List<float> rxs = new List<float>();
				List<float> rys = new List<float>();
				List<float> rzs = new List<float>();
				List<float> rws = new List<float>();
				if(myKeyframes.Count < 3)
					return;


				for(int i=0; i<myKeyframes.Count; i++) {
					ControlKeyframe controlKeyframe = myKeyframes[i] as ControlKeyframe;
					ControlTransform ce = controlKeyframe.myControlEntry.myTransform;
					ts.Add(controlKeyframe.myTime);
					xs.Add(ce.myPosition.x);
					ys.Add(ce.myPosition.y);
					zs.Add(ce.myPosition.z);
					rxs.Add(ce.myRotation.x);
					rys.Add(ce.myRotation.y);
					rzs.Add(ce.myRotation.z);
					rws.Add(ce.myRotation.w);
				}

				List<ControlPoint> xControlPoints = AutoComputeControlPoints(xs, ts);
				List<ControlPoint> yControlPoints = AutoComputeControlPoints(ys, ts);
				List<ControlPoint> zControlPoints = AutoComputeControlPoints(zs, ts);
				List<ControlPoint> rxControlPoints = AutoComputeControlPoints(rxs, ts);
				List<ControlPoint> ryControlPoints = AutoComputeControlPoints(rys, ts);
				List<ControlPoint> rzControlPoints = AutoComputeControlPoints(rzs, ts);
				List<ControlPoint> rwControlPoints = AutoComputeControlPoints(rws, ts);

				for(int i=0; i<myKeyframes.Count; i++) {
					ControlKeyframe controlKeyframe = myKeyframes[i] as ControlKeyframe;
					ControlEntry controlPointIn = controlKeyframe.myControlPointIn;
					if(controlPointIn == null) {
						controlPointIn = new ControlEntry(myControlCapture);
						controlKeyframe.myControlPointIn = controlPointIn;
					}

					controlPointIn.myTransform = new ControlTransform(
						new Vector3(xControlPoints[i].In, yControlPoints[i].In, zControlPoints[i].In),
						new Quaternion(rxControlPoints[i].In, ryControlPoints[i].In, rzControlPoints[i].In, rwControlPoints[i].In)
					);

					ControlEntry controlPointOut = controlKeyframe.myControlPointOut;
					if(controlPointOut == null) {
						controlPointOut = new ControlEntry(myControlCapture);
						controlKeyframe.myControlPointOut = controlPointOut;
					}

					controlPointOut.myTransform = new ControlTransform(
						new Vector3(xControlPoints[i].Out, yControlPoints[i].Out, zControlPoints[i].Out),
						new Quaternion(rxControlPoints[i].Out, ryControlPoints[i].Out, rzControlPoints[i].Out, rwControlPoints[i].Out)
					);
				}
			}

			public void UpdateCurve(float t)
			{
				//t = ArcLengthParametrization(t);

				myKeyframes = new List<Keyframe>(myKeyframes.OrderBy(k => k.myTime));
				ControlTransform virtualAnchor = GetVirtualAnchor(t);

                int n = BinarySearch(t);
				ControlKeyframe k1 = myKeyframes[n] as ControlKeyframe;
				ControlKeyframe k2 = myKeyframes[n+1] as ControlKeyframe;

				t = (t-k1.myTime)/(k2.myTime-k1.myTime);

				Vector3 c1, c4; Vector3? c2, c3;
				Quaternion rc1, rc4; Quaternion? rc2, rc3;
				c2=c3=null; rc2=rc3=null;

				c1 = k1.myControlEntry.myTransform.myPosition;
				rc1 = k1.myControlEntry.myTransform.myRotation;
				c4 = k2.myControlEntry.myTransform.myPosition;
				rc4 = k2.myControlEntry.myTransform.myRotation;
				if(k1.myControlPointOut != null) {
					c2 = k1.myControlPointOut.myTransform.myPosition;
					rc2 = k1.myControlPointOut.myTransform.myRotation;
				}
				if(k2.myControlPointIn != null) {
					c3 = k2.myControlPointIn.myTransform.myPosition;
					rc3 = k2.myControlPointIn.myTransform.myRotation;
				}

				ControlTransform transform = virtualAnchor.Compose(
					new ControlTransform(
						EvalBezier(t, c1, c2, c3, c4),
						EvalBezier(t, rc1, rc2, rc3, rc4)
					)
				);

				if (myControlCapture.myApplyPosition)
//	 && k2.myControlEntry.myPositionState != FreeControllerV3.PositionState.Off
					myControlCapture.myTransform.position = transform.myPosition;

				if (myControlCapture.myApplyRotation)
//  && k2.myControlEntry.myRotationState != FreeControllerV3.RotationState.Off
					myControlCapture.myTransform.rotation = transform.myRotation;
			}

			public void UpdateControllerStates() {
				myControlCapture.SetPositionState(myEndEntry.myPositionState);
				myControlCapture.SetRotationState(myEndEntry.myRotationState);
			}

			public void Merge(ControlTimeline timeline2, float transition1Duration, float transition2Duration) {
				float totalDuration = transition1Duration + transition2Duration;

				for (int i=0; i<myKeyframes.Count; ++i) {
					ControlKeyframe keyframe = myKeyframes[i] as ControlKeyframe;
					keyframe.myControlEntry.myTransform = GetVirtualAnchor(keyframe.myTime).Compose(
						keyframe.myControlEntry.myTransform
					);
					keyframe.myTime = keyframe.myTime * transition1Duration/totalDuration;
					keyframe.myIsLast = false;
				}

				for (int i=1; i<timeline2.myKeyframes.Count; ++i) {
					ControlKeyframe keyframe = timeline2.myKeyframes[i] as ControlKeyframe;
					keyframe.myControlEntry.myTransform = timeline2.GetVirtualAnchor(keyframe.myTime).Compose(
						keyframe.myControlEntry.myTransform
					);
					keyframe.myTime = transition1Duration/totalDuration + keyframe.myTime * transition2Duration/totalDuration;
					myKeyframes.Add(keyframe);
				}

				myKeyframes.Remove(myKeyframes[0]);
				myKeyframes.Remove(myKeyframes[myKeyframes.Count-1]);
			}
		}
    }
}