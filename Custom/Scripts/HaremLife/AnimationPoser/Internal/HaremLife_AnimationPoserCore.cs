/* /////////////////////////////////////////////////////////////////////////////////////////////////
AnimationPoser by HaremLife.
Based on Life#IdlePoser by MacGruber.
State-based idle animation system with anchoring.
https://www.patreon.com/MacGruber_Laboratory
https://github.com/haremlife/AnimationPoser

Licensed under CC BY-SA after EarlyAccess ended. (see https://creativecommons.org/licenses/by-sa/4.0/)

///////////////////////////////////////////////////////////////////////////////////////////////// */

#if !VAM_GT_1_20
	#error AnimationPoser requires VaM 1.20 or newer!
#endif

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
		private static AnimationPoser myPlugin;
		private const int MAX_STATES = 2;
		private static readonly int[] DISTANCE_SAMPLES = new int[] { 0, 0, 0, 11, 20};

		private const float DEFAULT_TRANSITION_DURATION = 0.5f;
		private const float DEFAULT_BLEND_DURATION = 0.2f;
		private const float DEFAULT_EASEIN_DURATION = 0.0f;
		private const float DEFAULT_EASEOUT_DURATION = 0.0f;
		private const float DEFAULT_PROBABILITY = 0.5f;
		private const float DEFAULT_WAIT_DURATION_MIN = 0.0f;
		private const float DEFAULT_WAIT_DURATION_MAX = 0.0f;
		private const float DEFAULT_ANCHOR_BLEND_RATIO = 0.5f;
		private const float DEFAULT_ANCHOR_DAMPING_TIME = 0.2f;

		private static Dictionary<string, Animation> myAnimations = new Dictionary<string, Animation>();
		private static Dictionary<string, Message> myMessages = new Dictionary<string, Message>();
		private static Dictionary<string, Avoid> myAvoids = new Dictionary<string, Avoid>();
		private static Dictionary<string, Role> myRoles = new Dictionary<string, Role>();
		private static Animation myCurrentAnimation;
		private static Layer myCurrentLayer;
		private static State myCurrentState;

		private static bool myPlayMode = false;
		private static bool myPaused = false;
		private static bool myNeedRefresh = false;
		private bool myWasLoading = true;

		private static JSONStorableString mySendMessage;
		private static JSONStorableString myPlaceAvoid;
		private static JSONStorableString myLiftAvoid;
		private static JSONStorableString myLoadAnimation;
		private static JSONStorableBool myPlayPaused;

		public override void Init()
		{
			myPlugin = this;

			myWasLoading = true;

			InitUI();

			// trigger values
			mySendMessage = new JSONStorableString("SendMessage", "", ReceiveMessage);
			mySendMessage.isStorable = mySendMessage.isRestorable = false;
			RegisterString(mySendMessage);

			myPlaceAvoid = new JSONStorableString("PlaceAvoid", "", PlaceAvoid);
			myPlaceAvoid.isStorable = myPlaceAvoid.isRestorable = false;
			RegisterString(myPlaceAvoid);

			myLiftAvoid = new JSONStorableString("LiftAvoid", "", LiftAvoid);
			myLiftAvoid.isStorable = myLiftAvoid.isRestorable = false;
			RegisterString(myLiftAvoid);

			JSONStorableFloat myAnimationSpeed = new JSONStorableFloat("AnimationSpeed", 1.0f, ChangeSpeed, 0.0f, 10.0f, true, true);
			myAnimationSpeed.isStorable = myAnimationSpeed.isRestorable = false;
			RegisterFloat(myAnimationSpeed);

			myPlayPaused = new JSONStorableBool("PlayPause", false, PlayPauseAction);
			myPlayPaused.isStorable = myPlayPaused.isRestorable = false;
			RegisterBool(myPlayPaused);

			myLoadAnimation = new JSONStorableString("LoadAnimation", "", LoadAnimationsAction);
			myLoadAnimation.isStorable = myLoadAnimation.isRestorable = false;
			RegisterString(myLoadAnimation);

			SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRename;
			SimpleTriggerHandler.LoadAssets();
		}

		private void OnDestroy()
		{
			SuperController.singleton.onAtomUIDRenameHandlers -= OnAtomRename;
			OnDestroyUI();

			foreach (var ms in myAnimations)
			{
				ms.Value.Clear();
			}
		}

		private void ChangeSpeed(float f)
		{
			if(myCurrentAnimation!=null)
				myCurrentAnimation.mySpeed = f;
		}

		public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
		{
			JSONClass jc = base.GetJSON(includePhysical, includeAppearance, forceStore);
			if ((includePhysical && includeAppearance) || forceStore) // StoreType.Full
			{
				jc["idlepose"] = SaveAnimations();
				needsStore = true;
			}
			return jc;
		}

		public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true)
		{
			base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);
			if (restorePhysical && restoreAppearance) // StoreType.Full
			{
				if (jc.HasKey("idlepose"))
					LoadAnimations(jc["idlepose"].AsObject);
				myNeedRefresh = true;
			}
		}

		public void ReceiveMessage(String messageString) {
			mySendMessage.valNoCallback = "";
			foreach (var m in myMessages) {
				Message message = m.Value;
				if(message.myMessageString == messageString) {
					foreach (var l in myCurrentAnimation.myLayers) {
						State currentState = l.Value.myCurrentState;
						if(message.mySourceStates.Values.ToList().Contains(currentState)) {
							currentState.myLayer.GoTo(message.myTargetState);
						}
					}
				}
			}
		}

		public void PlaceAvoid(String avoidString) {
			myPlaceAvoid.valNoCallback = "";
			foreach (var av in myAvoids) {
				Avoid avoid = av.Value;
				if(avoid.myName == avoidString) {
					avoid.myIsPlaced = true;
				}
			}
		}

		public void LiftAvoid(String avoidString) {
			myLiftAvoid.valNoCallback = "";
			foreach (var av in myAvoids) {
				Avoid avoid = av.Value;
				if(avoid.myName == avoidString) {
					avoid.myIsPlaced = false;
				}
			}
		}

		private void LoadAnimationsAction(string v)
		{
			myLoadAnimation.valNoCallback = string.Empty;
			JSONClass jc = LoadJSON(BASE_DIRECTORY+"/"+v).AsObject;
			if (jc != null)
				LoadAnimations(jc);
				UIRefreshMenu();
		}

		private void PlayPauseAction(bool b)
		{
			myPlayPaused.val = b;
			myPaused = (myMenuItem != MENU_PLAY || myPlayPaused.val);
		}

		private Animation CreateAnimation(string name)
		{
			Animation a = new Animation(name);
			myAnimations[name] = a;
			return a;
		}

		private Layer CreateLayer(string name)
		{
			return new Layer(name);
		}

		private State CreateState(string name)
		{
			State s = new State(this, name, myCurrentLayer) {
				myWaitDurationMin = myGlobalDefaultWaitDurationMin.val,
				myWaitDurationMax = myGlobalDefaultWaitDurationMax.val,
				myDefaultDuration = myGlobalDefaultTransitionDuration.val,
				myDefaultEaseInDuration = myGlobalDefaultEaseInDuration.val,
				myDefaultEaseOutDuration = myGlobalDefaultEaseOutDuration.val
			};
			myCurrentLayer.CaptureState(s);
			if(myCurrentLayer.myCurrentState != null) {
				setCaptureDefaults(s, myCurrentLayer.myCurrentState);
			}
			myCurrentLayer.myStates[name] = s;
			return s;
		}

		private static void SetAnimation(Animation animation)
		{
			if(myCurrentAnimation != null) {
				foreach(var l in myCurrentAnimation.myLayers) {
					Layer layer = l.Value;
					layer.myStateChain.Clear();
				}
			}
			myCurrentAnimation = animation;
			myMainAnimation.valNoCallback = animation.myName;

			List<string> layers = myCurrentAnimation.myLayers.Keys.ToList();
			layers.Sort();
			myMainLayer.choices = layers;
			foreach(var l in myCurrentAnimation.myLayers) {
				SetLayer(l.Value);
			}
		}

		private static void SetLayer(Layer layer)
		{
			myCurrentLayer = layer;
			myMainLayer.valNoCallback = layer.myName;

			State state = layer.myCurrentState;
			if(state != null)
				myMainState.valNoCallback = state.myName;
		}

		private void setCaptureDefaults(State state, State oldState)
		{
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].setDefaults(state, oldState);
		}

		private void Update()
		{
			bool isLoading = SuperController.singleton.isLoading;
			bool isOrWasLoading = isLoading || myWasLoading;
			myWasLoading = isLoading;
			if (isOrWasLoading){
				return;
			}

			if (myNeedRefresh)
			{
				UIRefreshMenu();
				myNeedRefresh = false;
			}
			DebugUpdateUI();

			foreach (var layer in myCurrentAnimation.myLayers)
				layer.Value.UpdateLayer();
		}

		private void OnAtomRename(string oldid, string newid)
		{
			foreach (var s in myCurrentLayer.myStates)
			{
				State state = s.Value;
				state.EnterBeginTrigger.SyncAtomNames();
				state.EnterEndTrigger.SyncAtomNames();
				state.ExitBeginTrigger.SyncAtomNames();
				state.ExitEndTrigger.SyncAtomNames();

				foreach (var ce in state.myControlEntries)
				{
					ControlEntryAnchored entry = ce.Value;
					if (entry.myAnchorAAtom == oldid)
						entry.myAnchorAAtom = newid;
					if (entry.myAnchorBAtom == oldid)
						entry.myAnchorBAtom = newid;
				}
			}

			myNeedRefresh = true;
		}

		// =======================================================================================
		private class AnimationObject
		{
			public string myName;

			public AnimationObject(string name)
			{
				myName = name;
			}
		}

		private class Animation : AnimationObject
		{
			public Dictionary<string, Layer> myLayers = new Dictionary<string, Layer>();
			public float mySpeed = 1.0f;

			public Animation(string name) : base(name){}

			public void Clear()
			{
				foreach (var l in myLayers)
				{
					Layer layer = l.Value;
					layer.Clear();
				}
			}

			public void InitAnimationLayers() {
				foreach(var l in myLayers) {
					Layer layer = l.Value;
					layer.GoToAnyState(myGlobalDefaultTransitionDuration.val, 0);
				}
			}

			public List<State> findPath(Animation target) {
				List<State> smallestPath = null;
				foreach(var l in myLayers) {
					Layer layer = l.Value;
					State state = layer.myCurrentState;
					if(state == null)
						continue;
					List<State> path = state.findPath(target);
					if(path != null && (smallestPath == null || path.Count < smallestPath.Count))
						smallestPath = path;
				}
				return smallestPath;
			}
		}

		// =======================================================================================
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

			public void CaptureState(State state)
			{
				for (int i=0; i<myControlCaptures.Count; ++i)
					myControlCaptures[i].CaptureEntry(state);
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

		private class Role : AnimationObject
		{
			public Atom myPerson;

			public Role(string name) : base(name) {
			}
		}

		private class BaseTransition
		{
			public State mySourceState;
			public State myTargetState;
			public float myProbability;
		}

		private class IndirectTransition : BaseTransition
		{
			public IndirectTransition(State sourceState, State targetState)
			{
				mySourceState = sourceState;
				myTargetState = targetState;
				myProbability = targetState.myDefaultProbability;
			}

			public IndirectTransition(IndirectTransition t) {
				mySourceState = t.mySourceState;
				myTargetState = t.myTargetState;
				myProbability = t.myProbability;
			}
		}

		private class Timeline {
			public List<Keyframe> myKeyframes = new List<Keyframe>();

			public void AddKeyframe(Keyframe keyframe) {
				int i;
				for(i=0; i<myKeyframes.Count; i++) {
					if(myKeyframes[i].myTime > keyframe.myTime)
						break;
				}
				myKeyframes.Insert(i, keyframe);
				ComputeControlPoints();
			}

			public void RemoveKeyframe(Keyframe keyframe) {
				myKeyframes.Remove(keyframe);
				ComputeControlPoints();
			}

			public virtual void ComputeControlPoints() {
			}

			public virtual void CaptureKeyframe(float time) {
			}

			public virtual void UpdateKeyframe(Keyframe keyframe) {
			}
		}

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

				ControlEntry startEntry = new ControlEntry(myControlCapture);
				startEntry.myTransform = ControlTransform.Identity();
				AddKeyframe(new ControlKeyframe("first", startEntry));

				ControlEntry endEntry = new ControlEntry(myControlCapture);
				endEntry.myTransform = ControlTransform.Identity();
				AddKeyframe(new ControlKeyframe("last", endEntry));

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

				ControlKeyframe k1 = myKeyframes[0] as ControlKeyframe;
				ControlKeyframe k2 = myKeyframes[1] as ControlKeyframe;
				for (int i=1; i<myKeyframes.Count; ++i) {
					if(k2.myTime < t) {
						k1 = myKeyframes[i] as ControlKeyframe;
						k2 = myKeyframes[i+1] as ControlKeyframe;
					} else {
						break;
					}
				}

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
		}

		private class MorphTimeline : Timeline {
			public MorphCapture myMorphCapture;

			public MorphTimeline(MorphCapture morphCapture) {
				myMorphCapture = morphCapture;
			}

			public void SetEndpoints(float startMorphEntry,
									 float endMorphEntry) {
				myKeyframes.Add(new MorphKeyframe("first", startMorphEntry));
				myKeyframes.Add(new MorphKeyframe("last", endMorphEntry));
				ComputeControlPoints();
			}

			public override void ComputeControlPoints() {
				List<float> ts = new List<float>();
				List<float> vs = new List<float>();
				List<Keyframe> keyframes = new List<Keyframe>(myKeyframes.OrderBy(k => k.myTime));
				if(keyframes.Count < 3)
					return;

				for(int i=0; i<keyframes.Count; i++) {
					MorphKeyframe morphKeyframe = keyframes[i] as MorphKeyframe;
					ts.Add(morphKeyframe.myTime);
					vs.Add(morphKeyframe.myMorphEntry);
				}

				List<ControlPoint> controlPoints = AutoComputeControlPoints(vs, ts);

				for(int i=0; i<keyframes.Count; i++) {
					MorphKeyframe morphKeyframe = keyframes[i] as MorphKeyframe;
					morphKeyframe.myControlPointIn = controlPoints[i].In;
					morphKeyframe.myControlPointOut = controlPoints[i].Out;
				}
			}

			public void UpdateCurve(float t)
			{
				if (!myMorphCapture.myApply)
					return;

				MorphKeyframe k1 = myKeyframes[0] as MorphKeyframe;
				MorphKeyframe k2 = myKeyframes[1] as MorphKeyframe;
				for (int i=1; i<myKeyframes.Count; ++i) {
					if(k2.myTime < t) {
						k1 = myKeyframes[i] as MorphKeyframe;
						k2 = myKeyframes[i+1] as MorphKeyframe;
					} else {
						break;
					}
				}

				t = (t-k1.myTime)/(k2.myTime-k1.myTime);
				float c1 = k1.myMorphEntry;
				float c2 = k1.myControlPointOut;
				float c3 = k2.myControlPointIn;
				float c4 = k2.myMorphEntry;

				myMorphCapture.myMorph.morphValue = EvalBezier(t, c1, c2, c3, c4);
			}
		}

		private class Transition : BaseTransition
		{
			public float myTime;
			public float myNoise;
			public bool myFinished;
			public float myEaseInDuration;
			public float myEaseOutDuration;
			public float myDuration;
			public float myDurationNoise = 0.0f;
			public Dictionary<Layer, State> mySyncTargets = new Dictionary<Layer, State>();
			public Dictionary<Role, String> myMessages = new Dictionary<Role, String>();
			public Dictionary<Role, Dictionary<String, bool>> myAvoids = new Dictionary<Role, Dictionary<String, bool>>();
			public Dictionary<ControlCapture, ControlTimeline> myControlTimelines = new Dictionary<ControlCapture, ControlTimeline>();
			public Dictionary<MorphCapture, MorphTimeline> myMorphTimelines = new Dictionary<MorphCapture, MorphTimeline>();

			public Transition(State sourceState, State targetState)
			{
				SetEndpoints(sourceState, targetState);
				myProbability = targetState.myDefaultProbability;
				myEaseInDuration = targetState.myDefaultEaseInDuration;
				myEaseOutDuration = targetState.myDefaultEaseOutDuration;
				myDuration = targetState.myDefaultDuration;
			}

			public Transition(State sourceState, State targetState, float duration)
			{
				SetEndpoints(sourceState, targetState);
				myProbability = targetState.myDefaultProbability;
				myEaseInDuration = 0.0f;
				myEaseOutDuration = 0.0f;
				myDuration = duration;
			}

			public Transition(Transition t) {
				SetEndpoints(t.mySourceState, t.myTargetState);
				myProbability = t.myProbability;
				myEaseInDuration = t.myEaseInDuration;
				myEaseOutDuration = t.myEaseOutDuration;
				myDuration = t.myDuration;
				myDurationNoise = t.myDurationNoise;
				mySyncTargets = t.mySyncTargets;
				myMessages = t.myMessages;
				myAvoids = t.myAvoids;
			}

			public void SetEndpoints(State sourceState, State targetState) {
				mySourceState = sourceState;
				myTargetState = targetState;

				if(sourceState.myAnimation() != targetState.myAnimation())
					return;

				foreach(ControlCapture controlCapture in sourceState.myLayer.myControlCaptures) {
					if(!myControlTimelines.Keys.Contains(controlCapture))
						myControlTimelines[controlCapture] = new ControlTimeline(controlCapture);
					ControlTimeline timeline = myControlTimelines[controlCapture];
					timeline.SetEndpoints(sourceState.myControlEntries[controlCapture],
										  targetState.myControlEntries[controlCapture]);
				}

				foreach(MorphCapture morphCapture in sourceState.myLayer.myMorphCaptures) {
					if(!myMorphTimelines.Keys.Contains(morphCapture))
						myMorphTimelines[morphCapture] = new MorphTimeline(morphCapture);
					MorphTimeline timeline = myMorphTimelines[morphCapture];
					timeline.SetEndpoints(sourceState.myMorphEntries[morphCapture],
										  targetState.myMorphEntries[morphCapture]);
				}
			}

			public void StartTransition(List<TriggerActionDiscrete> triggerActionsNeedingUpdate) {
				SendStartTransitionTriggers(triggerActionsNeedingUpdate);

				foreach(var sc in mySyncTargets) {
					Layer syncLayer = sc.Key;
					State syncState = sc.Value;
					syncLayer.GoTo(syncState);
				}

				SendMessages();
				SendAvoids();
			}

			public void EndTransition(List<TriggerActionDiscrete> triggerActionsNeedingUpdate) {
				SendEndTransitionTriggers(triggerActionsNeedingUpdate);

				foreach (ControlTimeline timeline in myControlTimelines.Values.ToList())
					timeline.UpdateControllerStates();
			}

			public void SendStartTransitionTriggers(List<TriggerActionDiscrete> triggerActionsNeedingUpdate) {
				if (mySourceState.ExitBeginTrigger != null)
					mySourceState.ExitBeginTrigger.Trigger(triggerActionsNeedingUpdate);
				if (myTargetState.EnterBeginTrigger != null)
					myTargetState.EnterBeginTrigger.Trigger(triggerActionsNeedingUpdate);
			}

			public void SendEndTransitionTriggers(List<TriggerActionDiscrete> triggerActionsNeedingUpdate) {
				if (mySourceState.ExitEndTrigger != null)
					mySourceState.ExitEndTrigger.Trigger(triggerActionsNeedingUpdate);
				if (myTargetState.EnterEndTrigger != null)
					myTargetState.EnterEndTrigger.Trigger(triggerActionsNeedingUpdate);
			}

			public void SendMessages() {
				foreach(var m in myMessages) {
					Role role = m.Key;
					String message = m.Value;
					Atom person = role.myPerson;
					if (person == null) continue;
					var storableId = person.GetStorableIDs().FirstOrDefault(id => id.EndsWith("HaremLife.AnimationPoser"));
					if (storableId == null) continue;
					MVRScript storable = person.GetStorableByID(storableId) as MVRScript;
					if (storable == null) continue;
					// if (ReferenceEquals(storable, _plugin)) continue;
					if (!storable.enabled) continue;
					storable.SendMessage(nameof(AnimationPoser.ReceiveMessage), message);
				}
			}

			public void SendAvoids() {
				foreach(var r in myAvoids) {
					Role role = r.Key;
					foreach(var a in r.Value) {
						String avoidString = a.Key;
						Atom person = role.myPerson;
						if (person == null) continue;
						var storableId = person.GetStorableIDs().FirstOrDefault(id => id.EndsWith("HaremLife.AnimationPoser"));
						if (storableId == null) continue;
						MVRScript storable = person.GetStorableByID(storableId) as MVRScript;
						if (storable == null) continue;
						// if (ReferenceEquals(storable, _plugin)) continue;
						if (!storable.enabled) continue;
						if(a.Value)
							storable.SendMessage(nameof(AnimationPoser.PlaceAvoid), avoidString);
						else
							storable.SendMessage(nameof(AnimationPoser.LiftAvoid), avoidString);
					}
				}
			}

			public void Set(float v=0) {
				myFinished = false;
				myTime = v;
				myNoise = UnityEngine.Random.Range(-myDurationNoise, myDurationNoise);
				UpdateCurve();
			}

			public void Advance(float deltaTime) {
				myTime += deltaTime / (myDuration+myNoise);
				UpdateCurve();
			}

			public void UpdateCurve() {
				float s = Smooth(myEaseOutDuration, myEaseInDuration, 1, myTime);
				foreach(ControlTimeline timeline in myControlTimelines.Values.ToList()) {
					timeline.myStartEntry.UpdateTransform();
					timeline.myEndEntry.UpdateTransform();
					timeline.UpdateCurve(s);
				}
				foreach(MorphTimeline timeline in myMorphTimelines.Values.ToList())
					timeline.UpdateCurve(s);
				if(s>=1)
					myFinished = true;
			}
		}

		private class Message : AnimationObject
		{
			public State myTargetState;
			public String myMessageString;
			public Dictionary<string, State> mySourceStates = new Dictionary<string, State>();

			public Message(string name) : base(name) {
				myName = name;
			}
		}

		private class Avoid : AnimationObject
		{
			public Dictionary<string, State> myAvoidStates = new Dictionary<string, State>();
			public bool myIsPlaced = false;

			public Avoid(string name) : base(name) {
			}
		}

		private class State : AnimationObject
		{
			public Layer myLayer;
			public float myWaitDurationMin;
			public float myWaitDurationMax;
			public float myDefaultDuration;
			public float myDefaultEaseInDuration;
			public float myDefaultEaseOutDuration;
			public float myDefaultProbability = DEFAULT_PROBABILITY;
			public bool myIsRootState = false;
			public uint myDebugIndex = 0;
			public Dictionary<ControlCapture, ControlEntryAnchored> myControlEntries = new Dictionary<ControlCapture, ControlEntryAnchored>();
			public Dictionary<MorphCapture, float> myMorphEntries = new Dictionary<MorphCapture, float>();
			public List<BaseTransition> myTransitions = new List<BaseTransition>();
			public EventTrigger EnterBeginTrigger;
			public EventTrigger EnterEndTrigger;
			public EventTrigger ExitBeginTrigger;
			public EventTrigger ExitEndTrigger;

			public State(MVRScript script, string name, Layer layer) : base(name)
			{
				myLayer = layer;
				EnterBeginTrigger = new EventTrigger(script, "OnEnterBegin", name);
				EnterEndTrigger = new EventTrigger(script, "OnEnterEnd", name);
				ExitBeginTrigger = new EventTrigger(script, "OnExitBegin", name);
				ExitEndTrigger = new EventTrigger(script, "OnExitEnd", name);
			}

			public State(string name, Layer layer) : base(name)
			{
				myLayer = layer;
			}

			public State(string name, State source) : base(name)
			{
				myLayer = myCurrentLayer;
				myWaitDurationMin = source.myWaitDurationMin;
				myWaitDurationMax = source.myWaitDurationMax;
				myDefaultDuration = source.myDefaultDuration;
				myDefaultEaseInDuration = source.myDefaultEaseInDuration;
				myDefaultEaseOutDuration = source.myDefaultEaseOutDuration;
				myDefaultProbability = source.myDefaultProbability;
				myIsRootState = source.myIsRootState;
				EnterBeginTrigger = new EventTrigger(source.EnterBeginTrigger);
				EnterEndTrigger = new EventTrigger(source.EnterEndTrigger);
				ExitBeginTrigger = new EventTrigger(source.ExitBeginTrigger);
				ExitEndTrigger = new EventTrigger(source.ExitEndTrigger);
			}

			public Animation myAnimation() {
				return myLayer.myAnimation;
			}

			public List<State> getDirectlyReachableStates() {
				List<State> states = new List<State>();
				for(int i=0; i<myTransitions.Count; i++) {
					if(myTransitions[i] is Transition) {
						states.Add(myTransitions[i].myTargetState);
					}
				}
				return states;
			}

			public List<State> getReachableStates() {
				List<State> states = new List<State>();
				for(int i=0; i<myTransitions.Count; i++)
					states.Add(myTransitions[i].myTargetState);
				return states;
			}

			public bool isReachable(State state) {
				List<State> states = this.getReachableStates();
				return states.Contains(state);
			}

			public List<State> filterAvoided(List<State> states) {
				List<State> notAvoided = new List<State>();
				foreach(State state in states) {
					bool avoided = false;
					foreach(var a in myAvoids) {
						Avoid avoid = a.Value;
						if(!avoid.myIsPlaced)
							continue;
						if(avoid.myAvoidStates.Values.Contains(state))
							avoided = true;
					}
					if(!avoided)
						notAvoided.Add(state);
				}
				return notAvoided;
			}

			public Dictionary<State, List<State>> getPaths() {
				Dictionary<State, List<State>> paths = new Dictionary<State, List<State>>();

				paths[this] = new List<State>();
				paths[this].Add(this);

				while(true) {
					bool changed = false;

					List<State> keys = paths.Keys.ToList();
					for(int i=0; i<keys.Count(); i++) {
						State thisState = keys[i];

					List<State> states = filterAvoided(thisState.getDirectlyReachableStates());
					for(int j=0; j<states.Count(); j++) {
							State state = states[j];

							if(!paths.ContainsKey(state)) {
								paths[state] = new List<State>(paths[thisState]);
								paths[state].Add(state);
								changed = true;
							}
						}
					}
					if(!changed) {
						break;
					}
				}

				return paths;
			}

			public List<State> findPath(State target) {
				Dictionary<State, List<State>> paths = getPaths();

				if(paths.ContainsKey(target)) {
					List<State> path = paths[target];
					return path;
				} else {
					return null;
				}
			}

			public List<State> findPath(Animation target) {
				Dictionary<State, List<State>> paths = getPaths();

				List<State> smallestPath = null;
				foreach(var s in paths) {
					State state = s.Key;
					if(state.myAnimation() == target) {
						List<State> path = paths[state];
						if(smallestPath == null || path.Count < smallestPath.Count)
							smallestPath = path;
					}
				}

				return smallestPath;
			}

			public State sortNextState() {
				List<State> states = filterAvoided(getReachableStates());
				if(states.Count == 0)
					return null;

				float sum = 0.0f;
				for (int i=0; i<myTransitions.Count; ++i)
					sum += myTransitions[i].myProbability;
				if (sum == 0.0f)
				{
					return null;
				}
				else
				{
					float threshold = UnityEngine.Random.Range(0.0f, sum);
					sum = 0.0f;
					int i;
					for (i=0; i<myTransitions.Count-1; ++i)
					{
						sum += myTransitions[i].myProbability;
						if (threshold <= sum)
							break;
					}
					return states[i];
				}

			}

			public BaseTransition getIncomingTransition(State state) {
				for(int i=0; i<myTransitions.Count; i++)
					if(myTransitions[i].myTargetState == state)
						return myTransitions[i];
				return null;
			}
			public void removeTransition(State state) {
				for(int i=0; i<myTransitions.Count; i++)
					if(myTransitions[i].myTargetState == state)
						myTransitions.RemoveAt(i);
			}

			public void AssignOutTriggers(State other)
			{
				ExitBeginTrigger = other?.ExitBeginTrigger;
				ExitEndTrigger = other?.ExitEndTrigger;
			}
		}

		private class Keyframe
		{
			public float myTime;
			public bool myIsFirst = false;
			public bool myIsLast = false;
		}

		private class ControlKeyframe : Keyframe
		{
			public ControlEntry myControlEntry;
			public ControlEntry myControlPointIn;
			public ControlEntry myControlPointOut;

			public ControlKeyframe(string firstOrLast, ControlEntry entry)
			{
				if(String.Equals(firstOrLast, "first")) {
					myTime = 0;
					myIsFirst = true;
				}
				if(String.Equals(firstOrLast, "last")) {
					myTime = 1;
					myIsLast = true;
				}
				myControlEntry = entry;
			}

			public ControlKeyframe(float time, ControlEntry entry)
			{
				myTime = time;
				myControlEntry = entry;
			}
		}

		private class MorphKeyframe : Keyframe
		{
			public float myMorphEntry;
			public float myControlPointIn;
			public float myControlPointOut;

			public MorphKeyframe(string firstOrLast, float entry)
			{
				if(String.Equals(firstOrLast, "first")) {
					myTime = 0;
					myIsFirst = true;
				}
				if(String.Equals(firstOrLast, "last")) {
					myTime = 1;
					myIsLast = true;
				}
				myMorphEntry = entry;
			}

			public MorphKeyframe(float time, float entry)
			{
				myTime = time;
				myMorphEntry = entry;
			}
		}

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
		
		private class ControlEntry {
			public ControlTransform myTransform;
			public ControlCapture myControlCapture;

			public ControlEntry(ControlCapture controlCapture)
			{
				myControlCapture = controlCapture;
			}
		}

		private class ControlEntryAnchored : ControlEntry
		{
			public const int ANCHORMODE_WORLD = 0;
			public const int ANCHORMODE_SINGLE = 1;
			public const int ANCHORMODE_BLEND = 2;

			public const int ANCHORTYPE_OBJECT = 0;
			public const int ANCHORTYPE_ROLE = 1;

			public FreeControllerV3.PositionState myPositionState;
			public FreeControllerV3.RotationState myRotationState;
			public ControlTransform myAnchorOffset;
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

			public void UpdateTransform()
			{
				if (myAnchorMode == ANCHORMODE_WORLD)
				{
					myTransform = myAnchorOffset;
				}
				else
				{
					ControlTransform anchor = GetVirtualAnchorTransform();
					if(anchor == null)
						return;

					if (myDampingTime >= 0.001f)
					{
						float t = Mathf.Clamp01(Time.deltaTime / myDampingTime);
						myTransform = new ControlTransform(myTransform, anchor.Compose(myAnchorOffset), t);
					}
					else
					{
						myTransform = anchor.Compose(myAnchorOffset);
					}
				}
			}

			public void Capture(ControlTransform transform,
									FreeControllerV3.PositionState positionState,
									FreeControllerV3.RotationState rotationState)
			{
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

		private class MorphCapture
		{
			public string mySID;
			public DAZMorph myMorph;
			public DAZCharacterSelector.Gender myGender;
			public bool myApply = true;

			// used when adding a capture
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector.Gender gender, DAZMorph morph)
			{
				myMorph = morph;
				myGender = gender;
				mySID = plugin.GenerateMorphsSID(gender == DAZCharacterSelector.Gender.Female);
			}

			// legacy handling of old qualifiedName where the order was reversed
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector geometry, string oldQualifiedName)
			{
				bool isFemale = oldQualifiedName.StartsWith("Female#");
				if (!isFemale && !oldQualifiedName.StartsWith("Male#")){
					return;
				}
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				string morphUID = oldQualifiedName.Substring(isFemale ? 7 : 5);
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;

				mySID = plugin.GenerateMorphsSID(isFemale);
			}

			// legacy handling before there were ShortIDs
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector geometry, string morphUID, bool isFemale)
			{
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;

				mySID = plugin.GenerateMorphsSID(isFemale);
			}

			// used when loading from JSON
			public MorphCapture(DAZCharacterSelector geometry, string morphUID, string morphSID)
			{
				bool isFemale = morphSID.Length > 0 && morphSID[0] == 'F';
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;
				mySID = morphSID;
			}

			public void CaptureEntry(State state)
			{
				state.myMorphEntries[this] = myMorph.morphValue;
			}

			public bool IsValid()
			{
				return myMorph != null && mySID != null;
			}
		}

		private string GenerateMorphsSID(bool isFemale)
		{
			string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			char[] randomChars = new char[6];
			randomChars[0] = isFemale ? 'F' : 'M';
			randomChars[1] = '-';
			for (int a=0; a<10; ++a)
			{
				// find unused shortID
				for (int i=2; i<randomChars.Length; ++i)
					randomChars[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
				string sid = new string(randomChars);
				if (myCurrentLayer.myMorphCaptures.Find(x => x.mySID == sid) == null){
					return sid;
				}
			}

			return null; // you are very lucky, you should play lottery!
		}
	}
}
