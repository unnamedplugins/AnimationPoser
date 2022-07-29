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
		private class ControlTransform
		{
			public Vector3 myPosition;
			public Quaternion myRotation;

			public ControlTransform(Vector3 position, Quaternion rotation) {
				myPosition = position;
				myRotation = rotation;
			}

			public ControlTransform(Transform transform) {
				myPosition = transform.position;
				myRotation = transform.rotation;
			}

			public ControlTransform(ControlTransform transform) {
				myPosition = transform.myPosition;
				myRotation = transform.myRotation;
			}

			public ControlTransform(Transform transform1, Transform transform2, float blendRatio) {
				myPosition = Vector3.LerpUnclamped(transform1.position, transform2.position, blendRatio);
				myRotation = Quaternion.SlerpUnclamped(transform1.rotation, transform2.rotation, blendRatio);
			}

			public ControlTransform(ControlTransform transform1, ControlTransform transform2, float blendRatio) {
				myPosition = Vector3.LerpUnclamped(transform1.myPosition, transform2.myPosition, blendRatio);
				myRotation = Quaternion.SlerpUnclamped(transform1.myRotation, transform2.myRotation, blendRatio);
			}

			public ControlTransform Compose(ControlTransform transform) {
				return new ControlTransform(
					myPosition + myRotation * transform.myPosition,
					myRotation * transform.myRotation
				);
			}

			public ControlTransform Inverse() {
				return new ControlTransform(
					-(Quaternion.Inverse(myRotation) * myPosition),
					Quaternion.Inverse(myRotation)
				);
			}

			public static ControlTransform Identity() {
				return new ControlTransform(
					Vector3.zero,
					Quaternion.identity
				);
			}
		}
    }
}