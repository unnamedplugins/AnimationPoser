namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private class ControlCapture
		{
			public string myName;
			public Transform myTransform;
			private ControlTimeline myTimeline;
			public bool myApplyPosition = true;
			public bool myApplyRotation = true;
			FreeControllerV3 myController;

			private static Quaternion[] ourTempQuaternions = new Quaternion[3];
			private static float[] ourTempDistances = new float[DISTANCE_SAMPLES[DISTANCE_SAMPLES.Length-1] + 2];

			public ControlCapture(AnimationPoser plugin, string control)
			{
				myName = control;
				FreeControllerV3 controller = plugin.containingAtom.GetStorableByID(control) as FreeControllerV3;
				if (controller != null)
					myTransform = controller.transform;

				myController = controller;
			}

			public void CaptureEntry(State state)
			{
				ControlEntryAnchored entry;
				if (!state.myControlEntries.TryGetValue(this, out entry))
				{
					entry = new ControlEntryAnchored(this);
					state.myControlEntries[this] = entry;
					CaptureEntry(entry);
					return;
				} else {
					ControlTransform oldTransform = new ControlTransform(entry.myAnchorOffset);

					CaptureEntry(entry);
					if(state.myIsRootState)
						TransformLayer(state, state.myLayer, oldTransform);
				}
			}

			public void TransformLayer(State rootState, Layer layer, ControlTransform oldTransform) {
				foreach(var s in layer.myStates) {
					State st = s.Value;
					if(st != rootState) {
						ControlEntryAnchored rootCe = rootState.myControlEntries[this];
						ControlEntryAnchored ce = st.myControlEntries[this];

						ce.myAnchorOffset = rootCe.myAnchorOffset.Compose(
							oldTransform.Inverse().Compose(ce.myAnchorOffset)
						);
					}
				}
			}

			public FreeControllerV3.PositionState GetPositionState() {
				if(myController.name == "control")
					return FreeControllerV3.PositionState.On;
				else
					return myController.currentPositionState;
			}

			public void SetPositionState(FreeControllerV3.PositionState state) {
				myController.currentPositionState = state;
			}

			public void SetRotationState(FreeControllerV3.RotationState state) {
				myController.currentRotationState = state;
			}

			public FreeControllerV3.RotationState GetRotationState() {
				if(myController.name == "control")
					return FreeControllerV3.RotationState.On;
				else
					return myController.currentRotationState;
			}

			public void CaptureEntry(ControlEntryAnchored entry) {
				entry.Capture(
					new ControlTransform(myTransform),
					GetPositionState(),
					GetRotationState()
				);
			}

			public void setDefaults(State state, State oldState)
			{
				ControlEntryAnchored entry;
				ControlEntryAnchored oldEntry;
				if (!state.myControlEntries.TryGetValue(this, out entry))
					return;
				if (!oldState.myControlEntries.TryGetValue(this, out oldEntry))
					return;
				entry.setDefaults(oldEntry);
			}

			public void UpdateState(State state)
			{
				ControlEntryAnchored entry;
				if (state.myControlEntries.TryGetValue(this, out entry))
				{
					entry.UpdateTransform();
					if (myApplyPosition)
						myTransform.position = entry.myTransform.myPosition;
					if (myApplyRotation)
						myTransform.rotation = entry.myTransform.myRotation;
				}
			}

			public bool IsValid()
			{
				return myTransform != null;
			}
		}
    }
}