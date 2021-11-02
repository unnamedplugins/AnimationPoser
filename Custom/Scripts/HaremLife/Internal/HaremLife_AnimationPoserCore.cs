/* /////////////////////////////////////////////////////////////////////////////////////////////////
Life#IdlePoser v7 by MacGruber.
State-based idle animation system with anchoring.
https://www.patreon.com/MacGruber_Laboratory

Licensed under CC BY-SA after EarlyAccess ended. (see https://creativecommons.org/licenses/by-sa/4.0/)

Version 7 - 2021-02-26
	Control ease in/out duration of a state from the Transition tab.
		(Now using a 3-piece smoothing curve consiting of a linear part in the middle and the integral of SmoothStep at both ends. The resulting combined curve has the benefit of allowing you to tweak ease in/out duration while also providing smooth velocity and continuous acceleration/deceleration.)
	TransitionDuration moved to Transition tab.
	Option for one-way transitions from the Transition tab.	
	Option for states to allow in-group transitions. All states along the transition need to have this enabled, giving your precise control.
	Option to default to using a world anchor (instead of person root control)	
	Debug rendering of paths and transitions can be limited to the selected state.
	Increased amount of state groups from 8 to 12.	
	Listing hidden atoms in the Anchor popups and providing a filter.
	Handling renaming of anchor atoms.
	New PartialStateMask action to allow only change the explicitly mentioned groups in the mask.
	Default to world anchor for non-person atoms to prevent self-reference.
	Default to 'control' capture on non-person atoms.
	Fixed issue with Anchors not being updated/applied correctly.
	Fixed issue with SetStateMask that could not be cleared, you can set it to "Clear" now.
	Fixed SetStateMask interrupting ongoing transitions. 
	Fixed issue with debug info not being updated.
	Fixed issue with UI alignment in the state menu.
	Fixed debug rendering render order and reduced performance overhead.
	Fixed issue that could happen when IdlePoser tried to change it's own state through triggers.
	Changed default directory for Pose saves to 'Saves/PluginData/IdlePoser' to comply with VaM's new plugin guideline and avoid the security warning.	
		(Note that you may have to manually move your saves poses, if you have any.)
	Folder is now created when missing.
	Updated MacGruber_Utils.cs

Version 6 - 2020-12-05
	Implemented triggers at beging and end of the transtion, both for entering and exiting a state.
	Implemented external control triggers: SwitchState, SetStateMask and TriggerSync.
	Added option for InfiniteDuration for states.
	Updated MacGruber.Utils and UI tech overhaul.

Version 5 - 2020-08-25
	Fixing morph captures for VaM 1.20.
	Requiring VaM 1.20 now.

Version 4 - 2020-07-19
	Improve curve debug render, now with colors! Also added a color legend.
	Improved Bezier evaluation (performance).
	UI improvement: Menu tab bar replaces the dropdown.
	UI improvement: InputField with Label
	Implemented animation pause.
	Implemented option to freeze captures.
	Various bugfixes/polish.

Version 3 - 2020-06-21
	Implemented curve debug rendering
	Implemented Morph Capture
	UI improvement: Label with X button.
	Implemented state groups.
	Implemented intermediate points to connect paths.
	Enforce two-way transitions
	Implemented load/save as part of the scene.
	Lots of bugfixes/cleanup.

Version 2 - 2020-05-25
	Hotfix file reference issue.

Version 1 - 2020-05-24
	Initial prototype release.

///////////////////////////////////////////////////////////////////////////////////////////////// */

#if !VAM_GT_1_20
	#error IdlePoser requires VaM 1.20 or newer!
#endif

using UnityEngine;
using UnityEngine.Events;
using System;
using System.Collections.Generic;
using SimpleJSON;
using System.ComponentModel;

namespace MacGruber
{
	public partial class IdlePoser : MVRScript
	{
		private const int MAX_STATES = 4;
		private static readonly int[] DISTANCE_SAMPLES = new int[] { 0, 0, 0, 11, 20};

		private const int STATETYPE_REGULARSTATE = 0;
		private const int STATETYPE_CONTROLPOINT = 1;
		private const int STATETYPE_INTERMEDIATE = 2;
		private const int NUM_STATETYPES = 3;
		
		private const float DEFAULT_TRANSITION_DURATION = 0.1f;
		private const float DEFAULT_BLEND_DURATION = 0.2f;
		private const float DEFAULT_EASEIN_DURATION = 0.0f;
		private const float DEFAULT_EASEOUT_DURATION = 0.0f;
		private const float DEFAULT_PROBABILITY = 0.5f;
		private const float DEFAULT_WAIT_DURATION_MIN = 0.0f;
		private const float DEFAULT_WAIT_DURATION_MAX = 0.0f;
		private const float DEFAULT_ANCHOR_BLEND_RATIO = 0.5f;
		private const float DEFAULT_ANCHOR_DAMPING_TIME = 0.2f;

		private Dictionary<string, Animation> myAnimations = new Dictionary<string, Animation>();
		private List<ControlCapture> myControlCaptures = new List<ControlCapture>();
		private List<MorphCapture> myMorphCaptures = new List<MorphCapture>();
		private List<State> myTransition = new List<State>(8);
		private List<State> myCurrentTransition = new List<State>(MAX_STATES);
		private static Animation myCurrentAnimation;
		private static Layer myCurrentLayer;
		private static State myCurrentState;
		private State myNextState;

		private static bool myPlayMode = false;
		private static bool myPaused = false;
		private bool myNeedRefresh = false;
		private bool myWasLoading = true;

		private JSONStorableString mySwitchAnimation;
		private JSONStorableString mySwitchLayer;
		private JSONStorableString mySwitchState;

		public override void Init()
		{
			myWasLoading = true;

			InitUI();

			mySwitchAnimation = new JSONStorableString("SwitchAnimation", "", SwitchAnimationAction);
			mySwitchAnimation.isStorable = mySwitchAnimation.isRestorable = false;
			RegisterString(mySwitchAnimation);

			mySwitchLayer = new JSONStorableString("SwitchLayer", "", SwitchLayerAction);
			mySwitchLayer.isStorable = mySwitchLayer.isRestorable = false;
			RegisterString(mySwitchLayer);

			mySwitchState = new JSONStorableString("SwitchState", "", SwitchStateAction);
			mySwitchState.isStorable = mySwitchState.isRestorable = false;
			RegisterString(mySwitchState);

			Utils.SetupAction(this, "TriggerSync", TriggerSyncAction);

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

		private void TriggerSyncAction()
		{
			foreach (var layer in myCurrentAnimation.myLayers)
				layer.Value.TriggerSyncAction();
		}

		private Animation CreateAnimation(string name)
		{
			Animation a = new Animation(name) {
				// myWaitDurationMin = 0.0f,
				// myWaitDurationMax = 0.0f,
				// myTransitionDuration = 0.1f,
				// myStateType = STATETYPE_REGULARSTATE
			};
			// CaptureState(s);
			// if(myCurrentState != null) {
			// 	setCaptureDefaults(s, myCurrentState);
			// }
			myAnimations[name] = a;
			return a;
		}

		private Layer CreateLayer(string name)
		{
			Layer l = new Layer(name) {
				// myWaitDurationMin = 0.0f,
				// myWaitDurationMax = 0.0f,
				// myTransitionDuration = 0.1f,
				// myStateType = STATETYPE_REGULARSTATE
			};
			// CaptureState(s);
			// if(myCurrentState != null) {
			// 	setCaptureDefaults(s, myCurrentState);
			// }
			myCurrentAnimation.myLayers[name] = l;
			return l;
		}

		private State CreateState(string name)
		{
			State s = new State(this, name) {
				myWaitDurationMin = DEFAULT_WAIT_DURATION_MIN,
				myWaitDurationMax = DEFAULT_WAIT_DURATION_MAX,
				myTransitionDuration = DEFAULT_TRANSITION_DURATION,
				myStateType = STATETYPE_REGULARSTATE
			};
			CaptureState(s);
			if(myCurrentLayer.myCurrentState != null) {
				setCaptureDefaults(s, myCurrentLayer.myCurrentState);
			}
			myCurrentLayer.myStates[name] = s;
			return s;
		}

		private void SetAnimation(Animation animation)
		{
			myCurrentAnimation = animation;
		}

		private void SetLayer(Layer layer)
		{
			myCurrentLayer = layer;
		}

		private void CaptureState(State state)
		{
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].CaptureEntry(state);
			for (int i=0; i<myCurrentLayer.myMorphCaptures.Count; ++i)
				myCurrentLayer.myMorphCaptures[i].CaptureEntry(state);
		}

		private void setCaptureDefaults(State state, State oldState)
		{
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].setDefaults(state, oldState);
			// for (int i=0; i<myMorphCaptures.Count; ++i)
			// 	myMorphCaptures[i].setDefaults(state, oldState);
		}

		private void SwitchAnimationAction(string v)
		{
			mySwitchAnimation.valNoCallback = string.Empty;

			Animation animation;
			myAnimations.TryGetValue(v, out animation);
			SetAnimation(animation);

			List<string> layers = animation.myLayers.Keys.ToList();
			layers.Sort();
			if(layers.Count > 0) {
				Layer layer;
				animation.myLayers.TryGetValue(layers[0], out layer);
				SetLayer(layer);

				List<string> states = layer.myStates.Keys.ToList();
				states.Sort();
				if(states.Count > 0) {
					State state;
					layer.myStates.TryGetValue(states[0], out state);
					layer.SetBlendTransition(state);
				}
			}
		}

		private void SwitchLayerAction(string v)
		{
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
		}

		private void SwitchStateAction(string v)
		{
			mySwitchState.valNoCallback = string.Empty;

			State state;
			if (myCurrentLayer.myStates.TryGetValue(v, out state))
				myCurrentLayer.SetBlendTransition(state);
			else
				SuperController.LogError("IdlePoser: Can't switch to unknown state '"+v+"'!");
		}

		private void Update()
		{
			bool isLoading = SuperController.singleton.isLoading;
			bool isOrWasLoading = isLoading || myWasLoading;
			myWasLoading = isLoading;
			if (isOrWasLoading)
				return;

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

		// private JSONClass SaveAnimation()
		// {
		// 	JSONClass jc = new JSONClass();

		// 	// save info
		// 	JSONClass info = new JSONClass();
		// 	info["Format"] = "MacGruber.Life.IdlePoser";
		// 	info["Version"].AsInt = 7;
		// 	string creatorName = UserPreferences.singleton.creatorName;
		// 	if (string.IsNullOrEmpty(creatorName))
		// 		creatorName = "Unknown";
		// 	info["Author"] = creatorName;
		// 	jc["Info"] = info;

		// 	// save settings
		// 	if (myCurrentState != null)
		// 		jc["InitialState"] = myCurrentState.myName;
		// 	jc["Paused"].AsBool = myPlayPaused.val;
		// 	jc["DefaultToWorldAnchor"].AsBool = myOptionsDefaultToWorldAnchor.val;

		// 	JSONArray anims = new JSONArray();

		// 	foreach(var an in myAnimations)
		// 	{
		// 		Animation animation = an.Value;
		// 		JSONClass anim = new JSONClass();
		// 		anim["Name"] = animation.myName;
		// 		// save captures
		// 		if (animation.myControlCaptures.Count > 0)
		// 		{
		// 			JSONArray cclist = new JSONArray();
		// 			for (int i=0; i<animation.myControlCaptures.Count; ++i)
		// 			{
		// 				ControlCapture cc = animation.myControlCaptures[i];
		// 				JSONClass ccclass = new JSONClass();
		// 				ccclass["Name"] = cc.myName;
		// 				ccclass["ApplyPos"].AsBool = cc.myApplyPosition;
		// 				ccclass["ApplyRot"].AsBool = cc.myApplyRotation;
		// 				cclist.Add("", ccclass);
		// 			}
		// 			anim["ControlCaptures"] = cclist;
		// 		}
		// 		if (myCurrentAnimation.myMorphCaptures.Count > 0)
		// 		{
		// 			JSONArray mclist = new JSONArray();
		// 			for (int i=0; i<myCurrentAnimation.myMorphCaptures.Count; ++i)
		// 			{
		// 				MorphCapture mc = myCurrentAnimation.myMorphCaptures[i];
		// 				JSONClass mcclass = new JSONClass();
		// 				mcclass["Name"] = mc.myName;
		// 				mcclass["Apply"].AsBool = mc.myApply;
		// 				mclist.Add("", mcclass);
		// 			}
		// 			anim["MorphCaptures"] = mclist;
		// 		}

		// 		// save states
		// 		JSONArray slist = new JSONArray();
		// 		foreach (var s in animation.myStates)
		// 		{
		// 			State state = s.Value;
		// 			JSONClass st = new JSONClass();
		// 			st["Name"] = state.myName;
		// 			st["WaitInfiniteDuration"].AsBool = state.myWaitInfiniteDuration;
		// 			st["WaitForSync"].AsBool = state.myWaitForSync;
		// 			st["AllowInGroupT"].AsBool = state.myAllowInGroupTransition;
		// 			st["WaitDurationMin"].AsFloat = state.myWaitDurationMin;
		// 			st["WaitDurationMax"].AsFloat = state.myWaitDurationMax;
		// 			st["TransitionDuration"].AsFloat = state.myTransitionDuration;
		// 			st["EaseInDuration"].AsFloat = state.myEaseInDuration;
		// 			st["EaseOutDuration"].AsFloat = state.myEaseOutDuration;
		// 			st["Probability"].AsFloat = state.myProbability;
		// 			st["StateType"].AsInt = state.myStateType;
		// 			st["StateGroup"].AsInt = state.myStateGroup;

		// 			JSONArray tlist = new JSONArray();
		// 			for (int i=0; i<state.myTransitions.Count; ++i)
		// 				tlist.Add("", state.myTransitions[i].myName);
		// 			st["Transitions"] = tlist;

		// 			if (state.myControlEntries.Count > 0)
		// 			{
		// 				JSONClass celist = new JSONClass();
		// 				foreach (var e in state.myControlEntries)
		// 				{
		// 					ControlEntryAnchored ce = e.Value;
		// 					JSONClass ceclass = new JSONClass();
		// 					ceclass["PX"].AsFloat = ce.myAnchorOffset.myPosition.x;
		// 					ceclass["PY"].AsFloat = ce.myAnchorOffset.myPosition.y;
		// 					ceclass["PZ"].AsFloat = ce.myAnchorOffset.myPosition.z;
		// 					Vector3 rotation = ce.myAnchorOffset.myRotation.eulerAngles;
		// 					ceclass["RX"].AsFloat = rotation.x;
		// 					ceclass["RY"].AsFloat = rotation.y;
		// 					ceclass["RZ"].AsFloat = rotation.z;
		// 					ceclass["AnchorMode"].AsInt = ce.myAnchorMode;
		// 					if (ce.myAnchorMode >= ControlEntryAnchored.ANCHORMODE_SINGLE)
		// 					{
		// 						ceclass["DampingTime"].AsFloat = ce.myDampingTime;
		// 						ceclass["AnchorAAtom"] = ce.myAnchorAAtom;
		// 						if (containingAtom.uid.Equals(ce.myAnchorAAtom))
		// 						{
		// 							ceclass["AnchorAAtom"] = "[Self]";
		// 						}
		// 						ceclass["AnchorAControl"] = ce.myAnchorAControl;
		// 					}
		// 					if (ce.myAnchorMode == ControlEntryAnchored.ANCHORMODE_BLEND)
		// 					{
		// 						ceclass["AnchorBAtom"] = ce.myAnchorBAtom;
		// 						ceclass["AnchorBControl"] = ce.myAnchorBControl;
		// 						ceclass["BlendRatio"].AsFloat = ce.myBlendRatio;
		// 					}
		// 					celist[e.Key.myName] = ceclass;
		// 				}
		// 				st["ControlEntries"] = celist;
		// 			}

		// 			if (state.myMorphEntries.Count > 0)
		// 			{
		// 				JSONClass melist = new JSONClass();
		// 				foreach (var e in state.myMorphEntries)
		// 				{
		// 					melist[e.Key.myName].AsFloat = e.Value;
		// 				}
		// 				st["MorphEntries"] = melist;
		// 			}

		// 			st[state.EnterBeginTrigger.Name] = state.EnterBeginTrigger.GetJSON(base.subScenePrefix);
		// 			st[state.EnterEndTrigger.Name] = state.EnterEndTrigger.GetJSON(base.subScenePrefix);
		// 			st[state.ExitBeginTrigger.Name] = state.ExitBeginTrigger.GetJSON(base.subScenePrefix);
		// 			st[state.ExitEndTrigger.Name] = state.ExitEndTrigger.GetJSON(base.subScenePrefix);

		// 			for(int k = 0; k < st[state.EnterBeginTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.EnterBeginTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == containingAtom.uid)
		// 				{
		// 					action["receiverAtom"] = "[Self]";
		// 				}
		// 			}
		// 			for(int k = 0; k < st[state.EnterEndTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.EnterEndTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == containingAtom.uid)
		// 				{
		// 					action["receiverAtom"] = "[Self]";
		// 				}
		// 			}
		// 			for(int k = 0; k < st[state.ExitBeginTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.ExitBeginTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == containingAtom.uid)
		// 				{
		// 					action["receiverAtom"] = "[Self]";
		// 				}
		// 			}
		// 			for(int k = 0; k < st[state.ExitEndTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.ExitEndTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == containingAtom.uid)
		// 				{
		// 					action["receiverAtom"] = "[Self]";
		// 				}
		// 			}

		// 			slist.Add("", st);
		// 		}
		// 		anim["States"] = slist;
		// 		anims.Add("", anim);
		// 	}

		// 	jc["Animations"] = anims;

		// 	return jc;
		// }

		// private void LoadAnimation(JSONClass jc)
		// {
		// 	// reset
		// 	// foreach (var s in myCurrentAnimation.myStates)
		// 	// {
		// 	// 	State state = s.Value;
		// 	// 	state.EnterBeginTrigger.Remove();
		// 	// 	state.EnterEndTrigger.Remove();
		// 	// 	state.ExitBeginTrigger.Remove();
		// 	// 	state.ExitEndTrigger.Remove();
		// 	// }

		// 	// myCurrentAnimation.myControlCaptures.Clear();
		// 	// myMorphCaptures.Clear();
		// 	// myCurrentAnimation.myStates.Clear();
		// 	// myCurrentState = null;
		// 	// myNextState = null;
		// 	// myBlendState.myControlEntries.Clear();
		// 	// myBlendState.myMorphEntries.Clear();
		// 	// myClock = 0.0f;

		// 	// load info
		// 	int version = jc["Info"].AsObject["Version"].AsInt;

		// 	// load captures
		// 	JSONArray anims = jc["Animations"].AsArray;
		// 	for (int l=0; l<anims.Count; ++l)
		// 	{
		// 		JSONClass anim = anims[l].AsObject;
		// 		Animation myCurrentAnimation = CreateAnimation(anim["Name"]);
		// 		if (anim.HasKey("ControlCaptures"))
		// 		{
		// 			JSONArray cclist = anim["ControlCaptures"].AsArray;
		// 			for (int i=0; i<cclist.Count; ++i)
		// 			{
		// 				ControlCapture cc;
		// 				if (version <= 2)
		// 				{
		// 					// handling legacy
		// 					cc = new ControlCapture(this, cclist[i].Value);
		// 				}
		// 				else
		// 				{
		// 					JSONClass ccclass = cclist[i].AsObject;
		// 					cc = new ControlCapture(this, ccclass["Name"]);
		// 					cc.myApplyPosition = ccclass["ApplyPos"].AsBool;
		// 					cc.myApplyRotation = ccclass["ApplyRot"].AsBool;
		// 				}

		// 				if (cc.IsValid())
		// 					myCurrentAnimation.myControlCaptures.Add(cc);
		// 			}
		// 		}
		// 		if (anim.HasKey("MorphCaptures"))
		// 		{
		// 			JSONArray mclist = anim["MorphCaptures"].AsArray;
		// 			DAZCharacterSelector geometry = containingAtom.GetStorableByID("geometry") as DAZCharacterSelector;
		// 			for (int i=0; i<mclist.Count; ++i)
		// 			{
		// 				MorphCapture mc;
		// 				if (version <= 2)
		// 				{
		// 					// handling legacy
		// 					mc = new MorphCapture(geometry, mclist[i].Value);
		// 				}
		// 				else
		// 				{
		// 					JSONClass mcclass = mclist[i].AsObject;
		// 					mc = new MorphCapture(geometry, mcclass["Name"]);
		// 					mc.myApply = mcclass["Apply"].AsBool;
		// 				}

		// 				if (mc.IsValid())
		// 					myCurrentAnimation.myMorphCaptures.Add(mc);
		// 			}
		// 		}

		// 		// load states
		// 		JSONArray slist = anim["States"].AsArray;
		// 		for (int i=0; i<slist.Count; ++i)
		// 		{
		// 			// load state
		// 			JSONClass st = slist[i].AsObject;
		// 			State state;
		// 			if (version == 1)
		// 			{
		// 				// handling legacy
		// 				state = new State(this, st["Name"]) {
		// 					myWaitDurationMin = st["WaitDurationMin"].AsFloat,
		// 					myWaitDurationMax = st["WaitDurationMax"].AsFloat,
		// 					myTransitionDuration = st["TransitionDuration"].AsFloat,
		// 					myProbability = DEFAULT_PROBABILITY,
		// 					myStateType = st["IsTransition"].AsBool ? STATETYPE_CONTROLPOINT : STATETYPE_REGULARSTATE,
		// 					myStateGroup = 0
		// 				};
		// 			}
		// 			else
		// 			{
		// 				state = new State(this, st["Name"]) {
		// 					myWaitInfiniteDuration = st["WaitInfiniteDuration"].AsBool,
		// 					myWaitForSync = st["WaitForSync"].AsBool,
		// 					myAllowInGroupTransition = st["AllowInGroupT"].AsBool,
		// 					myWaitDurationMin = st["WaitDurationMin"].AsFloat,
		// 					myWaitDurationMax = st["WaitDurationMax"].AsFloat,
		// 					myTransitionDuration = st["TransitionDuration"].AsFloat,
		// 					myEaseInDuration = st.HasKey("EaseInDuration") ? st["EaseInDuration"].AsFloat : DEFAULT_EASEIN_DURATION,
		// 					myEaseOutDuration = st.HasKey("EaseOutDuration") ? st["EaseOutDuration"].AsFloat : DEFAULT_EASEOUT_DURATION,
		// 					myProbability = st["Probability"].AsFloat,
		// 					myStateType = st["StateType"].AsInt,
		// 					myStateGroup = st["StateGroup"].AsInt
		// 				};
		// 			}

		// 			for(int k = 0; k < st[state.EnterBeginTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.EnterBeginTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == "[Self]")
		// 				{
		// 					action["receiverAtom"] = containingAtom.uid;
		// 				}
		// 			}
		// 			for(int k = 0; k < st[state.EnterEndTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.EnterEndTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == "[Self]")
		// 				{
		// 					action["receiverAtom"] = containingAtom.uid;
		// 				}
		// 			}
		// 			for(int k = 0; k < st[state.ExitBeginTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.ExitBeginTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == "[Self]")
		// 				{
		// 					action["receiverAtom"] = containingAtom.uid;
		// 				}
		// 			}
		// 			for(int k = 0; k < st[state.ExitEndTrigger.Name]["startActions"].Count; k++)
		// 			{
		// 				SimpleJSON.JSONNode action = st[state.ExitEndTrigger.Name]["startActions"][k];
		// 				if (action["receiverAtom"].Value == "[Self]")
		// 				{
		// 					action["receiverAtom"] = containingAtom.uid;
		// 				}
		// 			}

		// 			state.EnterBeginTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);
		// 			state.EnterEndTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);
		// 			state.ExitBeginTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);
		// 			state.ExitEndTrigger.RestoreFromJSON(st, base.subScenePrefix, base.mergeRestore, true);

		// 			if (myCurrentAnimation.myStates.ContainsKey(state.myName))
		// 				continue;
		// 			myCurrentAnimation.myStates[state.myName] = state;

		// 			// load control captures
		// 			if (myCurrentAnimation.myControlCaptures.Count > 0)
		// 			{
		// 				JSONClass celist = st["ControlEntries"].AsObject;
		// 				foreach (string ccname in celist.Keys)
		// 				{
		// 					ControlCapture cc = myCurrentAnimation.myControlCaptures.Find(x => x.myName == ccname);
		// 					if (cc == null)
		// 						continue;

		// 					JSONClass ceclass = celist[ccname].AsObject;
		// 					ControlEntryAnchored ce = new ControlEntryAnchored(this, ccname);
		// 					ce.myAnchorOffset.myPosition.x = ceclass["PX"].AsFloat;
		// 					ce.myAnchorOffset.myPosition.y = ceclass["PY"].AsFloat;
		// 					ce.myAnchorOffset.myPosition.z = ceclass["PZ"].AsFloat;
		// 					Vector3 rotation;
		// 					rotation.x = ceclass["RX"].AsFloat;
		// 					rotation.y = ceclass["RY"].AsFloat;
		// 					rotation.z = ceclass["RZ"].AsFloat;
		// 					ce.myAnchorOffset.myRotation.eulerAngles = rotation;
		// 					ce.myAnchorMode = ceclass["AnchorMode"].AsInt;
		// 					if (ce.myAnchorMode >= ControlEntryAnchored.ANCHORMODE_SINGLE)
		// 					{
		// 						ce.myDampingTime = ceclass["DampingTime"].AsFloat;
		// 						ce.myAnchorAAtom = ceclass["AnchorAAtom"].Value;
		// 						ce.myAnchorAControl = ceclass["AnchorAControl"].Value;
								
		// 						if (ce.myAnchorAAtom == "[Self]") // legacy
		// 							ce.myAnchorAAtom = containingAtom.uid;
		// 					}
		// 					if (ce.myAnchorMode == ControlEntryAnchored.ANCHORMODE_BLEND)
		// 					{
		// 						ce.myAnchorBAtom = ceclass["AnchorBAtom"].Value;
		// 						ce.myAnchorBControl = ceclass["AnchorBControl"].Value;
		// 						ce.myBlendRatio = ceclass["BlendRatio"].AsFloat;
								
		// 						if (ce.myAnchorBAtom == "[Self]") // legacy
		// 							ce.myAnchorBAtom = containingAtom.uid;
		// 					}
		// 					ce.Initialize();

		// 					state.myControlEntries.Add(cc, ce);
		// 				}
		// 				for (int j=0; j<myCurrentAnimation.myControlCaptures.Count; ++j)
		// 				{
		// 					if (!state.myControlEntries.ContainsKey(myCurrentAnimation.myControlCaptures[j]))
		// 						myCurrentAnimation.myControlCaptures[j].CaptureEntry(state);
		// 				}
		// 			}

		// 			// load morph captures
		// 			if (myCurrentAnimation.myMorphCaptures.Count > 0)
		// 			{
		// 				JSONClass melist = st["MorphEntries"].AsObject;
		// 				foreach (string mcname in melist.Keys)
		// 				{
		// 					MorphCapture mc = myCurrentAnimation.myMorphCaptures.Find(x => x.myName == mcname);
		// 					if (mc == null)
		// 						continue;
		// 					float me = melist[mcname].AsFloat;
		// 					state.myMorphEntries.Add(mc, me);
		// 				}
		// 				for (int j=0; j<myCurrentAnimation.myMorphCaptures.Count; ++j)
		// 				{
		// 					if (!state.myMorphEntries.ContainsKey(myCurrentAnimation.myMorphCaptures[j]))
		// 						myCurrentAnimation.myMorphCaptures[j].CaptureEntry(state);
		// 				}
		// 			}
		// 		}

		// 		// load transitions
		// 		for (int i=0; i<slist.Count; ++i)
		// 		{
		// 			JSONClass st = slist[i].AsObject;
		// 			State source;
		// 			if (!myCurrentAnimation.myStates.TryGetValue(st["Name"], out source))
		// 				continue;

		// 			JSONArray tlist = st["Transitions"].AsArray;
		// 			for (int j=0; j<tlist.Count; ++j)
		// 			{
		// 				State target;
		// 				if (myCurrentAnimation.myStates.TryGetValue(tlist[j].Value, out target))
		// 					source.myTransitions.Add(target);
		// 			}
		// 		}
		// 	}

		// 	// load settings
		// 	myPlayPaused.valNoCallback = jc.HasKey("Paused") && jc["Paused"].AsBool;
		// 	myPlayPaused.setCallbackFunction(myPlayPaused.val);

		// 	myOptionsDefaultToWorldAnchor.val = jc.HasKey("DefaultToWorldAnchor") && jc["DefaultToWorldAnchor"].AsBool;

		// 	// blend to initial state
		// 	// if (jc.HasKey("InitialState"))
		// 	// {
		// 	// 	State initial;
		// 	// 	if (myCurrentAnimation.myStates.TryGetValue(jc["InitialState"].Value, out initial))
		// 	// 	{
		// 	// 		SetState(initial);
		// 	// 		myMainState.valNoCallback = initial.myName;
		// 	// 	}
		// 	// }
		// }

		private JSONClass SaveAnimations()
		{
			JSONClass jc = new JSONClass();

			// save info
			JSONClass info = new JSONClass();
			info["Format"] = "MacGruber.Life.IdlePoser";
			info["Version"].AsInt = 7;
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

			return jc;
		}

		private void LoadAnimations(JSONClass jc)
		{
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

			// blend to initial state
			// if (jc.HasKey("InitialState"))
			// {
			// 	State initial;
			// 	if (myCurrentAnimation.myStates.TryGetValue(jc["InitialState"].Value, out initial))
			// 	{
			// 		SetState(initial);
			// 		myMainState.valNoCallback = initial.myName;
			// 	}
			// }
		}

		private JSONClass SaveLayer(Layer layerToSave)
		{
			JSONClass jc = new JSONClass();

			// save info
			JSONClass info = new JSONClass();
			info["Format"] = "MacGruber.Life.IdlePoser";
			info["Version"].AsInt = 7;
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
					mcclass["Name"] = mc.myName;
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
							if (containingAtom.uid.Equals(ce.myAnchorAAtom))
							{
								ceclass["AnchorAAtom"] = "[Self]";
							}
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
						melist[e.Key.myName].AsFloat = e.Value;
					}
					st["MorphEntries"] = melist;
				}

				st[state.EnterBeginTrigger.Name] = state.EnterBeginTrigger.GetJSON(base.subScenePrefix);
				st[state.EnterEndTrigger.Name] = state.EnterEndTrigger.GetJSON(base.subScenePrefix);
				st[state.ExitBeginTrigger.Name] = state.ExitBeginTrigger.GetJSON(base.subScenePrefix);
				st[state.ExitEndTrigger.Name] = state.ExitEndTrigger.GetJSON(base.subScenePrefix);

				for(int k = 0; k < st[state.EnterBeginTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.EnterBeginTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == containingAtom.uid)
					{
						action["receiverAtom"] = "[Self]";
					}
				}
				for(int k = 0; k < st[state.EnterEndTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.EnterEndTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == containingAtom.uid)
					{
						action["receiverAtom"] = "[Self]";
					}
				}
				for(int k = 0; k < st[state.ExitBeginTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.ExitBeginTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == containingAtom.uid)
					{
						action["receiverAtom"] = "[Self]";
					}
				}
				for(int k = 0; k < st[state.ExitEndTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.ExitEndTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == containingAtom.uid)
					{
						action["receiverAtom"] = "[Self]";
					}
				}

				slist.Add("", st);
			}
			layer["States"] = slist;

			jc["Layer"] = layer;

			return jc;
		}

		private Layer LoadLayer(JSONClass jc, bool keepName)
		{
			// reset
			// foreach (var s in myCurrentAnimation.myStates)
			// {
			// 	State state = s.Value;
			// 	state.EnterBeginTrigger.Remove();
			// 	state.EnterEndTrigger.Remove();
			// 	state.ExitBeginTrigger.Remove();
			// 	state.ExitEndTrigger.Remove();
			// }

			// myCurrentAnimation.myControlCaptures.Clear();
			// myMorphCaptures.Clear();
			// myCurrentAnimation.myStates.Clear();
			// myCurrentState = null;
			// myNextState = null;
			// myBlendState.myControlEntries.Clear();
			// myBlendState.myMorphEntries.Clear();
			// myClock = 0.0f;

			// load info
			int version = jc["Info"].AsObject["Version"].AsInt;

			// load captures
			JSONClass layer = jc["Layer"].AsObject;

			if(keepName)
				myCurrentLayer = CreateLayer(myCurrentLayer.myName);
			else
				myCurrentLayer = CreateLayer(layer["Name"]);

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
					if (version <= 2)
					{
						// handling legacy
						mc = new MorphCapture(geometry, mclist[i].Value);
					}
					else
					{
						JSONClass mcclass = mclist[i].AsObject;
						mc = new MorphCapture(geometry, mcclass["Name"]);
						mc.myApply = mcclass["Apply"].AsBool;
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

				for(int k = 0; k < st[state.EnterBeginTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.EnterBeginTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == "[Self]")
					{
						action["receiverAtom"] = containingAtom.uid;
					}
				}
				for(int k = 0; k < st[state.EnterEndTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.EnterEndTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == "[Self]")
					{
						action["receiverAtom"] = containingAtom.uid;
					}
				}
				for(int k = 0; k < st[state.ExitBeginTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.ExitBeginTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == "[Self]")
					{
						action["receiverAtom"] = containingAtom.uid;
					}
				}
				for(int k = 0; k < st[state.ExitEndTrigger.Name]["startActions"].Count; k++)
				{
					SimpleJSON.JSONNode action = st[state.ExitEndTrigger.Name]["startActions"][k];
					if (action["receiverAtom"].Value == "[Self]")
					{
						action["receiverAtom"] = containingAtom.uid;
					}
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
						ControlEntryAnchored ce = new ControlEntryAnchored(this, ccname, cc);
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
					foreach (string mcname in melist.Keys)
					{
						MorphCapture mc = myCurrentLayer.myMorphCaptures.Find(x => x.myName == mcname);
						if (mc == null)
							continue;
						float me = melist[mcname].AsFloat;
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

			myOptionsDefaultToWorldAnchor.val = jc.HasKey("DefaultToWorldAnchor") && jc["DefaultToWorldAnchor"].AsBool;

			// blend to initial state
			// if (jc.HasKey("InitialState"))
			// {
			// 	State initial;
			// 	if (myCurrentAnimation.myStates.TryGetValue(jc["InitialState"].Value, out initial))
			// 	{
			// 		SetState(initial);
			// 		myMainState.valNoCallback = initial.myName;
			// 	}
			// }
			SwitchLayerAction(myCurrentLayer.myName);
			return myCurrentLayer;
		}

		// =======================================================================================

		private class Animation 
		{
			public string myName;
			public Dictionary<string, Layer> myLayers = new Dictionary<string, Layer>();

			public Animation(string name)
			{
				myName = name;
			}

			public void Clear()
			{
				foreach (var l in myLayers)
				{
					Layer layer = l.Value;
					layer.Clear();
				}
			}

		}

		private class Layer
		{
			public string myName;
			public Dictionary<string, State> myStates = new Dictionary<string, State>();
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
				myName = name;
			}
			private void CaptureState(State state)
			{
				for (int i=0; i<myControlCaptures.Count; ++i)
					myControlCaptures[i].CaptureEntry(state);
				for (int i=0; i<myMorphCaptures.Count; ++i)
					myMorphCaptures[i].CaptureEntry(state);
			}

			private void SetState(State state)
			{
				myCurrentState = state;
				myNextState = null;

				myClock = 0.0f;
				if (state.myWaitInfiniteDuration)
					myDuration = float.MaxValue;
				else
					myDuration = UnityEngine.Random.Range(state.myWaitDurationMin, state.myWaitDurationMax);
				myCurrentTransition.Clear();
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

			private float Smooth(float a, float b, float d, float t)
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
				
				/*
				// Alternative using SmootherStep (6x^5 - 15x^4 + 10x^3) as a basis instead of SmoothStep
				
				if (t < a)
				{
					float t2 = t*t;
					float a2 = a*a;
					return ( t2/a2 - 3.0f*t/a + 2.5f ) * (t2*t2) / (n*a2*a);
				}
				else if (s >= b)
				{
					return (t - 0.5f*a) / n;
				}
				else
				{					
					float s2 = s*s;
					float b2 = b*b;
					return ( -s2/b2 + 3.0f*s/b - 2.5f ) * (s2*s2) / (n*b2*b) + 1.0f;
				}
				
				*/
			}

			public void UpdateLayer(){
				for (int i=0; i<myTriggerActionsNeedingUpdate.Count; ++i)
					myTriggerActionsNeedingUpdate[i].Update();
				myTriggerActionsNeedingUpdate.RemoveAll(a => !a.timerActive);

				if (myCurrentState == null)
					return;

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
							if(myName == myCurrentLayer.myName)
								myMainState.valNoCallback = myCurrentState.myName;
						
						if (previousState.ExitEndTrigger != null)
							previousState.ExitEndTrigger.Trigger(myTriggerActionsNeedingUpdate);
						if (myCurrentState.EnterEndTrigger != null)
							myCurrentState.EnterEndTrigger.Trigger(myTriggerActionsNeedingUpdate);
					}
					else if (!paused && !myCurrentState.myWaitForSync)
					{
						if (myTransition.Count == 0)
							SetRandomTransition();
						else
							SetTransition();
					}
				}
			}

			private void SetTransition(float duration = -1.0f)
			{
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

				myCurrentTransition.Clear();
				for (int i=0; i<entryCount; ++i)
					myCurrentTransition.Add(myTransition[i]);

				myClock = 0.0f;
				myDuration = (duration < 0) ? d : duration;
				myDuration = Mathf.Max(myDuration, 0.000f);
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
			}

			public void SetRandomTransition()
			{
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
					return;

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
			public void SetBlendTransition(State state, bool debug = false)
			{
				myTransition.Clear();
				myTransition.Add(myCurrentState);
				if (myCurrentState != null)
				{
					List<State> states = new List<State>(16);
					myNextState = null;
					GatherStates(1, states);
					List<int> indices = new List<int>(4);
					for (int i=0; i<states.Count; ++i)
					{
						if (states[i] == state)
							indices.Add(i);
					}
					if (indices.Count == 0)
					{
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
					myTransition.Add(state);
				}
				
				SetTransition();
			}
			private void GatherStates(int transitionLength, List<State> states)
			{
				State source = myTransition[0];
				State current = myTransition[myTransition.Count-1];
				for (int i=0; i<current.myTransitions.Count; ++i)
				{
					State next = current.myTransitions[i];
					if (myTransition.Contains(next))
						continue;

					if (next.IsRegularState || next == myNextState)
					{
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
			}

			private int GatherTransition(int transitionLength, int selected, int index)
			{
				State source = myTransition[0];
				State current = myTransition[myTransition.Count-1];
				for (int i=0; i<current.myTransitions.Count; ++i)
				{
					State next = current.myTransitions[i];
					if (myTransition.Contains(next))
						continue;

					if (next.IsRegularState || next == myNextState)
					{
						if (!DoAcceptRegularState(source, next))
						{
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
				return index;
			}
			private bool DoAcceptRegularState(State source, State next, bool debugMode = false)
			{
				if (next == myNextState && !debugMode)
					return true;
				if (next.myProbability < 0.01f)
					return false;
				if (source.myStateGroup == 0 || source.myStateGroup != next.myStateGroup)
					return true;
				
				// in-group transition: source.myStateGroup == next.myStateGroup
				if (!source.myAllowInGroupTransition || !next.myAllowInGroupTransition)
					return false;	

				List<State> transition = debugMode ? myDebugTransition : myTransition;
				for (int t=1; t<transition.Count; ++t)
				{
					if (!transition[t].myAllowInGroupTransition)
						return false;
				}
				
				return true;
			}

			private void BlendToRandomState(float duration)
			{
				List<State> possible = new List<State>(myStates.Count);
				foreach (var state in myStates)
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
			}
			public void TriggerSyncAction()
			{
				if (myCurrentState != null && myNextState == null
				&& myClock >= myDuration && !myCurrentState.myWaitInfiniteDuration
				&& myCurrentState.myWaitForSync && !myPaused)
				{
					if (myTransition.Count == 0)
						SetRandomTransition();
					else
						SetTransition();
				}
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

			private State(string name)
			{
				myName = name;
				// do NOT init event triggers
			}

			public State(MVRScript script, string name)
			{
				myName = name;
				EnterBeginTrigger = new EventTrigger(script, "OnEnterBegin", name);
				EnterEndTrigger = new EventTrigger(script, "OnEnterEnd", name);
				ExitBeginTrigger = new EventTrigger(script, "OnExitBegin", name);
				ExitEndTrigger = new EventTrigger(script, "OnExitEnd", name);
			}

			public static State CreateBlendState()
			{
				return new State("BlendState") {
					myWaitDurationMin = 0.0f,
					myWaitDurationMax = 0.0f,
					myTransitionDuration = 0.2f,
					myStateType = STATETYPE_CONTROLPOINT
				};
			}

			public void AssignOutTriggers(State other)
			{
				ExitBeginTrigger = other?.ExitBeginTrigger;
				ExitEndTrigger = other?.ExitEndTrigger;
			}
		}		

		private class ControlCapture
		{
			public string myName;
			private IdlePoser myPlugin;
			public Transform myTransform;
			private ControlEntryAnchored[] myTransition = new ControlEntryAnchored[MAX_STATES];
			private int myEntryCount = 0;
			public bool myApplyPosition = true;
			public bool myApplyRotation = true;

			private static Quaternion[] ourTempQuaternions = new Quaternion[MAX_STATES-1];
			private static float[] ourTempDistances = new float[DISTANCE_SAMPLES[DISTANCE_SAMPLES.Length-1] + 2];

			public ControlCapture(IdlePoser plugin, string control)
			{
				myPlugin = plugin;
				myName = control;
				FreeControllerV3 controller = plugin.containingAtom.GetStorableByID(control) as FreeControllerV3;
				if (controller != null)
					myTransform = controller.transform;
			}

			public void CaptureEntry(State state)
			{
				ControlEntryAnchored entry;
				if (!state.myControlEntries.TryGetValue(this, out entry))
				{
					entry = new ControlEntryAnchored(myPlugin, myName, this);
					entry.Initialize();
					state.myControlEntries[this] = entry;
				}
				entry.Capture(myTransform.position, myTransform.rotation);
			}

			public void setDefaults(State state, State oldState)
			{
				ControlEntryAnchored entry;
				ControlEntryAnchored oldEntry;
				if (!state.myControlEntries.TryGetValue(this, out entry))
					return;
				if (!oldState.myControlEntries.TryGetValue(this, out oldEntry))
					return;
				entry.myAnchorAAtom = oldEntry.myAnchorAAtom;
				entry.myAnchorAControl = oldEntry.myAnchorAControl;
				entry.myAnchorMode = oldEntry.myAnchorMode;
			}

			public void SetTransition(List<State> states, int entryCount)
			{
				myEntryCount = entryCount;
				for (int i=0; i<myEntryCount; ++i)
				{
					if (!states[i].myControlEntries.TryGetValue(this, out myTransition[i]))
					{
						CaptureEntry(states[i]);
						myTransition[i] = states[i].myControlEntries[this];
					}
				}
			}

			public void UpdateTransition(float t)
			{
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
			}

			private float ArcLengthParametrization(float t)
			{
				if (myEntryCount <= 2 || myEntryCount > 4)
					return t;

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
					if (idx == 0)
						return 0.0f;
					else if (idx >= numSamples+2)
						return 1.0f;
					t = Mathf.InverseLerp(ourTempDistances[idx-1], ourTempDistances[idx], t);
					return Mathf.LerpUnclamped((idx-1) / numLines, idx / numLines, t);
				}
				else
				{
					return idx / numLines;
				}
			}

			private Vector3 EvalBezierLinearPosition(float t)
			{
				return Vector3.LerpUnclamped(myTransition[0].myEntry.myPosition, myTransition[1].myEntry.myPosition, t);
			}

			private Vector3 EvalBezierQuadraticPosition(float t)
			{
				// evaluating quadratic Bézier curve using Bernstein polynomials
				float s = 1.0f - t;
				return      (s*s) * myTransition[0].myEntry.myPosition
					 + (2.0f*s*t) * myTransition[1].myEntry.myPosition
					 +      (t*t) * myTransition[2].myEntry.myPosition;
			}

			private Vector3 EvalBezierCubicPosition(float t)
			{
				// evaluating cubic Bézier curve using Bernstein polynomials
				float s = 1.0f - t;
				float t2 = t*t;
				float s2 = s*s;
				return      (s*s2) * myTransition[0].myEntry.myPosition
					 + (3.0f*s2*t) * myTransition[1].myEntry.myPosition
					 + (3.0f*s*t2) * myTransition[2].myEntry.myPosition
					 +      (t*t2) * myTransition[3].myEntry.myPosition;
			}

			private Quaternion EvalBezierLinearRotation(float t)
			{
				return Quaternion.SlerpUnclamped(myTransition[0].myEntry.myRotation, myTransition[1].myEntry.myRotation, t);
			}

			private Quaternion EvalBezierQuadraticRotation(float t)
			{
				// evaluating quadratic Bézier curve using de Casteljau's algorithm
				ourTempQuaternions[0] = Quaternion.SlerpUnclamped(myTransition[0].myEntry.myRotation, myTransition[1].myEntry.myRotation, t);
				ourTempQuaternions[1] = Quaternion.SlerpUnclamped(myTransition[1].myEntry.myRotation, myTransition[2].myEntry.myRotation, t);
				return Quaternion.SlerpUnclamped(ourTempQuaternions[0], ourTempQuaternions[1], t);
			}

			private Quaternion EvalBezierCubicRotation(float t)
			{
				// evaluating cubic Bézier curve using de Casteljau's algorithm
				for (int i=0; i<3; ++i)
					ourTempQuaternions[i] = Quaternion.SlerpUnclamped(myTransition[i].myEntry.myRotation, myTransition[i+1].myEntry.myRotation, t);
				for (int i=0; i<2; ++i)
					ourTempQuaternions[i] = Quaternion.SlerpUnclamped(ourTempQuaternions[i], ourTempQuaternions[i+1], t);
				return Quaternion.SlerpUnclamped(ourTempQuaternions[0], ourTempQuaternions[1], t);
			}

			public void UpdateState(State state)
			{
				ControlEntryAnchored entry;
				if (state.myControlEntries.TryGetValue(this, out entry))
				{
					entry.Update();
					if (myApplyPosition)
						myTransform.position = entry.myEntry.myPosition;
					if (myApplyRotation)
						myTransform.rotation = entry.myEntry.myRotation;
				}
			}

			public bool IsValid()
			{
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
			public const int ANCHORMODE_RELATIVE = 3;

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
			public ControlCapture myControlCapture;
			
			public ControlEntryAnchored(IdlePoser plugin, string control, ControlCapture controlCapture)
			{
				Atom containingAtom = plugin.GetContainingAtom();
				if (plugin.myOptionsDefaultToWorldAnchor.val || containingAtom.type != "Person")
					myAnchorMode = ANCHORMODE_WORLD;
				if(control == "control" && containingAtom.parentAtom != null)
					myAnchorAAtom = myAnchorBAtom = containingAtom.parentAtom.uid;
				else
					myAnchorAAtom = myAnchorBAtom = containingAtom.uid;
				myControlCapture = controlCapture;
			}

			public void Initialize()
			{
				GetTransforms();
				UpdateInstant();
			}

			public void AdjustAnchor()
			{
				GetTransforms();
				Capture(myEntry.myPosition, myEntry.myRotation);				
			}

			private void GetTransforms()
			{
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
			}

			private Transform GetTransform(string atomName, string controlName)
			{
				Atom atom = SuperController.singleton.GetAtomByUid(atomName);
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
					} else if (myAnchorMode == ANCHORMODE_RELATIVE) {
						List<string> states = myCurrentLayer.myStates.Keys.ToList();
						states.Sort();
						State state;
						myCurrentLayer.myStates.TryGetValue(states[0], out state);
						ControlCapture cc = state.myControlEntries.Keys.ToList().Find(ccx => ccx.myName == myControlCapture.myName);
						ControlEntry ce = state.myControlEntries[cc].myEntry;
						anchor.myPosition = ce.myPosition;
						anchor.myRotation = ce.myRotation;
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
						if (myAnchorATransform == null)
							return;
						root.myPosition = myAnchorATransform.position;
						root.myRotation = myAnchorATransform.rotation;
					} else if (myAnchorMode == ANCHORMODE_RELATIVE) {
						List<string> states = myCurrentLayer.myStates.Keys.ToList();
						states.Sort();
						State state;
						myCurrentLayer.myStates.TryGetValue(states[0], out state);
						ControlCapture cc = state.myControlEntries.Keys.ToList().Find(ccx => ccx.myName == myControlCapture.myName);
						ControlEntry ce = state.myControlEntries[cc].myEntry;
						root.myPosition = ce.myPosition;
						root.myRotation = ce.myRotation;
					}
					else
					{
						if (myAnchorATransform == null || myAnchorBTransform == null)
							return;
						root.myPosition = Vector3.LerpUnclamped(myAnchorATransform.position, myAnchorBTransform.position, myBlendRatio);
						root.myRotation = Quaternion.SlerpUnclamped(myAnchorATransform.rotation, myAnchorBTransform.rotation, myBlendRatio);
					}

					myAnchorOffset.myPosition = Quaternion.Inverse(root.myRotation) * (position - root.myPosition);
					myAnchorOffset.myRotation = Quaternion.Inverse(root.myRotation) * rotation;
				}
			}
		}


		private class MorphCapture
		{
			public string myName;
			public DAZMorph myMorph;
			public DAZCharacterSelector.Gender myGender;
			private float[] myTransition = new float[MAX_STATES];
			private int myEntryCount = 0;
			public bool myApply = true;

			public MorphCapture(DAZCharacterSelector.Gender gender, DAZMorph morph)
			{
				myMorph = morph;
				myGender = gender;
				myName = myGender.ToString() + "#" + myMorph.uid;
			}

			public MorphCapture(DAZCharacterSelector geometry, string qualifiedName)
			{
				bool isFemale = qualifiedName.StartsWith("Female#");
				if (!isFemale && !qualifiedName.StartsWith("Male#"))
					return;
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				string morphUID = qualifiedName.Substring(isFemale ? 7 : 5);
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;

				myName = qualifiedName;
			}

			public void CaptureEntry(State state)
			{
				state.myMorphEntries[this] = myMorph.morphValue;
			}

			public void SetTransition(List<State> states, int entryCount)
			{
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
			}

			public void UpdateTransition(float t)
			{
				if (!myApply)
					return;

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
			}

			private float EvalBezierLinear(float t)
			{
				return Mathf.LerpUnclamped(myTransition[0], myTransition[1], t);
			}

			private float EvalBezierQuadratic(float t)
			{
				// evaluating using Bernstein polynomials
				float s = 1.0f - t;
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
				return      (s*s2) * myTransition[0]
					 + (3.0f*s2*t) * myTransition[1]
					 + (3.0f*s*t2) * myTransition[2]
					 +      (t*t2) * myTransition[3];
			}

			public bool IsValid()
			{
				return myMorph != null;
			}
		}

	}
}