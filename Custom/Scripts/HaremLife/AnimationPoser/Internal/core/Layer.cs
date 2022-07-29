namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
    {
		private class Layer : AnimationObject
		{
			public Animation myAnimation;
			public Dictionary<string, State> myStates = new Dictionary<string, State>();
			public State myCurrentState;
			public List<ControlCapture> myControlCaptures = new List<ControlCapture>();
			public List<MorphCapture> myMorphCaptures = new List<MorphCapture>();
			private Transition myTransition;
			public float myClock = 0.0f;
			public float myDuration = 1.0f;
			private List<TriggerActionDiscrete> myTriggerActionsNeedingUpdate = new List<TriggerActionDiscrete>();
			public List<State> myStateChain = new List<State>();

			public Layer(string name) : base(name)
			{
				myAnimation = myCurrentAnimation;
			}

			public void AddCapture(ControlCapture capture) {
				myControlCaptures.Add(capture);
				foreach (var s in myStates) {
					State state = s.Value;
					capture.CaptureEntry(state);
				}

				foreach (var s in myStates) {
					State state = s.Value;
					foreach(BaseTransition t in state.myTransitions) {
						if(t is Transition) {
							Transition transition = t as Transition;
							transition.SetEndpoints(transition.mySourceState, transition.myTargetState);
						}
					}
				}
			}

			public void RemoveCapture(ControlCapture capture) {
				myControlCaptures.Remove(capture);
				foreach(State s in myStates.Values.ToList()) {
					foreach(BaseTransition t in s.myTransitions) {
						if(t is Transition) {
							Transition transition = t as Transition;
							if(transition.myControlTimelines.Keys.Contains(capture))
								transition.myControlTimelines.Remove(capture);
						}
					}
					s.myControlEntries.Remove(capture);
				}
			}

			public void CaptureState(State state)
			{
				for (int i=0; i<myControlCaptures.Count; ++i) {
					myControlCaptures[i].CaptureEntry(state);
				}
				for (int i=0; i<myMorphCaptures.Count; ++i)
					myMorphCaptures[i].CaptureEntry(state);
			}

			public void SetState(State state)
			{
				myCurrentState = state;

				myClock = 0.0f;
				myDuration = UnityEngine.Random.Range(state.myWaitDurationMin, state.myWaitDurationMax);

				if (myMainLayer.val == myName) {
					myMainState.valNoCallback = myCurrentState.myName;
					myMainAnimation.valNoCallback = myCurrentState.myAnimation().myName;
				}
			}

			public void Clear()
			{
				foreach (var s in myStates)
				{
					State state = s.Value;
					state.EnterBeginTrigger.Remove();
					state.EnterEndTrigger.Remove();
					state.ExitBeginTrigger.Remove();
					state.ExitEndTrigger.Remove();
				}
			}

			public void UpdateLayer()
			{
				for (int i=0; i<myTriggerActionsNeedingUpdate.Count; ++i)
					myTriggerActionsNeedingUpdate[i].Update();
				myTriggerActionsNeedingUpdate.RemoveAll(a => !a.timerActive);

				float deltaTime = Time.deltaTime*myCurrentAnimation.mySpeed;

				if(myMenuItem == MENU_TIMELINES)
					return;

				if(myClock >= myDuration) {
					if(myTransition != null) {
						myTransition.Advance(deltaTime);
						if (myTransition.myFinished) {
							ArriveAtState();
						}
						return;
					} else {
						SetNextTransition();
					}
				}

				if(!myPaused) {
					myClock = Mathf.Min(myClock + deltaTime, 100000.0f);
					UpdateState();
				}
			}

			public void UpdateState() {
				for (int i=0; i<myControlCaptures.Count; ++i)
					myControlCaptures[i].UpdateState(myCurrentState);
			}

			public void ArriveAtState() {
				SetState(myTransition.myTargetState);
				myTransition.EndTransition(myTriggerActionsNeedingUpdate);
				myTransition = null;
			}

			public State CreateBlendState()
			{
				State blendState = new State("BlendState", this) {
					myWaitDurationMin = 0.0f,
					myWaitDurationMax = 0.0f,
					myDefaultDuration = myGlobalDefaultTransitionDuration.val,
				};
				CaptureState(blendState);
				return blendState;
			}

			public void GoTo(State state)
			{
				myStateChain.Clear();
				if (myCurrentState == null || myCurrentState == state) {
					myStateChain.Add(CreateBlendState());
					myStateChain.Add(state);
				} else {
					List<State> path = myCurrentState.findPath(state);
					if(path != null) {
						myStateChain = path;
					} else {
						myStateChain.Add(myCurrentState);
						myStateChain.Add(state);
					}
				}
				SetNextTransition();
			}

			public void FillStateChain() {
				if(myStateChain.Count == 0)
					myStateChain.Add(myCurrentState);
				while(myStateChain.Count < MAX_STATES) {
					State state = myStateChain.Last();
					State nextState = state.sortNextState();
					if(nextState == null)
						break;
					List<State> path = state.findPath(nextState);
					path.RemoveAt(0);
					foreach(State s in path) {
						myStateChain.Add(s);
					}
				}
			}

			public void SetNextTransition() {
				if(!myPaused)
					FillStateChain();

				myClock = myDuration;

				myTransition = null;
				if(myStateChain.Count < 2)
					return;

				State sourceState = myStateChain[0];
				State targetState = myStateChain[1];
				myStateChain.RemoveAt(0);

				Transition transition;
				if(sourceState.isReachable(targetState)) {
					transition = sourceState.getIncomingTransition(targetState) as Transition;
				} else {
					transition = new Transition(sourceState, targetState);
				}

				transition.StartTransition(myTriggerActionsNeedingUpdate);

				if(targetState.myAnimation() != sourceState.myAnimation()) {
					Animation animation = targetState.myAnimation();
					Layer targetLayer = targetState.myLayer;

					float noise = UnityEngine.Random.Range(-transition.myDurationNoise, transition.myDurationNoise);

					foreach(var l in animation.myLayers) {
						Layer layer = l.Value;
						if(layer == targetLayer || transition.mySyncTargets.Keys.Contains(layer))
							continue;
						layer.GoToAnyState(transition.myDuration, noise);
					}

					targetLayer.BlendTo(targetState, transition.myDuration, noise);
					targetLayer.myStateChain = new List<State>(myStateChain);

					SetAnimation(animation);
				} else {
					SetTransition(transition);
				}
			}

			public void GoToAnyState(float transitionDuration, float noise) {
				if(myCurrentState == null) {
					List<string> states = myStates.Keys.ToList();
					states.Sort();
					if(states.Count() > 0)
						BlendTo(myStates[states[0]], transitionDuration, noise);
				} else
					BlendTo(myCurrentState, transitionDuration, noise);
			}

			public void BlendTo(State targetState, float transitionDuration, float noise) {
				Transition transition = new Transition(CreateBlendState(), targetState, transitionDuration);
				SetTransition(transition);
				myTransition.myNoise = noise;
			}

			public void SetTransition(Transition transition, float t=0) {
				transition.Set(t);
				myTransition = transition;
			}
		}
    }
}