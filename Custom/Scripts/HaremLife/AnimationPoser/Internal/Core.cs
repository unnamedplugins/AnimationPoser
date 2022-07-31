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
