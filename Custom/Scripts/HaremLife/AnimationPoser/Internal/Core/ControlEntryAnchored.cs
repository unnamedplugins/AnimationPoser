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
		private class ControlEntryAnchored : ControlEntry
		{
			public const int ANCHORMODE_WORLD = 0;
			public const int ANCHORMODE_SINGLE = 1;
			public const int ANCHORMODE_BLEND = 2;

			public const int ANCHORTYPE_OBJECT = 0;
			public const int ANCHORTYPE_ROLE = 1;

			public FreeControllerV3.PositionState myPositionState;
			public FreeControllerV3.RotationState myRotationState;
			public bool myIsEditing = false;
			public ControlTransform myAnchorOffset;
			public ControlTransform myEditingAnchorOffset;
			public Transform myAnchorATransform;
			public Transform myAnchorBTransform;
			public int myAnchorMode = ANCHORMODE_SINGLE;
			public int myAnchorAType = ANCHORTYPE_OBJECT;
			public int myAnchorBType = ANCHORTYPE_OBJECT;
			public float myBlendRatio = DEFAULT_ANCHOR_BLEND_RATIO;
			public float myDampingTime = DEFAULT_ANCHOR_DAMPING_TIME;

			public string myAnchorAAtom;
			public string myAnchorBAtom;
			public string myAnchorAControl = "control";
			public string myAnchorBControl = "control";

			public ControlEntryAnchored(ControlCapture controlCapture) : base(controlCapture)
			{
				Atom containingAtom = myPlugin.GetContainingAtom();
				if (containingAtom.type != "Person" || controlCapture.myName == "control")
					myAnchorMode = ANCHORMODE_WORLD;
				myAnchorAAtom = myAnchorBAtom = containingAtom.uid;

				GetAnchorTransforms();
			}

			public void setDefaults(ControlEntryAnchored entry)
			{
				myAnchorAAtom = entry.myAnchorAAtom;
				myAnchorAControl = entry.myAnchorAControl;
				myAnchorMode = entry.myAnchorMode;
				myAnchorAType = entry.myAnchorAType;
				myAnchorBType = entry.myAnchorBType;
			}

			public ControlEntryAnchored Clone()
			{
				return (ControlEntryAnchored)MemberwiseClone();
			}

			public void Initialize()
			{
				GetAnchorTransforms();
				UpdateInstant();
			}

			public void AdjustAnchor()
			{
				GetAnchorTransforms();
				Capture(myTransform, myPositionState, myRotationState);
			}

			private void GetAnchorTransforms()
			{
				if (myAnchorMode == ANCHORMODE_WORLD)
				{
					myAnchorATransform = null;
					myAnchorBTransform = null;
				}
				else
				{
					myAnchorATransform = GetTransform("A");
					if (myAnchorMode == ANCHORMODE_BLEND)
						myAnchorBTransform = GetTransform("B");
					else
						myAnchorBTransform = null;
				}
			}

			public Transform GetTransform(string anchor) {
				if(anchor == "A")
					return GetTransform(myAnchorAAtom, myAnchorAControl, myAnchorAType);
				else
					return GetTransform(myAnchorBAtom, myAnchorBControl, myAnchorBType);
			}

			private Transform GetTransform(string atomName, string controlName, int anchorType)
			{
				Atom atom = null;
				if (anchorType == ControlEntryAnchored.ANCHORTYPE_OBJECT)
					atom = SuperController.singleton.GetAtomByUid(atomName);
				else if(myRoles.Keys.Contains(atomName))
					atom = myRoles[atomName].myPerson;
				return atom?.GetStorableByID(controlName)?.transform;
			}

			public void UpdateInstant()
			{
				float dampingTime = myDampingTime;
				myDampingTime = 0.0f;
				UpdateTransform();
				if (myControlCapture.myApplyPosition)
					myControlCapture.myTransform.position = myTransform.myPosition;
				if (myControlCapture.myApplyRotation)
					myControlCapture.myTransform.rotation = myTransform.myRotation;
				myDampingTime = dampingTime;
			}

			public ControlTransform GetVirtualAnchorTransform() {
				ControlTransform virtualAnchor;
				if (myAnchorMode == ANCHORMODE_SINGLE)
				{
					if (myAnchorATransform == null)
						return null;
					virtualAnchor = new ControlTransform(myAnchorATransform);
				} else {
					if (myAnchorATransform == null || myAnchorBTransform == null)
						return null;
					virtualAnchor = new ControlTransform(myAnchorATransform, myAnchorBTransform, myBlendRatio);
				}
				return virtualAnchor;
			}

			public void SetEditing() {
				myIsEditing = true;
				ControlTransform anchor = GetVirtualAnchorTransform();
				if(anchor != null)
					myEditingAnchorOffset = anchor.Inverse().Compose(new ControlTransform(
						myControlCapture.myTransform
					));
			}

			public void UpdateTransform()
			{
				ControlTransform offset = myIsEditing ? myEditingAnchorOffset : myAnchorOffset;

				if (myAnchorMode == ANCHORMODE_WORLD)
				{
					myTransform = offset;
				}
				else
				{
					ControlTransform anchor = GetVirtualAnchorTransform();
					if(anchor == null)
						return;

					if (myDampingTime >= 0.001f)
					{
						float t = Mathf.Clamp01(Time.deltaTime / myDampingTime);
						myTransform = new ControlTransform(myTransform, anchor.Compose(offset), t);
					}
					else
					{
						myTransform = anchor.Compose(offset);
					}
				}
			}

			public void Capture(ControlTransform transform,
									FreeControllerV3.PositionState positionState,
									FreeControllerV3.RotationState rotationState)
			{
				myIsEditing = false;
				myPositionState = positionState;
				myRotationState = rotationState;

				myTransform = transform;

				if (myAnchorMode == ANCHORMODE_WORLD)
				{
					myAnchorOffset = new ControlTransform(transform);
				}
				else
				{
					ControlTransform anchor = GetVirtualAnchorTransform();
					if(anchor == null)
						return;

					myAnchorOffset = anchor.Inverse().Compose(transform);
				}
			}
		}
    }
}