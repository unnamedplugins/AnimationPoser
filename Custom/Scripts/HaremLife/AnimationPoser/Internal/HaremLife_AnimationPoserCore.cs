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
using System.Collections.Generic;
using SimpleJSON;

namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private const int MAX_STATES = 4;
		private static readonly int[] DISTANCE_SAMPLES = new int[] { 0, 0, 0, 11, 20};

		private const int STATETYPE_REGULARSTATE = 0;
		private const int STATETYPE_CONTROLPOINT = 1;
		private const int STATETYPE_INTERMEDIATE = 2;
		private const int NUM_STATETYPES = 3;

		private const float DEFAULT_TRANSITION_DURATION = 0.5f;
		private const float DEFAULT_BLEND_DURATION = 0.2f;
		private const float DEFAULT_EASEIN_DURATION = 1.0f;
		private const float DEFAULT_EASEOUT_DURATION = 1.0f;
		private const float DEFAULT_PROBABILITY = 0.5f;
		private const float DEFAULT_WAIT_DURATION_MIN = 2.0f;
		private const float DEFAULT_WAIT_DURATION_MAX = 4.0f;
		private const float DEFAULT_ANCHOR_BLEND_RATIO = 0.5f;
		private const float DEFAULT_ANCHOR_DAMPING_TIME = 0.2f;

		private Dictionary<string, Animation> myAnimations = new Dictionary<string, Animation>();
		private List<ControlCapture> myControlCaptures = new List<ControlCapture>();
		private List<MorphCapture> myMorphCaptures = new List<MorphCapture>();
		private List<State> myTransition = new List<State>(8);
		private List<State> myCurrentTransition = new List<State>(MAX_STATES);
		private List<TriggerActionDiscrete> myTriggerActionsNeedingUpdate = new List<TriggerActionDiscrete>();
		private static Animation myCurrentAnimation;
		private static Layer myCurrentLayer;
		private static State myCurrentState;
		private State myNextState;
		private State myBlendState = State.CreateBlendState();

		private float myDuration = 1.0f;
		private float myClock = 0.0f;
		private static uint myStateMask = 0;
		private static bool myStateMaskChanged = false;
		private static bool myNoValidTransition = false;
		private static bool myPlayMode = false;
		private static bool myPaused = false;
		private bool myNeedRefresh = false;
		private bool myWasLoading = true;

		private JSONStorableString mySwitchAnimation;
		private JSONStorableString mySwitchLayer;
		private JSONStorableString mySwitchState;
		private static JSONStorableString mySetStateMask;
		private static JSONStorableString myPartialStateMask;

		public override void Init()
		{
			// SuperController.LogMessage("****** Base.Init ******");
			myWasLoading = true;
			myClock = 0.0f;

			ControlCapture lHand = new ControlCapture(this, "lHandControl");
			if (lHand.IsValid())
				myControlCaptures.Add(lHand);
			ControlCapture rHand = new ControlCapture(this, "rHandControl");
			if (rHand.IsValid())
				myControlCaptures.Add(rHand);
			ControlCapture control = new ControlCapture(this, "control");
			if (myControlCaptures.Count == 0 && control.IsValid())
				myControlCaptures.Add(control);


			InitUI();

			// trigger values
			mySwitchAnimation = new JSONStorableString("SwitchAnimation", "", SwitchAnimationAction);
			mySwitchAnimation.isStorable = mySwitchAnimation.isRestorable = false;
			RegisterString(mySwitchAnimation);

			mySwitchLayer = new JSONStorableString("SwitchLayer", "", SwitchLayerAction);
			mySwitchLayer.isStorable = mySwitchLayer.isRestorable = false;
			RegisterString(mySwitchLayer);

			mySwitchState = new JSONStorableString("SwitchState", "", SwitchStateAction);
			mySwitchState.isStorable = mySwitchState.isRestorable = false;
			RegisterString(mySwitchState);

			mySetStateMask = new JSONStorableString("SetStateMask", "", SetStateMaskAction);
			mySetStateMask.isStorable = mySetStateMask.isRestorable = false;
			RegisterString(mySetStateMask);

			myPartialStateMask = new JSONStorableString("PartialStateMask", "", PartialStateMaskAction);
			myPartialStateMask.isStorable = myPartialStateMask.isRestorable = false;
			RegisterString(myPartialStateMask);

			Utils.SetupAction(this, "TriggerSync", TriggerSyncAction);

			SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRename;
			SimpleTriggerHandler.LoadAssets();
			// SuperController.LogMessage("****** Base.Init End ******");
		}

		private void OnDestroy()
		{
			// SuperController.LogMessage("****** Base.OnDestroy ******");
			SuperController.singleton.onAtomUIDRenameHandlers -= OnAtomRename;
			OnDestroyUI();

			foreach (var ms in myAnimations)
			{
				ms.Value.Clear();
			}
			// SuperController.LogMessage("****** Base.OnDestroy End ******");
		}

		private Animation CreateAnimation(string name)
		{
			// SuperController.LogMessage("****** Base.CreateAnimation ******");
			Animation a = new Animation(name) {
			};
			myAnimations[name] = a;
			// SuperController.LogMessage("****** Base.CreateAnimation End ******");
			return a;
		}

		private Layer CreateLayer(string name)
		{
			// SuperController.LogMessage("****** Base.CreateLayer ******");
			Layer l = new Layer(name) {
			};
			myCurrentAnimation.myLayers[name] = l;
			// SuperController.LogMessage("****** Base.CreateLayer End ******");
			return l;
		}

		private State CreateState(string name)
		{
			// SuperController.LogMessage("****** Base.CreateState ******");
			State s = new State(this, name) {
				myWaitDurationMin = DEFAULT_WAIT_DURATION_MIN,
				myWaitDurationMax = DEFAULT_WAIT_DURATION_MAX,
				myTransitionDuration = DEFAULT_TRANSITION_DURATION,
				myStateType = STATETYPE_REGULARSTATE
			};
			// SuperController.LogMessage("In create state");
			CaptureState(s);
			// SuperController.LogMessage("Captured state");
			if(myCurrentLayer.myCurrentState != null) {
				setCaptureDefaults(s, myCurrentLayer.myCurrentState);
			}
			myCurrentLayer.myStates[name] = s;
			// SuperController.LogMessage("added state to layer");
			// SuperController.LogMessage("****** Base.CreateState End ******");
			return s;
		}

		private void SetAnimation(Animation animation)
		{
			// SuperController.LogMessage("****** Base.SetAnimation ******");
			myCurrentAnimation = animation;
			// SuperController.LogMessage("****** Base.SetAnimation End ******");
		}

		private void SetLayer(Layer layer)
		{
			// SuperController.LogMessage("****** Base.SetLayer ******");
			myCurrentLayer = layer;
			// SuperController.LogMessage("****** Base.SetLayer End ******");
		}

		private void CaptureState(State state)
		{
			// SuperController.LogMessage("****** Base.CaptureState ******");
			// SuperController.LogMessage("Base.CaptureState: Capturing " + state.myName);
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i){
			  // SuperController.LogMessage("Base.CaptureState: Capturing " + myCurrentLayer.myControlCaptures[i].myName);
				myCurrentLayer.myControlCaptures[i].CaptureEntry(state);
			}
			for (int i=0; i<myCurrentLayer.myMorphCaptures.Count; ++i) {
			  // SuperController.LogMessage("Capturing " + myCurrentLayer.myMorphCaptures[i].mySID);
				// SuperController.LogMessage("Base.CaptureState: Capturing " + myCurrentLayer.myMorphCaptures[i].myMorph);
				myCurrentLayer.myMorphCaptures[i].CaptureEntry(state);
			}
			// SuperController.LogMessage("****** Base.CaptureState End ******");
		}

		private void setCaptureDefaults(State state, State oldState)
		{
			// SuperController.LogMessage("****** Base.setCaptureDefaults ******");
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].setDefaults(state, oldState);
			// SuperController.LogMessage("****** Base.setCaptureDefaults End ******");
		}

		private void SwitchAnimationAction(string v)
		{
			// SuperController.LogMessage("****** Base.SwitchAnimationAction ******");
			bool initPlayPaused = myPlayPaused.val;
			myPlayPaused.val = true;
			mySwitchAnimation.valNoCallback = string.Empty;

			Animation animation;
			myAnimations.TryGetValue(v, out animation);
			// SuperController.LogMessage("Base.SwitchAnimationAction: setting animation " + animation.myName);
			SetAnimation(animation);

			List<string> layers = myCurrentAnimation.myLayers.Keys.ToList();
			layers.Sort();
			if(layers.Count > 0) {
				Layer layer;
				foreach (var layerKey in layers) {
					myCurrentAnimation.myLayers.TryGetValue(layerKey, out layer);
					// SuperController.LogMessage("Base.SwitchAnimationAction: setting layer " + layer.myName);
					SetLayer(layer);
					List<string> states = layer.myStates.Keys.ToList();
					states.Sort();
					if(states.Count > 0) {
						State state;
						layer.myStates.TryGetValue(states[0], out state);
						// SuperController.LogMessage("Base.SwitchAnimationAction: setting state " + state.myName);
						// layer.SetBlendTransition(state);
						layer.SetState(state);
					}
				}
			}
			myPlayPaused.val = initPlayPaused;
			// SuperController.LogMessage("****** Base.SwitchAnimationAction End ******");
		}

		private void SwitchLayerAction(string v)
		{
			// SuperController.LogMessage("****** Base.SwitchLayerAction ******");
			mySwitchLayer.valNoCallback = string.Empty;

			Layer layer;

			myCurrentAnimation.myLayers.TryGetValue(v, out layer);
			SetLayer(layer);

			List<string> states = layer.myStates.Keys.ToList();
			states.Sort();
			if(states.Count > 0) {
				State state;
				layer.myStates.TryGetValue(states[0], out state);
				layer.SetBlendTransition(state);
			}
			// SuperController.LogMessage("****** Base.SwitchLayerAction End ******");
		}

		private void SwitchStateAction(string v)
		{
			// SuperController.LogMessage("****** Base.SwitchStateAction ******");
			mySwitchState.valNoCallback = string.Empty;

			State state;
			if (myCurrentLayer.myStates.TryGetValue(v, out state))
				myCurrentLayer.SetBlendTransition(state);
			else
				SuperController.LogError("AnimationPoser: Can't switch to unknown state '"+v+"'!");
			// SuperController.LogMessage("****** Base.SwitchStateAction End ******");
		}

		private void SetStateMaskAction(string v)
		{
			// SuperController.LogMessage("****** Base.SetStateMaskAction ******");
			mySetStateMask.valNoCallback = string.Empty;

			myStateMask = 0;
			bool invert = false;
			bool error = false;
			if (v.ToLower() != "clear")
			{
				for (int i=0; i<v.Length; ++i)
				{
					char c = v[i];
					if (i == 0 && c == '!')
						invert = true;
					else if (c >= 'A' && c <= 'L')
						myStateMask |= 1u << (c - 'A' + 1);
					else if (c >= 'a' && c <= 'l')
						myStateMask |= 1u << (c - 'a' + 1);
					else if (c == 'N' || c == 'n')
						myStateMask |= 1u << 0;
					else
						error = true;
				}
			}

			if (error)
			{
				SuperController.LogError("AnimationPoser: SetStateMask set to invalid data: '"+v+"'");
				return;
			}

			if (myStateMask != 0 && invert)
				myStateMask = ~myStateMask;

			myStateMaskChanged = true;
			TryApplyStateMaskChange();
			// SuperController.LogMessage("****** Base.SetStateMaskAction End ******");
		}

		private void PartialStateMaskAction(string v)
		{
			// SuperController.LogMessage("****** Base.PartialStateMaskAction ******");
			myPartialStateMask.valNoCallback = string.Empty;

			uint stateMask = 0;
			bool invert = false;
			bool error = false;
			string[] masks = v.Split(new char[]{' ',';','|'}, 32, StringSplitOptions.RemoveEmptyEntries);
			for (int m=0; m<masks.Length; ++m)
			{
				stateMask = 0;
				invert = false;
				for (int i=0; i<masks[m].Length; ++i)
				{
					char c = masks[m][i];
					if (i == 0 && c == '!')
						invert = true;
					else if (c >= 'A' && c <= 'L')
						stateMask |= 1u << (c - 'A' + 1);
					else if (c >= 'a' && c <= 'l')
						stateMask |= 1u << (c - 'a' + 1);
					else if (c == 'N' || c == 'n')
						stateMask |= 1u << 0;
					else
						error = true;
				}

				if (error)
				{
					SuperController.LogError("AnimationPoser: PartialStateMask set to invalid data: '"+v+"'");
					return;
				}

				if (stateMask == 0)
					continue;

				if (invert)
					myStateMask = myStateMask & ~stateMask;
				else
					myStateMask = myStateMask | stateMask;
			}

			myStateMaskChanged = true;
			TryApplyStateMaskChange();
			// SuperController.LogMessage("****** Base.PartialStateMaskAction End ******");
		}

		private void TryApplyStateMaskChange()
		{
			// SuperController.LogMessage("****** Base.TryApplyStateMaskChange ******");
			if (myCurrentState == null || myNextState != null || !myCurrentState.IsRegularState){
				// SuperController.LogMessage("****** Base.TryApplyStateMaskChange End ******");
				return;
			}

			if (myClock >= myDuration && !myCurrentState.myWaitForSync && !myPaused)
			{
				myStateMaskChanged = false;
				myCurrentLayer.SetRandomTransition();
			}
			else if (myStateMask != 0 && (myCurrentState.StateMask & myStateMask) == 0)
			{
				myStateMaskChanged = false;
				myCurrentLayer.SetRandomTransition();
			}
			// SuperController.LogMessage("****** Base.TryApplyStateMaskChange End ******");
		}

		private void TriggerSyncAction()
		{
			// SuperController.LogMessage("****** Base.TriggerSyncAction ******");
			foreach (var layer in myCurrentAnimation.myLayers)
				layer.Value.TriggerSyncAction();
			// SuperController.LogMessage("****** Base.TriggerSyncAction End ******");
		}

		private void Update()
		{
			// SuperController.LogMessage("****** Base.Update ******");
			bool isLoading = SuperController.singleton.isLoading;
			bool isOrWasLoading = isLoading || myWasLoading;
			myWasLoading = isLoading;
			if (isOrWasLoading){
				// SuperController.LogMessage("****** Base.Update End 1 ******");
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
			// SuperController.LogMessage("****** Base.OnAtomRename ******");
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
			// SuperController.LogMessage("****** Base.OnAtomRename End ******");
		}

		public override JSONClass GetJSON(bool includePhysical = true, bool includeAppearance = true, bool forceStore = false)
		{
			// SuperController.LogMessage("****** Base.GetJSON ******");
			JSONClass jc = base.GetJSON(includePhysical, includeAppearance, forceStore);
			if ((includePhysical && includeAppearance) || forceStore) // StoreType.Full
			{
				jc["idlepose"] = SaveAnimations();
				needsStore = true;
			}
			// SuperController.LogMessage("****** Base.GetJSON End ******");
			return jc;
		}

		public override void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, bool setMissingToDefault = true)
		{
			// SuperController.LogMessage("****** Base.LateRestoreFromJSON ******");
			base.LateRestoreFromJSON(jc, restorePhysical, restoreAppearance, setMissingToDefault);
			if (restorePhysical && restoreAppearance) // StoreType.Full
			{
				if (jc.HasKey("idlepose"))
					LoadAnimations(jc["idlepose"].AsObject);
				myNeedRefresh = true;
			}
			// SuperController.LogMessage("****** Base.LateRestoreFromJSON End ******");
		}

		private JSONClass SaveAnimations()
		{
			// SuperController.LogMessage("****** Base.SaveAnimations ******");
			JSONClass jc = new JSONClass();

			// save info
			JSONClass info = new JSONClass();
			info["Format"] = "MacGruber.Life.IdlePoser";
			info["Version"].AsInt = 9;
			string creatorName = UserPreferences.singleton.creatorName;
			if (string.IsNullOrEmpty(creatorName))
				creatorName = "Unknown";
			info["Author"] = creatorName;
			jc["Info"] = info;

			// save settings
			if (myCurrentState != null)
				jc["InitialState"] = myCurrentState.myName;
			jc["Paused"].AsBool = myPlayPaused.val;
			jc["DefaultToWorldAnchor"].AsBool = myOptionsDefaultToWorldAnchor.val;

			JSONArray anims = new JSONArray();

			foreach(var an in myAnimations)
			{
				Animation animation = an.Value;
				JSONClass anim = new JSONClass();
				anim["Name"] = animation.myName;
				JSONArray llist = new JSONArray();
				foreach(var l in animation.myLayers){
					llist.Add("", SaveLayer(l.Value));
				}
				anim["Layers"] = llist;
				anims.Add("", anim);
			}

			jc["Animations"] = anims;

			// SuperController.LogMessage("****** Base.SaveAnimations End ******");
			return jc;
		}

		private void LoadAnimations(JSONClass jc)
		{
			// SuperController.LogMessage("****** Base.LoadAnimations ******");
			// load info
			int version = jc["Info"].AsObject["Version"].AsInt;

			// load captures
			JSONArray anims = jc["Animations"].AsArray;
			for (int l=0; l<anims.Count; ++l)
			{
				JSONClass anim = anims[l].AsObject;
				myCurrentAnimation = CreateAnimation(anim["Name"]);
				JSONArray layers = anim["Layers"].AsArray;
				for(int m=0; m<layers.Count; m++)
				{
					JSONClass layer = layers[m].AsObject;
					LoadLayer(layer, false);
				}
			}

			// load settings
			myPlayPaused.valNoCallback = jc.HasKey("Paused") && jc["Paused"].AsBool;
			myPlayPaused.setCallbackFunction(myPlayPaused.val);

			myOptionsDefaultToWorldAnchor.val = jc.HasKey("DefaultToWorldAnchor") && jc["DefaultToWorldAnchor"].AsBool;
			// SuperController.LogMessage("****** Base.LoadAnimations End ******");
		}

		private JSONClass SaveLayer(Layer layerToSave)
		{
			JSONClass jc = new JSONClass();

			// save info
			JSONClass info = new JSONClass();
			info["Format"] = "MacGruber.IdlePoser";
			info["Version"].AsInt = 9;
			string creatorName = UserPreferences.singleton.creatorName;
			if (string.IsNullOrEmpty(creatorName))
				creatorName = "Unknown";
			info["Author"] = creatorName;
			jc["Info"] = info;

			// save settings
			if (myCurrentState != null)
				jc["InitialState"] = myCurrentState.myName;
			jc["Paused"].AsBool = myPlayPaused.val;
			jc["StateMask"].AsInt = (int)myStateMask;
			jc["DefaultToWorldAnchor"].AsBool = myOptionsDefaultToWorldAnchor.val;

			JSONClass layer = new JSONClass();
			layer["Name"] = layerToSave.myName;

			// save captures
			if (layerToSave.myControlCaptures.Count > 0)
			{
				JSONArray cclist = new JSONArray();
				for (int i=0; i<layerToSave.myControlCaptures.Count; ++i)
				{
					ControlCapture cc = layerToSave.myControlCaptures[i];
					JSONClass ccclass = new JSONClass();
					ccclass["Name"] = cc.myName;
					ccclass["ApplyPos"].AsBool = cc.myApplyPosition;
					ccclass["ApplyRot"].AsBool = cc.myApplyRotation;
					cclist.Add("", ccclass);
				}
				layer["ControlCaptures"] = cclist;
			}
			if (layerToSave.myMorphCaptures.Count > 0)
			{
				JSONArray mclist = new JSONArray();
				for (int i=0; i<layerToSave.myMorphCaptures.Count; ++i)
				{
					MorphCapture mc = layerToSave.myMorphCaptures[i];
					JSONClass mcclass = new JSONClass();
					mcclass["UID"] = mc.myMorph.uid;
					mcclass["SID"] = mc.mySID;
					mcclass["Apply"].AsBool = mc.myApply;
					mclist.Add("", mcclass);
				}
				layer["MorphCaptures"] = mclist;
			}

			// save states
			JSONArray slist = new JSONArray();
			foreach (var s in layerToSave.myStates)
			{
				State state = s.Value;
				JSONClass st = new JSONClass();
				st["Name"] = state.myName;
				st["WaitInfiniteDuration"].AsBool = state.myWaitInfiniteDuration;
				st["WaitForSync"].AsBool = state.myWaitForSync;
				st["AllowInGroupT"].AsBool = state.myAllowInGroupTransition;
				st["WaitDurationMin"].AsFloat = state.myWaitDurationMin;
				st["WaitDurationMax"].AsFloat = state.myWaitDurationMax;
				st["TransitionDuration"].AsFloat = state.myTransitionDuration;
				st["EaseInDuration"].AsFloat = state.myEaseInDuration;
				st["EaseOutDuration"].AsFloat = state.myEaseOutDuration;
				st["Probability"].AsFloat = state.myProbability;
				st["StateType"].AsInt = state.myStateType;
				st["StateGroup"].AsInt = state.myStateGroup;

				JSONArray tlist = new JSONArray();
				for (int i=0; i<state.myTransitions.Count; ++i)
					tlist.Add("", state.myTransitions[i].myName);
				st["Transitions"] = tlist;

				if (state.myControlEntries.Count > 0)
				{
					JSONClass celist = new JSONClass();
					foreach (var e in state.myControlEntries)
					{
						ControlEntryAnchored ce = e.Value;
						JSONClass ceclass = new JSONClass();
						ceclass["PX"].AsFloat = ce.myAnchorOffset.myPosition.x;
						ceclass["PY"].AsFloat = ce.myAnchorOffset.myPosition.y;
						ceclass["PZ"].AsFloat = ce.myAnchorOffset.myPosition.z;
						Vector3 rotation = ce.myAnchorOffset.myRotation.eulerAngles;
						ceclass["RX"].AsFloat = rotation.x;
						ceclass["RY"].AsFloat = rotation.y;
						ceclass["RZ"].AsFloat = rotation.z;
						ceclass["AnchorMode"].AsInt = ce.myAnchorMode;
						if (ce.myAnchorMode >= ControlEntryAnchored.ANCHORMODE_SINGLE)
						{
							ceclass["DampingTime"].AsFloat = ce.myDampingTime;
							ceclass["AnchorAAtom"] = ce.myAnchorAAtom;
							ceclass["AnchorAControl"] = ce.myAnchorAControl;
						}
						if (ce.myAnchorMode == ControlEntryAnchored.ANCHORMODE_BLEND)
						{
							ceclass["AnchorBAtom"] = ce.myAnchorBAtom;
							ceclass["AnchorBControl"] = ce.myAnchorBControl;
							ceclass["BlendRatio"].AsFloat = ce.myBlendRatio;
						}
						celist[e.Key.myName] = ceclass;
					}
					st["ControlEntries"] = celist;
				}

				if (state.myMorphEntries.Count > 0)
				{
					JSONClass melist = new JSONClass();
					foreach (var e in state.myMorphEntries)
					{
						melist[e.Key.mySID].AsFloat = e.Value;
					}
					st["MorphEntries"] = melist;
				}

				st[state.EnterBeginTrigger.Name] = state.EnterBeginTrigger.GetJSON(base.subScenePrefix);
				st[state.EnterEndTrigger.Name] = state.EnterEndTrigger.GetJSON(base.subScenePrefix);
				st[state.ExitBeginTrigger.Name] = state.ExitBeginTrigger.GetJSON(base.subScenePrefix);
				st[state.ExitEndTrigger.Name] = state.ExitEndTrigger.GetJSON(base.subScenePrefix);

				slist.Add("", st);
			}
			layer["States"] = slist;

			jc["Layer"] = layer;

			return jc;
		}

		private Layer LoadLayer(JSONClass jc, bool keepName)
		{
			// reset
			foreach (var s in myCurrentLayer.myStates)
			{
				State state = s.Value;
				state.EnterBeginTrigger.Remove();
				state.EnterEndTrigger.Remove();
				state.ExitBeginTrigger.Remove();
				state.ExitEndTrigger.Remove();
			}

			myCurrentLayer.myControlCaptures.Clear();
			myCurrentLayer.myMorphCaptures.Clear();
			myCurrentLayer.myStates.Clear();
			myCurrentState = null;
			myNextState = null;
			myBlendState.myControlEntries.Clear();
			myBlendState.myMorphEntries.Clear();
			myClock = 0.0f;

			// load info
			int version = jc["Info"].AsObject["Version"].AsInt;

			// load captures
			JSONClass layer = jc["Layer"].AsObject;

			if(keepName)
				myCurrentLayer = CreateLayer(myCurrentLayer.myName);
			else
				myCurrentLayer = CreateLayer(layer["Name"]);

			// load captures
			if (layer.HasKey("ControlCaptures"))
			{
				JSONArray cclist = layer["ControlCaptures"].AsArray;
				for (int i=0; i<cclist.Count; ++i)
				{
					ControlCapture cc;
					if (version <= 2)
					{
						// handling legacy
						cc = new ControlCapture(this, cclist[i].Value);
					}
					else
					{
						JSONClass ccclass = cclist[i].AsObject;
						cc = new ControlCapture(this, ccclass["Name"]);
						cc.myApplyPosition = ccclass["ApplyPos"].AsBool;
						cc.myApplyRotation = ccclass["ApplyRot"].AsBool;
					}

					if (cc.IsValid())
						myCurrentLayer.myControlCaptures.Add(cc);
				}
			}
			if (layer.HasKey("MorphCaptures"))
			{
				JSONArray mclist = layer["MorphCaptures"].AsArray;
				DAZCharacterSelector geometry = containingAtom.GetStorableByID("geometry") as DAZCharacterSelector;
				for (int i=0; i<mclist.Count; ++i)
				{
					MorphCapture mc;
					if (version >= 9)
					{
						JSONClass mcclass = mclist[i].AsObject;
						string uid = mcclass["UID"];
						if (uid.EndsWith(".vmi")) // handle custom morphs, resolve VAR packages
							uid = SuperController.singleton.NormalizeLoadPath(uid);
						string sid = mcclass["SID"];
						mc = new MorphCapture(geometry, uid, sid);
						mc.myApply = mcclass["Apply"].AsBool;
					}
					else if (version == 8)
					{
						// handling legacy
						JSONClass mcclass = mclist[i].AsObject;
						mc = new MorphCapture(this, geometry, mcclass["UID"], mcclass["IsFemale"].AsBool);
						mc.myApply = mcclass["Apply"].AsBool;
					}
					else if (version >= 3 && version <= 7)
					{
						// handling legacy
						JSONClass mcclass = mclist[i].AsObject;
						mc = new MorphCapture(this, geometry, mcclass["Name"]);
						mc.myApply = mcclass["Apply"].AsBool;
					}
					else // (version <= 2)
					{
						// handling legacy
						mc = new MorphCapture(this, geometry, mclist[i].Value);
					}

					if (mc.IsValid())
						myCurrentLayer.myMorphCaptures.Add(mc);
				}
			}

			// load states
			JSONArray slist = layer["States"].AsArray;
			for (int i=0; i<slist.Count; ++i)
			{
				// load state
				JSONClass st = slist[i].AsObject;
				State state;
				if (version == 1)
				{
					// handling legacy
					state = new State(this, st["Name"]) {
						myWaitDurationMin = st["WaitDurationMin"].AsFloat,
						myWaitDurationMax = st["WaitDurationMax"].AsFloat,
						myTransitionDuration = st["TransitionDuration"].AsFloat,
						myProbability = DEFAULT_PROBABILITY,
						myStateType = st["IsTransition"].AsBool ? STATETYPE_CONTROLPOINT : STATETYPE_REGULARSTATE,
						myStateGroup = 0
					};
				}
				else
				{
					state = new State(this, st["Name"]) {
						myWaitInfiniteDuration = st["WaitInfiniteDuration"].AsBool,
						myWaitForSync = st["WaitForSync"].AsBool,
						myAllowInGroupTransition = st["AllowInGroupT"].AsBool,
						myWaitDurationMin = st["WaitDurationMin"].AsFloat,
						myWaitDurationMax = st["WaitDurationMax"].AsFloat,
						myTransitionDuration = st["TransitionDuration"].AsFloat,
						myEaseInDuration = st.HasKey("EaseInDuration") ? st["EaseInDuration"].AsFloat : DEFAULT_EASEIN_DURATION,
						myEaseOutDuration = st.HasKey("EaseOutDuration") ? st["EaseOutDuration"].AsFloat : DEFAULT_EASEOUT_DURATION,
						myProbability = st["Probability"].AsFloat,
						myStateType = st["StateType"].AsInt,
						myStateGroup = st["StateGroup"].AsInt
					};
				}

				state.EnterBeginTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);
				state.EnterEndTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);
				state.ExitBeginTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);
				state.ExitEndTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);


				if (myCurrentLayer.myStates.ContainsKey(state.myName))
					continue;
				myCurrentLayer.myStates[state.myName] = state;

				// load control captures
				if (myCurrentLayer.myControlCaptures.Count > 0)
				{
					JSONClass celist = st["ControlEntries"].AsObject;
					foreach (string ccname in celist.Keys)
					{
						ControlCapture cc = myCurrentLayer.myControlCaptures.Find(x => x.myName == ccname);
						if (cc == null)
							continue;

						JSONClass ceclass = celist[ccname].AsObject;
						ControlEntryAnchored ce = new ControlEntryAnchored(this, ccname);
						ce.myAnchorOffset.myPosition.x = ceclass["PX"].AsFloat;
						ce.myAnchorOffset.myPosition.y = ceclass["PY"].AsFloat;
						ce.myAnchorOffset.myPosition.z = ceclass["PZ"].AsFloat;
						Vector3 rotation;
						rotation.x = ceclass["RX"].AsFloat;
						rotation.y = ceclass["RY"].AsFloat;
						rotation.z = ceclass["RZ"].AsFloat;
						ce.myAnchorOffset.myRotation.eulerAngles = rotation;
						ce.myAnchorMode = ceclass["AnchorMode"].AsInt;
						if (ce.myAnchorMode >= ControlEntryAnchored.ANCHORMODE_SINGLE)
						{
							ce.myDampingTime = ceclass["DampingTime"].AsFloat;
							ce.myAnchorAAtom = ceclass["AnchorAAtom"].Value;
							ce.myAnchorAControl = ceclass["AnchorAControl"].Value;

							if (ce.myAnchorAAtom == "[Self]") // legacy
								ce.myAnchorAAtom = containingAtom.uid;
						}
						if (ce.myAnchorMode == ControlEntryAnchored.ANCHORMODE_BLEND)
						{
							ce.myAnchorBAtom = ceclass["AnchorBAtom"].Value;
							ce.myAnchorBControl = ceclass["AnchorBControl"].Value;
							ce.myBlendRatio = ceclass["BlendRatio"].AsFloat;

							if (ce.myAnchorBAtom == "[Self]") // legacy
								ce.myAnchorBAtom = containingAtom.uid;
						}
						ce.Initialize();

						state.myControlEntries.Add(cc, ce);
					}
					for (int j=0; j<myCurrentLayer.myControlCaptures.Count; ++j)
					{
						if (!state.myControlEntries.ContainsKey(myCurrentLayer.myControlCaptures[j]))
							myCurrentLayer.myControlCaptures[j].CaptureEntry(state);
					}
				}

				// load morph captures
				if (myCurrentLayer.myMorphCaptures.Count > 0)
				{
					JSONClass melist = st["MorphEntries"].AsObject;
					foreach (string key in melist.Keys)
					{
						MorphCapture mc = null;
						if (version >= 9)
						{
							mc = myCurrentLayer.myMorphCaptures.Find(x => x.mySID == key);
						}
						else if (version == 8)
						{
							// legacy handling of old qualifiedName
							string uid;
							DAZCharacterSelector.Gender gender;
							if (key.EndsWith("#Female"))
							{
								uid = key.Substring(0, key.Length-7);
								gender = DAZCharacterSelector.Gender.Female;
							}
							else if (key.EndsWith("#Male"))
							{
								uid = key.Substring(0, key.Length-5);
								gender = DAZCharacterSelector.Gender.Male;
							}
							else
							{
								continue;
							}

							mc = myCurrentLayer.myMorphCaptures.Find(x => x.myGender == gender && x.myMorph.uid == uid);
						}
						else // version <= 7
						{
							// legacy handling of old qualifiedName where the order was reversed
							string uid;
							DAZCharacterSelector.Gender gender;
							if (key.StartsWith("Female#"))
							{
								uid = key.Substring(7);
								gender = DAZCharacterSelector.Gender.Female;
							}
							else if (key.StartsWith("Male#"))
							{
								uid = key.Substring(5);
								gender = DAZCharacterSelector.Gender.Male;
							}
							else
							{
								continue;
							}

							mc = myCurrentLayer.myMorphCaptures.Find(x => x.myGender == gender && x.myMorph.uid == uid);
						}

						if (mc == null)
						{
							continue;
						}
						float me = melist[key].AsFloat;
						state.myMorphEntries.Add(mc, me);
					}
					for (int j=0; j<myCurrentLayer.myMorphCaptures.Count; ++j)
					{
						if (!state.myMorphEntries.ContainsKey(myCurrentLayer.myMorphCaptures[j]))
							myCurrentLayer.myMorphCaptures[j].CaptureEntry(state);
					}
				}
			}

			// load transitions
			for (int i=0; i<slist.Count; ++i)
			{
				JSONClass st = slist[i].AsObject;
				State source;
				if (!myCurrentLayer.myStates.TryGetValue(st["Name"], out source))
					continue;

				JSONArray tlist = st["Transitions"].AsArray;
				for (int j=0; j<tlist.Count; ++j)
				{
					State target;
					if (myCurrentLayer.myStates.TryGetValue(tlist[j].Value, out target))
						source.myTransitions.Add(target);
				}
			}

			// load settings
			myPlayPaused.valNoCallback = jc.HasKey("Paused") && jc["Paused"].AsBool;
			myPlayPaused.setCallbackFunction(myPlayPaused.val);

			int stateMask = jc["StateMask"].AsInt;
			myStateMask = (uint)stateMask;

			myOptionsDefaultToWorldAnchor.val = jc.HasKey("DefaultToWorldAnchor") && jc["DefaultToWorldAnchor"].AsBool;

			SwitchLayerAction(myCurrentLayer.myName);

			// blend to initial state
			if (jc.HasKey("InitialState"))
			{
				State initial;
				if (myCurrentLayer.myStates.TryGetValue(jc["InitialState"].Value, out initial))
				{
					myCurrentLayer.SetState(initial);
					myMainState.valNoCallback = initial.myName;
				}
			}
			return myCurrentLayer;
		}

		// =======================================================================================

		private class Animation
		{
			public string myName;
			public Dictionary<string, Layer> myLayers = new Dictionary<string, Layer>();

			public Animation(string name)
			{
				// SuperController.LogMessage("****** Animation.Animation ******");
				myName = name;
				// SuperController.LogMessage("****** Animation.Animation End ******");
			}

			public void Clear()
			{
				// SuperController.LogMessage("****** Animation.Clear ******");
				foreach (var l in myLayers)
				{
					Layer layer = l.Value;
					layer.Clear();
				}
				// SuperController.LogMessage("****** Animation.Clear End ******");
			}

		}


		// =======================================================================================
		private class Layer
		{
			public string myName;
			public string myDefaultState;
			public Dictionary<string, State> myStates = new Dictionary<string, State>();
			public bool myNoValidTransition = false;
			public State myCurrentState;
			public State myNextState;
			public List<ControlCapture> myControlCaptures = new List<ControlCapture>();
			public List<MorphCapture> myMorphCaptures = new List<MorphCapture>();
			private List<State> myTransition = new List<State>(8);
			private List<State> myCurrentTransition = new List<State>(MAX_STATES);
			public float myClock = 0.0f;
			public float myDuration = 1.0f;
			private List<TriggerActionDiscrete> myTriggerActionsNeedingUpdate = new List<TriggerActionDiscrete>();
			private State myBlendState = State.CreateBlendState();

			public Layer(string name)
			{
				// SuperController.LogMessage("****** Layer.Layer ******");
				// SuperController.LogMessage("Layer.Layer: Creating layer " + name);
				myName = name;
				// SuperController.LogMessage("****** Layer.Layer End ******");
			}
			public void CaptureState(State state)
			{
				// SuperController.LogMessage("****** Layer.CaptureState ******");
				// SuperController.LogMessage("Layer.CaptureState: Capturing state " + state.myName);
				// SuperController.LogMessage("Layer.CaptureState: N controlcaptures " + myControlCaptures.Count);
				// SuperController.LogMessage("Layer.CaptureState: N morphcaptures " + myMorphCaptures.Count);
				for (int i=0; i<myControlCaptures.Count; ++i)
					myControlCaptures[i].CaptureEntry(state);
				for (int i=0; i<myMorphCaptures.Count; ++i)
					myMorphCaptures[i].CaptureEntry(state);
				// SuperController.LogMessage("****** Layer.CaptureState End ******");
			}

			public void SetState(State state)
			{
				// SuperController.LogMessage("****** Layer.SetState ******");
				myNoValidTransition = false;
				// SuperController.LogMessage("Layer.SetState: setting state " + state.myName);
				myCurrentState = state;
				myNextState = null;

				myClock = 0.0f;
				if (state.myWaitInfiniteDuration){
					// SuperController.LogMessage("Layer.SetState: State has infinite duration.");
					myDuration = float.MaxValue;
				}
				else {
					// SuperController.LogMessage("Layer.SetState: State does not have infinite duration.");
					myDuration = UnityEngine.Random.Range(state.myWaitDurationMin, state.myWaitDurationMax);
				}
				// SuperController.LogMessage("Layer.SetState: Clearing current transition.");
				myCurrentTransition.Clear();
				// SuperController.LogMessage("****** Layer.SetState End ******");
			}

			public void Clear()
			{
				// SuperController.LogMessage("****** Layer.Clear ******");
				foreach (var s in myStates)
				{
					State state = s.Value;
					state.EnterBeginTrigger.Remove();
					state.EnterEndTrigger.Remove();
					state.ExitBeginTrigger.Remove();
					state.ExitEndTrigger.Remove();
				}
				// SuperController.LogMessage("****** Layer.Clear End ******");
			}

			public float Smooth(float a, float b, float d, float t)
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

			public void UpdateLayer()
			{
				// SuperController.LogMessage("****** Layer.UpdateLayer ******");
				for (int i=0; i<myTriggerActionsNeedingUpdate.Count; ++i)
					myTriggerActionsNeedingUpdate[i].Update();
				myTriggerActionsNeedingUpdate.RemoveAll(a => !a.timerActive);

				if (myCurrentState == null){
					// SuperController.LogMessage("****** Layer.UpdateLayer End 1 ******");
					return;
				}

				bool paused = myPaused && myNextState == null && myTransition.Count == 0;
				if (!paused)
					myClock = Mathf.Min(myClock + Time.deltaTime, 100000.0f);

				if (myNextState != null)
				{
					float t = Smooth(myCurrentState.myEaseOutDuration, myNextState.myEaseInDuration, myDuration, myClock);
					for (int i=0; i<myControlCaptures.Count; ++i)
						myControlCaptures[i].UpdateTransition(t);
					for (int i=0; i<myMorphCaptures.Count; ++i)
						myMorphCaptures[i].UpdateTransition(t);
				}
				else if (!paused || myPlayMode)
				{
					for (int i=0; i<myControlCaptures.Count; ++i)
						myControlCaptures[i].UpdateState(myCurrentState);
				}

				if (myClock >= myDuration)
				{
					if (myNextState != null)
					{
						State previousState = myCurrentState;
						SetState(myNextState);
						if (myTransition.Count == 0)
							myMainState.valNoCallback = myCurrentState.myName;

						if (previousState.ExitEndTrigger != null)
							previousState.ExitEndTrigger.Trigger(myTriggerActionsNeedingUpdate);
						if (myCurrentState.EnterEndTrigger != null)
							myCurrentState.EnterEndTrigger.Trigger(myTriggerActionsNeedingUpdate);
					}
					else if (!paused && !myCurrentState.myWaitForSync && !myNoValidTransition)
					{
						if (myTransition.Count == 0)
							SetRandomTransition();
						else
							SetTransition();
					}
					else if (myStateMaskChanged)
					{
						TryApplyStateMaskChange();
					}
				}
				else if (myStateMaskChanged)
				{
					TryApplyStateMaskChange();
				}
				// SuperController.LogMessage("****** Layer.UpdateLayer End 3 ******");
			}

			public void SetTransition(float duration = -1.0f)
			{
				// SuperController.LogMessage("****** Layer.SetTransition ******");
				float d = 0.0f;
				int entryCount = Mathf.Min(myTransition.Count, MAX_STATES);
				for (int i=0; i<entryCount; ++i)
				{
					d += myTransition[i].myTransitionDuration;
					if (i > 0 && !myTransition[i].IsControlPoint)
					{
						entryCount = i+1;
						break;
					}
				}

				myNoValidTransition = false;
				myCurrentTransition.Clear();
				for (int i=0; i<entryCount; ++i)
					myCurrentTransition.Add(myTransition[i]);

				myClock = 0.0f;
				myDuration = (duration < 0) ? d : duration;
				myDuration = Mathf.Max(myDuration, 0.001f);
				myNextState = myTransition[entryCount-1];
				for (int i=0; i<myControlCaptures.Count; ++i)
					myControlCaptures[i].SetTransition(myTransition, entryCount);
				for (int i=0; i<myMorphCaptures.Count; ++i)
					myMorphCaptures[i].SetTransition(myTransition, entryCount);

				if (myTransition.Count == entryCount)
					myTransition.Clear();
				else
					myTransition.RemoveRange(0, entryCount-1);

				if (myCurrentState.ExitBeginTrigger != null)
					myCurrentState.ExitBeginTrigger.Trigger(myTriggerActionsNeedingUpdate);
				if (myNextState.EnterBeginTrigger != null)
					myNextState.EnterBeginTrigger.Trigger(myTriggerActionsNeedingUpdate);
				// SuperController.LogMessage("****** Layer.SetTransition End ******");
			}

			public void SetRandomTransition()
			{
				// SuperController.LogMessage("****** Layer.SetRandomTransition ******");
				List<State> states = new List<State>(16);
				myTransition.Clear();
				myTransition.Add(myCurrentState);
				myNextState = null;
				GatherStates(1, states);

				int i;
				float sum = 0.0f;
				for (i=0; i<states.Count; ++i)
					sum += states[i].myProbability;
				if (sum == 0.0f)
				{
					myTransition.Clear();
					myNoValidTransition = true;
				}
				else
				{
					float threshold = UnityEngine.Random.Range(0.0f, sum);
					sum = 0.0f;
					for (i=0; i<states.Count-1; ++i)
					{
						sum += states[i].myProbability;
						if (threshold <= sum)
							break;
					}
					GatherTransition(1, i, 0);
					SetTransition();
				}
				// SuperController.LogMessage("****** Layer.SetRandomTransition End ******");
			}

			public void SetBlendTransition(State state, bool debug = false)
			{
				// SuperController.LogMessage("****** Layer.SetBlendTransition ******");
				// SuperController.LogMessage("Layer.SetBlendTransition: Setting blend transition to "+state.myName);
				myTransition.Clear();
				myTransition.Add(myCurrentState);
				for (int i=0; i<myTransition.Count; ++i) {
					// SuperController.LogMessage("Layer.SetBlendTransition: transition " + myTransition[i].myName);
				}
				if (myCurrentState != null)
				{
					// SuperController.LogMessage("Layer.SetBlendTransition: myCurrentState not null, " + myCurrentState.myName);
					List<State> states = new List<State>(16);
					myNextState = null;
					GatherStates(1, states);
					// SuperController.LogMessage("Layer.SetBlendTransition: Gathered states");
					List<int> indices = new List<int>(4);
					for (int i=0; i<states.Count; ++i)
					{
						if (states[i] == state)
							indices.Add(i);
					}
					if (indices.Count == 0)
					{
						// SuperController.LogMessage("Layer.SetBlendTransition: Indices empty");
						states.Clear();
						myNextState = state;
						GatherStates(1, states);
						for (int i=0; i<states.Count; ++i)
						{
							if (states[i] == state)
								indices.Add(i);
						}
					}
					if (indices.Count > 0)
					{
						int selected = UnityEngine.Random.Range(0, indices.Count);
						// SuperController.LogMessage("Layer.SetBlendTransition: indices not empty");
						GatherTransition(1, indices[selected], 0);
					}
				}

				if (myCurrentState == null || debug)
				{
					CaptureState(myBlendState);
					myTransition[0] = myBlendState;
					if (myCurrentState != null && myTransition.Count > 1)
					{
						myBlendState.myTransitionDuration = myCurrentState.myTransitionDuration;
						myBlendState.myEaseInDuration = myCurrentState.myEaseInDuration;
						myBlendState.myEaseOutDuration = myCurrentState.myEaseOutDuration;
					}
					else
					{
						myBlendState.myTransitionDuration = DEFAULT_BLEND_DURATION;
						myBlendState.myEaseInDuration = DEFAULT_EASEIN_DURATION;
						myBlendState.myEaseOutDuration = DEFAULT_EASEOUT_DURATION;
					}
					myBlendState.AssignOutTriggers(myCurrentState);
					SetState(myBlendState);
				}

				if (myTransition.Count == 1) // Did not find transition....fake one
				{
					// SuperController.LogMessage("Layer.SetBlendTransition: single transition; adding");
					myTransition.Add(state);
				}

				// SuperController.LogMessage("Layer.SetBlendTransition: setting transition");
				SetTransition();
				// SuperController.LogMessage("****** Layer.SetBlendTransition End ******");
			}

			public void GatherStates(int transitionLength, List<State> states)
			{
				// SuperController.LogMessage("****** Layer.GatherStates ******");
				State source = myTransition[0];
				State current = myTransition[myTransition.Count-1];
				// SuperController.LogMessage("Layer.GatherStates source: " + source.myName);
				// SuperController.LogMessage("Layer.GatherStates current: " + current.myName);
				for (int i=0; i<current.myTransitions.Count; ++i)
				{
					State next = current.myTransitions[i];
					// SuperController.LogMessage("Layer.GatherStates next: " + next.myName);
					if (myTransition.Contains(next))
						continue;

					if (next.IsRegularState || next == myNextState)
					{
						// SuperController.LogMessage("Layer.GatherStates next is regular state");
						if (DoAcceptRegularState(source, next))
							states.Add(next);
					}
					else if (next.IsControlPoint)
					{
						if (transitionLength >= MAX_STATES-1)
							continue;
						myTransition.Add(next);
						GatherStates(transitionLength+1, states);
						myTransition.RemoveAt(myTransition.Count-1);
					}
					else // next.IsIntermediate
					{
						myTransition.Add(next);
						GatherStates(1, states);
						myTransition.RemoveAt(myTransition.Count-1);
					}
				}
				// SuperController.LogMessage("****** Layer.GatherStates End ******");
			}

			public int GatherTransition(int transitionLength, int selected, int index)
			{
				// SuperController.LogMessage("****** Layer.GatherTransition ******");
				// SuperController.LogMessage("Layer.GatherTransition: gathering transitions");
				State source = myTransition[0];
				State current = myTransition[myTransition.Count-1];
				// SuperController.LogMessage("Layer.GatherTransition: Current transition/state " + current.myName);
				for (int i=0; i<current.myTransitions.Count; ++i)
				{
					State next = current.myTransitions[i];
					// SuperController.LogMessage("Layer.GatherTransition: Next transition/state " + next.myName);
					if (myTransition.Contains(next))
						continue;

					if (next.IsRegularState || next == myNextState)
					{
						// SuperController.LogMessage("Layer.GatherTransition: Next transition/state is regular or something");
						if (!DoAcceptRegularState(source, next))
						{
							// SuperController.LogMessage("Layer.GatherTransition: Not accepting regular state");
							continue;
						}
						else if (index == selected)
						{
							myTransition.Add(next);
							return -1;
						}
						else
						{
							++index;
						}
					}
					else if (next.IsControlPoint)
					{
						if (transitionLength >= MAX_STATES-1)
							continue;
						myTransition.Add(next);
						index = GatherTransition(transitionLength+1, selected, index);
						if (index == -1)
							return -1;
						myTransition.RemoveAt(myTransition.Count-1);
					}
					else // next.IsIntermediate
					{
						myTransition.Add(next);
						index = GatherTransition(1, selected, index);
						if (index == -1)
							return -1;
						myTransition.RemoveAt(myTransition.Count-1);
					}
				}
				// SuperController.LogMessage("****** Layer.GatherTransition End ******");
				return index;
			}

			public bool DoAcceptRegularState(State source, State next, bool debugMode = false)
			{
				// SuperController.LogMessage("****** Layer.DoAcceptRegularState ******");
				if (next == myNextState && !debugMode){
					// SuperController.LogMessage("****** Layer.DoAcceptRegularState End 1 ******");
					return true;
				}
				if (next.myProbability < 0.01f){
					// SuperController.LogMessage("****** Layer.DoAcceptRegularState End 2 ******");
					return false;
				}
				if (myStateMask != 0 && (myStateMask & next.StateMask) == 0 && !debugMode){
					// SuperController.LogMessage("****** Layer.DoAcceptRegularState End 3 ******");
					return false;
				}
				if (source.myStateGroup == 0 || source.myStateGroup != next.myStateGroup){
					// SuperController.LogMessage("****** Layer.DoAcceptRegularState End 4 ******");
					return true;
				}

				// in-group transition: source.myStateGroup == next.myStateGroup
				if (!source.myAllowInGroupTransition || !next.myAllowInGroupTransition) {
					// SuperController.LogMessage("****** Layer.DoAcceptRegularState End 5 ******");
					return false;
				}

				List<State> transition = debugMode ? myDebugTransition : myTransition;
				for (int t=1; t<transition.Count; ++t)
				{
					if (!transition[t].myAllowInGroupTransition) {
						// SuperController.LogMessage("****** Layer.DoAcceptRegularState End 6 ******");
						return false;
					}
				}

				// SuperController.LogMessage("****** Layer.DoAcceptRegularState End 7 ******");
				return true;
			}

			public void BlendToRandomState(float duration)
			{
				// SuperController.LogMessage("****** Layer.BlendToRandomState ******");
				List<State> possible = new List<State>(myCurrentLayer.myStates.Count);
				foreach (var state in myCurrentLayer.myStates)
				{
					if (state.Value.IsRegularState)
						possible.Add(state.Value);
				}
				int idx = UnityEngine.Random.Range(0, possible.Count);

				CaptureState(myBlendState);
				myTransition.Clear();
				myTransition.Add(myBlendState);
				myTransition.Add(possible[idx]);

				myBlendState.AssignOutTriggers(myCurrentState);
				SetState(myBlendState);
				SetTransition(duration);
				// SuperController.LogMessage("****** Layer.BlendToRandomState End ******");
			}

			public void TriggerSyncAction()
			{
				// SuperController.LogMessage("****** Layer.TriggerSyncAction ******");
				if (myCurrentState != null && myNextState == null
				&& myClock >= myDuration && !myCurrentState.myWaitInfiniteDuration
				&& myCurrentState.myWaitForSync && !myPaused)
				{
					if (myTransition.Count == 0)
						SetRandomTransition();
					else
						SetTransition();
				}
				// SuperController.LogMessage("****** Layer.TriggerSyncAction End ******");
			}

			private void SetStateMaskAction(string v)
			{
				// SuperController.LogMessage("****** Layer.SetStateMaskAction ******");
				mySetStateMask.valNoCallback = string.Empty;

				myStateMask = 0;
				bool invert = false;
				bool error = false;
				if (v.ToLower() != "clear")
				{
					for (int i=0; i<v.Length; ++i)
					{
						char c = v[i];
						if (i == 0 && c == '!')
							invert = true;
						else if (c >= 'A' && c <= 'L')
							myStateMask |= 1u << (c - 'A' + 1);
						else if (c >= 'a' && c <= 'l')
							myStateMask |= 1u << (c - 'a' + 1);
						else if (c == 'N' || c == 'n')
							myStateMask |= 1u << 0;
						else
							error = true;
					}
				}

				if (error)
				{
					SuperController.LogError("AnimationPoser: SetStateMask set to invalid data: '"+v+"'");
					return;
				}

				if (myStateMask != 0 && invert)
					myStateMask = ~myStateMask;

				myStateMaskChanged = true;
				TryApplyStateMaskChange();
				// SuperController.LogMessage("****** Layer.SetStateMaskAction End ******");
			}

			private void PartialStateMaskAction(string v)
			{
				// SuperController.LogMessage("****** Layer.PartialStateMaskAction ******");
				myPartialStateMask.valNoCallback = string.Empty;

				uint stateMask = 0;
				bool invert = false;
				bool error = false;
				string[] masks = v.Split(new char[]{' ',';','|'}, 32, StringSplitOptions.RemoveEmptyEntries);
				for (int m=0; m<masks.Length; ++m)
				{
					stateMask = 0;
					invert = false;
					for (int i=0; i<masks[m].Length; ++i)
					{
						char c = masks[m][i];
						if (i == 0 && c == '!')
							invert = true;
						else if (c >= 'A' && c <= 'L')
							stateMask |= 1u << (c - 'A' + 1);
						else if (c >= 'a' && c <= 'l')
							stateMask |= 1u << (c - 'a' + 1);
						else if (c == 'N' || c == 'n')
							stateMask |= 1u << 0;
						else
							error = true;
					}

					if (error)
					{
						SuperController.LogError("AnimationPoser: PartialStateMask set to invalid data: '"+v+"'");
						return;
					}

					if (stateMask == 0)
						continue;

					if (invert)
						myStateMask = myStateMask & ~stateMask;
					else
						myStateMask = myStateMask | stateMask;
				}

				myStateMaskChanged = true;
				TryApplyStateMaskChange();
				// SuperController.LogMessage("****** Layer.PartialStateMaskAction End ******");
			}

			private void TryApplyStateMaskChange()
			{
				// SuperController.LogMessage("****** Layer.TryApplyStateMaskChange ******");
				if (myCurrentState == null || myNextState != null || !myCurrentState.IsRegularState){
					// SuperController.LogMessage("****** Layer.TryApplyStateMaskChange End ******");
					return;
				}

				if (myClock >= myDuration && !myCurrentState.myWaitForSync && !myPaused)
				{
					myStateMaskChanged = false;
					myCurrentLayer.SetRandomTransition();
				}
				else if (myStateMask != 0 && (myCurrentState.StateMask & myStateMask) == 0)
				{
					myStateMaskChanged = false;
					myCurrentLayer.SetRandomTransition();
				}
				// SuperController.LogMessage("****** Layer.TryApplyStateMaskChange End ******");
			}
		}


		private class State
		{
			public string myName;
			public float myWaitDurationMin;
			public float myWaitDurationMax;
			public float myTransitionDuration;
			public float myEaseInDuration = DEFAULT_EASEIN_DURATION;
			public float myEaseOutDuration = DEFAULT_EASEOUT_DURATION;
			public float myProbability = DEFAULT_PROBABILITY;
			public int myStateType;
			public int myStateGroup;
			public bool myWaitInfiniteDuration = false;
			public bool myWaitForSync = false;
			public bool myAllowInGroupTransition = false;
			public uint myDebugIndex = 0;
			public Dictionary<ControlCapture, ControlEntryAnchored> myControlEntries = new Dictionary<ControlCapture, ControlEntryAnchored>();
			public Dictionary<MorphCapture, float> myMorphEntries = new Dictionary<MorphCapture, float>();
			public List<State> myTransitions = new List<State>();
			public EventTrigger EnterBeginTrigger;
			public EventTrigger EnterEndTrigger;
			public EventTrigger ExitBeginTrigger;
			public EventTrigger ExitEndTrigger;

			public bool IsRegularState { get { return myStateType == STATETYPE_REGULARSTATE; } }
			public bool IsControlPoint { get { return myStateType == STATETYPE_CONTROLPOINT; } }
			public bool IsIntermediate { get { return myStateType == STATETYPE_INTERMEDIATE; } }
			public uint StateMask { get { return 1u << myStateGroup; } }

			private State(string name)
			{
				// SuperController.LogMessage("****** State.State ******");
				myName = name;
				// SuperController.LogMessage("****** State.State End ******");
				// do NOT init event triggers
			}

			public State(MVRScript script, string name)
			{
				// SuperController.LogMessage("****** State.State 2 ******");
				myName = name;
				EnterBeginTrigger = new EventTrigger(script, "OnEnterBegin", name);
				EnterEndTrigger = new EventTrigger(script, "OnEnterEnd", name);
				ExitBeginTrigger = new EventTrigger(script, "OnExitBegin", name);
				ExitEndTrigger = new EventTrigger(script, "OnExitEnd", name);
				// SuperController.LogMessage("****** State.State 2 End ******");
			}

			public State(string name, State source)
			{
				// SuperController.LogMessage("****** State.State 3 ******");
				myName = name;
				myWaitDurationMin = source.myWaitDurationMin;
				myWaitDurationMax = source.myWaitDurationMax;
				myTransitionDuration = source.myTransitionDuration;
				myEaseInDuration = source.myEaseInDuration;
				myEaseOutDuration = source.myEaseOutDuration;
				myProbability = source.myProbability;
				myStateType = source.myStateType;
				myStateGroup = source.myStateGroup;
				myWaitInfiniteDuration = source.myWaitInfiniteDuration;
				myWaitForSync = source.myWaitForSync;
				myAllowInGroupTransition = source.myAllowInGroupTransition;
				EnterBeginTrigger = new EventTrigger(source.EnterBeginTrigger);
				EnterEndTrigger = new EventTrigger(source.EnterEndTrigger);
				ExitBeginTrigger = new EventTrigger(source.ExitBeginTrigger);
				ExitEndTrigger = new EventTrigger(source.ExitEndTrigger);
				// SuperController.LogMessage("****** State.State 3 End ******");
			}

			public static State CreateBlendState()
			{
				// SuperController.LogMessage("****** State.CreateBlendState ******");
				// SuperController.LogMessage("****** State.CreateBlendState End ******");
				return new State("BlendState") {
					myWaitDurationMin = 0.0f,
					myWaitDurationMax = 0.0f,
					myTransitionDuration = 0.2f,
					myStateType = STATETYPE_CONTROLPOINT
				};
			}

			public void AssignOutTriggers(State other)
			{
				// SuperController.LogMessage("****** State.AssignOutTriggers ******");
				ExitBeginTrigger = other?.ExitBeginTrigger;
				ExitEndTrigger = other?.ExitEndTrigger;
				// SuperController.LogMessage("****** State.AssignOutTriggers End ******");
			}
		}

		private class ControlCapture
		{
			public string myName;
			private AnimationPoser myPlugin;
			private Transform myTransform;
			private ControlEntryAnchored[] myTransition = new ControlEntryAnchored[MAX_STATES];
			private int myEntryCount = 0;
			public bool myApplyPosition = true;
			public bool myApplyRotation = true;

			private static Quaternion[] ourTempQuaternions = new Quaternion[MAX_STATES-1];
			private static float[] ourTempDistances = new float[DISTANCE_SAMPLES[DISTANCE_SAMPLES.Length-1] + 2];

			public ControlCapture(AnimationPoser plugin, string control)
			{
				// SuperController.LogMessage("****** ControlCapture.ControlCapture ******");
				myPlugin = plugin;
				myName = control;
				FreeControllerV3 controller = plugin.containingAtom.GetStorableByID(control) as FreeControllerV3;
				if (controller != null)
					myTransform = controller.transform;
				// SuperController.LogMessage("****** ControlCapture.ControlCapture End ******");
			}

			public void CaptureEntry(State state)
			{
				// SuperController.LogMessage("****** ControlCapture.CaptureEntry ******");
				ControlEntryAnchored entry;
				if (!state.myControlEntries.TryGetValue(this, out entry))
				{
					entry = new ControlEntryAnchored(myPlugin, myName);
					entry.Initialize();
					state.myControlEntries[this] = entry;
				}
				entry.Capture(myTransform.position, myTransform.rotation);
				// SuperController.LogMessage("****** ControlCapture.CaptureEntry End ******");
			}

			public void setDefaults(State state, State oldState)
			{
				// SuperController.LogMessage("****** ControlCapture.setDefaults ******");
				ControlEntryAnchored entry;
				ControlEntryAnchored oldEntry;
				if (!state.myControlEntries.TryGetValue(this, out entry))
					return;
				if (!oldState.myControlEntries.TryGetValue(this, out oldEntry))
					return;
				entry.myAnchorAAtom = oldEntry.myAnchorAAtom;
				entry.myAnchorAControl = oldEntry.myAnchorAControl;
				entry.myAnchorMode = oldEntry.myAnchorMode;
				// SuperController.LogMessage("****** ControlCapture.setDefaults End ******");
			}

			public void SetTransition(List<State> states, int entryCount)
			{
				// SuperController.LogMessage("****** ControlCapture.SetTransition ******");
				myEntryCount = entryCount;
				for (int i=0; i<myEntryCount; ++i)
				{
					if (!states[i].myControlEntries.TryGetValue(this, out myTransition[i]))
					{
						CaptureEntry(states[i]);
						myTransition[i] = states[i].myControlEntries[this];
					}
				}
				// SuperController.LogMessage("****** ControlCapture.SetTransition End ******");
			}

			public void UpdateTransition(float t)
			{
				// SuperController.LogMessage("****** ControlCapture.UpdateTransition ******");
				for (int i=0; i<myEntryCount; ++i)
					myTransition[i].Update();

				//t = ArcLengthParametrization(t);

				if (myApplyPosition)
				{
					switch (myEntryCount)
					{
						case 4:	myTransform.position = EvalBezierCubicPosition(t);          break;
						case 3: myTransform.position = EvalBezierQuadraticPosition(t);      break;
						case 2: myTransform.position = EvalBezierLinearPosition(t);         break;
						default: myTransform.position = myTransition[0].myEntry.myPosition; break;
					}
				}
				if (myApplyRotation)
				{
					switch (myEntryCount)
					{
						case 4: myTransform.rotation = EvalBezierCubicRotation(t);          break;
						case 3: myTransform.rotation = EvalBezierQuadraticRotation(t);      break;
						case 2: myTransform.rotation = EvalBezierLinearRotation(t);         break;
						default: myTransform.rotation = myTransition[0].myEntry.myRotation; break;
					}
				}
				// SuperController.LogMessage("****** ControlCapture.UpdateTransition End ******");
			}

			private float ArcLengthParametrization(float t)
			{
				// SuperController.LogMessage("****** ControlCapture.ArcLengthParametrization ******");
				if (myEntryCount <= 2 || myEntryCount > 4){
					// SuperController.LogMessage("****** ControlCapture.ArcLengthParametrization End 1******");
					return t;
				}

				int numSamples = DISTANCE_SAMPLES[myEntryCount];
				float numLines = (float)(numSamples+1);
				float distance = 0.0f;
				Vector3 previous = myTransition[0].myEntry.myPosition;
				ourTempDistances[0] = 0.0f;

				if (myEntryCount == 3)
				{
					for (int i=1; i<=numSamples; ++i)
					{
						Vector3 current = EvalBezierQuadraticPosition(i / numLines);
						distance += Vector3.Distance(previous, current);
						ourTempDistances[i] = distance;
						previous = current;
					}
				}
				else
				{
					for (int i=1; i<=numSamples; ++i)
					{
						Vector3 current = EvalBezierCubicPosition(i / numLines);
						distance += Vector3.Distance(previous, current);
						ourTempDistances[i] = distance;
						previous = current;
					}
				}

				distance += Vector3.Distance(previous, myTransition[myEntryCount-1].myEntry.myPosition);
				ourTempDistances[numSamples+1] = distance;

				t *= distance;

				int idx = Array.BinarySearch(ourTempDistances, 0, numSamples+2, t);
				if (idx < 0)
				{
					idx = ~idx;
					if (idx == 0){
						// SuperController.LogMessage("****** ControlCapture.ArcLengthParametrization End 2 ******");
						return 0.0f;
					}
					else if (idx >= numSamples+2){
						// SuperController.LogMessage("****** ControlCapture.ArcLengthParametrization End 3 ******");
						return 1.0f;
					}
					t = Mathf.InverseLerp(ourTempDistances[idx-1], ourTempDistances[idx], t);
					// SuperController.LogMessage("****** ControlCapture.ArcLengthParametrization End 4 ******");
					return Mathf.LerpUnclamped((idx-1) / numLines, idx / numLines, t);
				}
				else
				{
					// SuperController.LogMessage("****** ControlCapture.ArcLengthParametrization End 5 ******");
					return idx / numLines;
				}
			}

			private Vector3 EvalBezierLinearPosition(float t)
			{
				// SuperController.LogMessage("****** ControlCapture.EvalBezierLinearPosition End ******");
				return Vector3.LerpUnclamped(myTransition[0].myEntry.myPosition, myTransition[1].myEntry.myPosition, t);
			}

			private Vector3 EvalBezierQuadraticPosition(float t)
			{
				// evaluating quadratic Bézier curve using Bernstein polynomials
				// SuperController.LogMessage("****** ControlCapture.EvalBezierQuadraticPosition ******");
				float s = 1.0f - t;
				// SuperController.LogMessage("****** ControlCapture.EvalBezierQuadraticPosition End ******");
				return      (s*s) * myTransition[0].myEntry.myPosition
					 + (2.0f*s*t) * myTransition[1].myEntry.myPosition
					 +      (t*t) * myTransition[2].myEntry.myPosition;
			}

			private Vector3 EvalBezierCubicPosition(float t)
			{
				// evaluating cubic Bézier curve using Bernstein polynomials
				// SuperController.LogMessage("****** ControlCapture.EvalBezierCubicPosition ******");
				float s = 1.0f - t;
				float t2 = t*t;
				float s2 = s*s;
				// SuperController.LogMessage("****** ControlCapture.EvalBezierCubicPosition End ******");
				return      (s*s2) * myTransition[0].myEntry.myPosition
					 + (3.0f*s2*t) * myTransition[1].myEntry.myPosition
					 + (3.0f*s*t2) * myTransition[2].myEntry.myPosition
					 +      (t*t2) * myTransition[3].myEntry.myPosition;
			}

			private Quaternion EvalBezierLinearRotation(float t)
			{
				// SuperController.LogMessage("****** ControlCapture.EvalBezierLinearRotation End ******");
				return Quaternion.SlerpUnclamped(myTransition[0].myEntry.myRotation, myTransition[1].myEntry.myRotation, t);
			}

			private Quaternion EvalBezierQuadraticRotation(float t)
			{
				// evaluating quadratic Bézier curve using de Casteljau's algorithm
				// SuperController.LogMessage("****** ControlCapture.EvalBezierQuadraticRotation ******");
				ourTempQuaternions[0] = Quaternion.SlerpUnclamped(myTransition[0].myEntry.myRotation, myTransition[1].myEntry.myRotation, t);
				ourTempQuaternions[1] = Quaternion.SlerpUnclamped(myTransition[1].myEntry.myRotation, myTransition[2].myEntry.myRotation, t);
				// SuperController.LogMessage("****** ControlCapture.EvalBezierQuadraticRotation End ******");
				return Quaternion.SlerpUnclamped(ourTempQuaternions[0], ourTempQuaternions[1], t);
			}

			private Quaternion EvalBezierCubicRotation(float t)
			{
				// evaluating cubic Bézier curve using de Casteljau's algorithm
				// SuperController.LogMessage("****** ControlCapture.EvalBezierCubicRotation ******");
				for (int i=0; i<3; ++i)
					ourTempQuaternions[i] = Quaternion.SlerpUnclamped(myTransition[i].myEntry.myRotation, myTransition[i+1].myEntry.myRotation, t);
				for (int i=0; i<2; ++i)
					ourTempQuaternions[i] = Quaternion.SlerpUnclamped(ourTempQuaternions[i], ourTempQuaternions[i+1], t);
				// SuperController.LogMessage("****** ControlCapture.EvalBezierCubicRotation End ******");
				return Quaternion.SlerpUnclamped(ourTempQuaternions[0], ourTempQuaternions[1], t);
			}

			public void UpdateState(State state)
			{
				// SuperController.LogMessage("****** ControlCapture.UpdateState ******");
				ControlEntryAnchored entry;
				if (state.myControlEntries.TryGetValue(this, out entry))
				{
					entry.Update();
					if (myApplyPosition)
						myTransform.position = entry.myEntry.myPosition;
					if (myApplyRotation)
						myTransform.rotation = entry.myEntry.myRotation;
				}
				// SuperController.LogMessage("****** ControlCapture.UpdateState End ******");
			}

			public bool IsValid()
			{
				// SuperController.LogMessage("****** ControlCapture.IsValid End ******");
				return myTransform != null;
			}
		}

		private struct ControlEntry
		{
			public Quaternion myRotation;
			public Vector3 myPosition;
		}

		private class ControlEntryAnchored
		{
			public const int ANCHORMODE_WORLD = 0;
			public const int ANCHORMODE_SINGLE = 1;
			public const int ANCHORMODE_BLEND = 2;

			public ControlEntry myEntry;
			public ControlEntry myAnchorOffset;
			public Transform myAnchorATransform;
			public Transform myAnchorBTransform;
			public int myAnchorMode = ANCHORMODE_SINGLE;
			public float myBlendRatio = DEFAULT_ANCHOR_BLEND_RATIO;
			public float myDampingTime = DEFAULT_ANCHOR_DAMPING_TIME;

			public string myAnchorAAtom;
			public string myAnchorBAtom;
			public string myAnchorAControl = "control";
			public string myAnchorBControl = "control";

			public ControlEntryAnchored(AnimationPoser plugin, string control)
			{
				// SuperController.LogMessage("****** ControlEntryAnchored.ControlEntryAnchored ******");
				Atom containingAtom = plugin.GetContainingAtom();
				if (plugin.myOptionsDefaultToWorldAnchor.val || containingAtom.type != "Person" || control == "control")
					myAnchorMode = ANCHORMODE_WORLD;
				myAnchorAAtom = myAnchorBAtom = containingAtom.uid;
				// SuperController.LogMessage("****** ControlEntryAnchored.ControlEntryAnchored End ******");
			}

			public ControlEntryAnchored Clone()
			{
				return (ControlEntryAnchored)MemberwiseClone();
			}

			public void Initialize()
			{
				// SuperController.LogMessage("****** ControlEntryAnchored.Initialize ******");
				GetTransforms();
				UpdateInstant();
				// SuperController.LogMessage("****** ControlEntryAnchored.Initialize End ******");
			}

			public void AdjustAnchor()
			{
				// SuperController.LogMessage("****** ControlEntryAnchored.AdjustAnchor ******");
				GetTransforms();
				Capture(myEntry.myPosition, myEntry.myRotation);
				// SuperController.LogMessage("****** ControlEntryAnchored.AdjustAnchor End ******");
			}

			private void GetTransforms()
			{
				// SuperController.LogMessage("****** ControlEntryAnchored.GetTransforms ******");
				if (myAnchorMode == ANCHORMODE_WORLD)
				{
					myAnchorATransform = null;
					myAnchorBTransform = null;
				}
				else
				{
					myAnchorATransform = GetTransform(myAnchorAAtom, myAnchorAControl);
					if (myAnchorMode == ANCHORMODE_BLEND)
						myAnchorBTransform = GetTransform(myAnchorBAtom, myAnchorBControl);
					else
						myAnchorBTransform = null;
				}
				// SuperController.LogMessage("****** ControlEntryAnchored.GetTransforms End ******");
			}

			private Transform GetTransform(string atomName, string controlName)
			{
				// SuperController.LogMessage("****** ControlEntryAnchored.GetTransform ******");
				Atom atom = SuperController.singleton.GetAtomByUid(atomName);
				// SuperController.LogMessage("****** ControlEntryAnchored.GetTransform End ******");
				return atom?.GetStorableByID(controlName)?.transform;
			}

			public void UpdateInstant()
			{
				float dampingTime = myDampingTime;
				myDampingTime = 0.0f;
				Update();
				myDampingTime = dampingTime;
			}

			public void Update()
			{
				if (myAnchorMode == ANCHORMODE_WORLD)
				{
					myEntry = myAnchorOffset;
				}
				else
				{
					ControlEntry anchor;
					if (myAnchorMode == ANCHORMODE_SINGLE)
					{
						if (myAnchorATransform == null)
							return;
						anchor.myPosition = myAnchorATransform.position;
						anchor.myRotation = myAnchorATransform.rotation;
					}
					else
					{
						if (myAnchorATransform == null || myAnchorBTransform == null)
							return;
						anchor.myPosition = Vector3.LerpUnclamped(myAnchorATransform.position, myAnchorBTransform.position, myBlendRatio);
						anchor.myRotation = Quaternion.SlerpUnclamped(myAnchorATransform.rotation, myAnchorBTransform.rotation, myBlendRatio);
					}
					anchor.myPosition = anchor.myPosition + anchor.myRotation * myAnchorOffset.myPosition;
					anchor.myRotation = anchor.myRotation * myAnchorOffset.myRotation;

					if (myDampingTime >= 0.001f)
					{
						float t = Mathf.Clamp01(Time.deltaTime / myDampingTime);
						myEntry.myPosition = Vector3.LerpUnclamped(myEntry.myPosition, anchor.myPosition, t);
						myEntry.myRotation = Quaternion.SlerpUnclamped(myEntry.myRotation, anchor.myRotation, t);
					}
					else
					{
						myEntry = anchor;
					}
				}
			}

			public void Capture(Vector3 position, Quaternion rotation)
			{
				// SuperController.LogMessage("****** ControlEntryAnchored.Capture ******");
				myEntry.myPosition = position;
				myEntry.myRotation = rotation;

				if (myAnchorMode == ANCHORMODE_WORLD)
				{
					myAnchorOffset.myPosition = position;
					myAnchorOffset.myRotation = rotation;
				}
				else
				{
					ControlEntry root;
					if (myAnchorMode == ANCHORMODE_SINGLE)
					{
						if (myAnchorATransform == null){
							// SuperController.LogMessage("****** ControlEntryAnchored.Capture End 1 ******");
							return;
						}
						root.myPosition = myAnchorATransform.position;
						root.myRotation = myAnchorATransform.rotation;
					}
					else
					{
						if (myAnchorATransform == null || myAnchorBTransform == null){
							// SuperController.LogMessage("****** ControlEntryAnchored.Capture End 2 ******");
							return;
						}
						root.myPosition = Vector3.LerpUnclamped(myAnchorATransform.position, myAnchorBTransform.position, myBlendRatio);
						root.myRotation = Quaternion.SlerpUnclamped(myAnchorATransform.rotation, myAnchorBTransform.rotation, myBlendRatio);
					}

					myAnchorOffset.myPosition = Quaternion.Inverse(root.myRotation) * (position - root.myPosition);
					myAnchorOffset.myRotation = Quaternion.Inverse(root.myRotation) * rotation;
				}
				// SuperController.LogMessage("****** ControlEntryAnchored.Capture End 3 ******");
			}
		}


		private class MorphCapture
		{
			public string mySID;
			public DAZMorph myMorph;
			public DAZCharacterSelector.Gender myGender;
			private float[] myTransition = new float[MAX_STATES];
			private int myEntryCount = 0;
			public bool myApply = true;

			// used when adding a capture
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector.Gender gender, DAZMorph morph)
			{
				// SuperController.LogMessage("****** MorphCapture.MorphCapture ******");
				myMorph = morph;
				myGender = gender;
				mySID = plugin.GenerateMorphsSID(gender == DAZCharacterSelector.Gender.Female);
				// SuperController.LogMessage("****** MorphCapture.MorphCapture End ******");
			}

			// legacy handling of old qualifiedName where the order was reversed
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector geometry, string oldQualifiedName)
			{
				// SuperController.LogMessage("****** MorphCapture.MorphCapture 2 ******");
				bool isFemale = oldQualifiedName.StartsWith("Female#");
				if (!isFemale && !oldQualifiedName.StartsWith("Male#")){
					// SuperController.LogMessage("****** MorphCapture.MorphCapture 2 End 1 ******");
					return;
				}
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				string morphUID = oldQualifiedName.Substring(isFemale ? 7 : 5);
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;

				mySID = plugin.GenerateMorphsSID(isFemale);
				// SuperController.LogMessage("****** MorphCapture.MorphCapture 2 End 2 ******");
			}

			// legacy handling before there were ShortIDs
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector geometry, string morphUID, bool isFemale)
			{
				// SuperController.LogMessage("****** MorphCapture.MorphCapture 3 ******");
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;

				mySID = plugin.GenerateMorphsSID(isFemale);
				// SuperController.LogMessage("****** MorphCapture.MorphCapture 3 End ******");
			}

			// used when loading from JSON
			public MorphCapture(DAZCharacterSelector geometry, string morphUID, string morphSID)
			{
				// SuperController.LogMessage("****** MorphCapture.MorphCapture 4 ******");
				bool isFemale = morphSID.Length > 0 && morphSID[0] == 'F';
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;
				mySID = morphSID;
				// SuperController.LogMessage("****** MorphCapture.MorphCapture 4 End ******");
			}

			public void CaptureEntry(State state)
			{
				// SuperController.LogMessage("****** MorphCapture.CaptureEntry ******");
				state.myMorphEntries[this] = myMorph.morphValue;
				// SuperController.LogMessage("****** MorphCapture.CaptureEntry End ******");
			}

			public void SetTransition(List<State> states, int entryCount)
			{
				// SuperController.LogMessage("****** MorphCapture.SetTransition ******");
				myEntryCount = entryCount;
				bool identical = true;
				float morphValue = myMorph.morphValue;
				for (int i=0; i<myEntryCount; ++i)
				{
					if (!states[i].myMorphEntries.TryGetValue(this, out myTransition[i]))
					{
						CaptureEntry(states[i]);
						myTransition[i] = morphValue;
					}
					else
					{
						identical &= (myTransition[i] == morphValue);
					}
				}
				if (identical)
					myEntryCount = 0; // nothing to do, save some performance
				// SuperController.LogMessage("****** MorphCapture.SetTransition End ******");
			}

			public void UpdateTransition(float t)
			{
				// SuperController.LogMessage("****** MorphCapture.UpdateTransition ******");
				if (!myApply){
					// SuperController.LogMessage("****** MorphCapture.UpdateTransition End 1 ******");
					return;
				}

				switch (myEntryCount)
				{
					case 4:
						myMorph.morphValue = EvalBezierCubic(t);
						break;
					case 3:
						myMorph.morphValue = EvalBezierQuadratic(t);
						break;
					case 2:
						myMorph.morphValue = EvalBezierLinear(t);
						break;
					default:
						myMorph.morphValue = myTransition[0];
						break;
				}
				// SuperController.LogMessage("****** MorphCapture.UpdateTransition End 2 ******");
			}

			private float EvalBezierLinear(float t)
			{
				// SuperController.LogMessage("****** MorphCapture.EvalBezierLinear ******");
				return Mathf.LerpUnclamped(myTransition[0], myTransition[1], t);
			}

			private float EvalBezierQuadratic(float t)
			{
				// evaluating using Bernstein polynomials
				float s = 1.0f - t;
				SuperController.LogMessage("****** MorphCapture.EvalBezierQuadratic ******");
				return      (s*s) * myTransition[0]
					 + (2.0f*s*t) * myTransition[1]
					 +      (t*t) * myTransition[2];
			}

			private float EvalBezierCubic(float t)
			{
				// evaluating using Bernstein polynomials
				float s = 1.0f - t;
				float t2 = t*t;
				float s2 = s*s;
				// SuperController.LogMessage("****** MorphCapture.EvalBezierCubic ******");
				return      (s*s2) * myTransition[0]
					 + (3.0f*s2*t) * myTransition[1]
					 + (3.0f*s*t2) * myTransition[2]
					 +      (t*t2) * myTransition[3];
			}

			public bool IsValid()
			{
				// SuperController.LogMessage("****** MorphCapture.IsValid ******");
				return myMorph != null && mySID != null;
			}
		}

		private string GenerateMorphsSID(bool isFemale)
		{
			// SuperController.LogMessage("****** MorphCapture.GenerateMorphsSID ******");
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
				if (myMorphCaptures.Find(x => x.mySID == sid) == null){
					// SuperController.LogMessage("****** MorphCapture.GenerateMorphsSID End 1 ******");
					return sid;
				}
			}

			// SuperController.LogMessage("****** MorphCapture.GenerateMorphsSID End 2 ******");
			return null; // you are very lucky, you should play lottery!
		}
	}
}
