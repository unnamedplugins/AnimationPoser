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
		private const int MAX_STATES = 4;
		private static readonly int[] DISTANCE_SAMPLES = new int[] { 0, 0, 0, 11, 20};

		private const int NUM_STATETYPES = 3;
		private List<ControlCapture> myControlCaptures = new List<ControlCapture>();
		private List<MorphCapture> myMorphCaptures = new List<MorphCapture>();

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
		private List<TriggerActionDiscrete> myTriggerActionsNeedingUpdate = new List<TriggerActionDiscrete>();
		private static Animation myCurrentAnimation;
		private static Layer myCurrentLayer;
		private static State myCurrentState;

		private float myClock = 0.0f;
		private static bool myNoValidTransition = false;
		private static bool myPlayMode = false;
		private static bool myPaused = false;
		private static bool myNeedRefresh = false;
		private bool myWasLoading = true;

		private static JSONStorableString mySendMessage;
		private static JSONStorableString myLoadAnimation;
		private static JSONStorableBool myPlayPaused;

		public override void Init()
		{
			myWasLoading = true;
			myClock = 0.0f;

			InitUI();

			// trigger values
			mySendMessage = new JSONStorableString("SendMessage", "", ReceiveMessage);
			mySendMessage.isStorable = mySendMessage.isRestorable = false;
			RegisterString(mySendMessage);

			myPlayPaused = new JSONStorableBool("PlayPause", false, PlayPauseAction);
			myPlayPaused.isStorable = myPlayPaused.isRestorable = false;
			RegisterBool(myPlayPaused);

			myLoadAnimation = new JSONStorableString("LoadAnimation", "", LoadAnimationsAction);
			myLoadAnimation.isStorable = myLoadAnimation.isRestorable = false;
			RegisterString(myLoadAnimation);

			SuperController.singleton.onAtomUIDRenameHandlers += OnAtomRename;
			SimpleTriggerHandler.LoadAssets();
		}

		public void ReceiveMessage(String messageString) {
			mySendMessage.valNoCallback = "";
			foreach(var l in myCurrentAnimation.myLayers) {
				Layer layer = l.Value;
				foreach(var m in layer.myMessages) {
					Message message = m.Value;
					if(message.myMessageString == messageString) {
						State currentState = layer.myCurrentState;
						if(message.mySourceStates.Values.ToList().Contains(currentState)) {
							Transition transition = new Transition(currentState, message);
							layer.SetTransition(transition);
						}
					}
				}
			}
		}

		public void GetAnimations(JSONStorableStringChooser mySyncAnimationList) {
			List<string> animations = new List<string>();
			foreach(var a in myAnimations) {
				animations.Add(a.Value.myName);
			}
			animations.Sort();
			mySyncAnimationList.choices = animations;
		}

		public void GetLayers(object [] methodParams) {
			String animationName = methodParams[0] as String;
			JSONStorableStringChooser mySyncLayerList = methodParams[1] as JSONStorableStringChooser;
			List<string> layers = new List<string>();
			foreach(var l in myAnimations[animationName].myLayers) {
				layers.Add(l.Value.myName);
			}
			layers.Sort();
			mySyncLayerList.choices = layers;
		}

		public void GetStates(object [] methodParams) {
			String animationName = methodParams[0] as String;
			String layerName = methodParams[1] as String;
			JSONStorableStringChooser mySyncStateList = methodParams[2] as JSONStorableStringChooser;
			List<string> states = new List<string>();
			foreach(var s in myAnimations[animationName].myLayers[layerName].myStates) {
				states.Add(s.Value.myName);
			}
			states.Sort();
			mySyncStateList.choices = states;
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

		private Animation CreateAnimation(string name)
		{
			Animation a = new Animation(name);
			myAnimations[name] = a;
			return a;
		}

		private Layer CreateLayer(string name)
		{
			Layer l = new Layer(name);
			myCurrentAnimation.myLayers[name] = l;
			return l;
		}

		private State CreateState(string name)
		{
			State s = new State(this, name) {
				myWaitDurationMin = myDefaultWaitDurationMin.val,
				myWaitDurationMax = myDefaultWaitDurationMax.val,
			};
			CaptureState(s);
			if(myCurrentLayer.myCurrentState != null) {
				setCaptureDefaults(s, myCurrentLayer.myCurrentState);
			}
			myCurrentLayer.myStates[name] = s;
			return s;
		}

		private static void SetAnimation(Animation animation)
		{
			myCurrentAnimation = animation;

			List<string> layers = animation.myLayers.Keys.ToList();
			layers.Sort();
			if(layers.Count > 0) {
				Layer layer;
				foreach (var layerKey in layers) {
					myCurrentAnimation.myLayers.TryGetValue(layerKey, out layer);
					SetLayer(layer);
				}
			}
		}

		private static void SetLayer(Layer layer)
		{
			myCurrentLayer = layer;
			List<string> states = layer.myStates.Keys.ToList();
			states.Sort();
			if(layer.myStates.Count > 0) {
				State state;
				layer.myStates.TryGetValue(states[0], out state);
				layer.SetBlendTransition(state);
			}
		}

		private void CaptureState(State state)
		{
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i){
				myCurrentLayer.myControlCaptures[i].CaptureEntry(state);
			}
			for (int i=0; i<myCurrentLayer.myMorphCaptures.Count; ++i) {
				myCurrentLayer.myMorphCaptures[i].CaptureEntry(state);
			}
		}

		private void setCaptureDefaults(State state, State oldState)
		{
			for (int i=0; i<myCurrentLayer.myControlCaptures.Count; ++i)
				myCurrentLayer.myControlCaptures[i].setDefaults(state, oldState);
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

		private JSONClass SaveAnimations()
		{
			JSONClass jc = new JSONClass();

			// save info
			JSONClass info = new JSONClass();
			info["Format"] = "HaremLife.AnimationPoser";
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

			JSONArray anims = new JSONArray();

			foreach(var an in myAnimations)
			{
				Animation animation = an.Value;
				JSONClass anim = new JSONClass();
				anim["Name"] = animation.myName;
				anim["Speed"].AsFloat = animation.mySpeed;
				JSONArray llist = new JSONArray();
				foreach(var l in animation.myLayers){
					llist.Add("", SaveLayer(l.Value));
				}
				anim["Layers"] = llist;

				// save roles
				if (animation.myRoles.Keys.Count > 0)
				{
					JSONArray rlist = new JSONArray();
					foreach (var r in animation.myRoles)
					{
						Role role = r.Value;
						JSONClass rclass = new JSONClass();
						rclass["Name"] = role.myName;
						if(role.myPerson != null) {
							rclass["Person"] = role.myPerson.name;
						} else {
							rclass["Person"] = "";
						}
						// ccclass["Person"] = r.myPerson;
						rlist.Add("", rclass);
					}
					anim["Roles"] = rlist;
				}
				anims.Add("", anim);
			}

			jc["Animations"] = anims;

			return jc;
		}

		private void LoadAnimations(JSONClass jc)
		{
			// load info
			myAnimations.Clear();
			int version = jc["Info"].AsObject["Version"].AsInt;

			// load captures
			JSONArray anims = jc["Animations"].AsArray;
			for (int l=0; l<anims.Count; ++l)
			{
				JSONClass anim = anims[l].AsObject;
				myCurrentAnimation = CreateAnimation(anim["Name"]);
				myCurrentAnimation.mySpeed = anim["Speed"].AsFloat;
			}

			for (int l=0; l<anims.Count; ++l)
			{
				JSONClass anim = anims[l].AsObject;

				// load roles
				if (anim.HasKey("Roles"))
				{
					JSONArray rlist = anim["Roles"].AsArray;
					for (int i=0; i<rlist.Count; ++i)
					{
						Role r;
						JSONClass rclass = rlist[i].AsObject;
						r = new Role(rclass["Name"]);
						if(rclass["Person"] != "") {
							Atom person = SuperController.singleton.GetAtoms().Find(a => String.Equals(a.name, rclass["Person"]));
							if(person != null) {
								r.myPerson = person;
							}
						}
						myCurrentAnimation.myRoles[r.myName] = r;
					}
				}
			}

			for (int l=0; l<anims.Count; ++l)
			{
				JSONClass anim = anims[l].AsObject;
				if (!myAnimations.TryGetValue(anim["Name"], out myCurrentAnimation))
					continue;
				JSONArray layers = anim["Layers"].AsArray;
				for(int m=0; m<layers.Count; m++)
				{
					JSONClass layer = layers[m].AsObject;
					LoadLayer(layer, false, false);
				}
			}

			for (int l=0; l<anims.Count; ++l)
			{
				JSONClass anim = anims[l].AsObject;
				if (!myAnimations.TryGetValue(anim["Name"], out myCurrentAnimation))
					continue;
				JSONArray layers = anim["Layers"].AsArray;
				for(int m=0; m<layers.Count; m++)
				{
					JSONClass layer = layers[m].AsObject;
					LoadTransitions(layer);
					LoadMessages(layer);
				}
			}

			// load settings
			myPlayPaused.valNoCallback = jc.HasKey("Paused") && jc["Paused"].AsBool;
			myPlayPaused.setCallbackFunction(myPlayPaused.val);

			if (myCurrentAnimation != null)
			{
				myMainAnimation.valNoCallback = myCurrentAnimation.myName;
				myMainAnimation.setCallbackFunction(myCurrentAnimation.myName);
			}
			if (myCurrentLayer != null)
			{
				myMainLayer.valNoCallback = myCurrentLayer.myName;
				myMainLayer.setCallbackFunction(myCurrentLayer.myName);
			}
			if (myCurrentState != null)
			{
				myMainState.valNoCallback = myCurrentState.myName;
				myMainState.setCallbackFunction(myCurrentState.myName);
			}
		}

		private JSONClass SaveLayer(Layer layerToSave)
		{
			JSONClass jc = new JSONClass();
			// save settings
			if (myCurrentState != null)
				jc["InitialState"] = myCurrentState.myName;
			jc["Paused"].AsBool = myPlayPaused.val;

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
				st["IsRootState"].AsBool = state.myIsRootState;
				st["WaitDurationMin"].AsFloat = state.myWaitDurationMin;
				st["WaitDurationMax"].AsFloat = state.myWaitDurationMax;
				st["DefaultDuration"].AsFloat = state.myDefaultDuration;
				st["DefaultEaseInDuration"].AsFloat = state.myDefaultEaseInDuration;
				st["DefaultEaseOutDuration"].AsFloat = state.myDefaultEaseOutDuration;
				st["DefaultProbability"].AsFloat = state.myDefaultProbability;

				JSONArray tlist = new JSONArray();
				for (int i=0; i<state.myTransitions.Count; ++i) {
					Transition transition = state.myTransitions[i];
					JSONClass t = new JSONClass();
					t["SourceState"] = transition.mySourceState.myName;
					t["TargetState"] = transition.myTargetState.myName;
					if(transition.myTargetState.myLayer == transition.mySourceState.myLayer)
						t["TargetLayer"] = "[Self]";
					else
						t["TargetLayer"] = transition.myTargetState.myLayer.myName;
					if(transition.myTargetState.myAnimation == transition.mySourceState.myAnimation)
						t["TargetAnimation"] = "[Self]";
					else
						t["TargetAnimation"] = transition.myTargetState.myAnimation.myName;
					t["Duration"].AsFloat = transition.myDuration;
					t["DurationNoise"].AsFloat = transition.myDurationNoise;
					t["EaseInDuration"].AsFloat = transition.myEaseInDuration;
					t["EaseOutDuration"].AsFloat = transition.myEaseOutDuration;
					t["Probability"].AsFloat = transition.myProbability;

					JSONClass synct = new JSONClass();
					foreach (var syncl in transition.mySyncTargets) {
						synct[syncl.Key.myName] = syncl.Value.myName;
					}
					t["SyncTargets"] = synct;

					JSONClass msgs = new JSONClass();
					foreach (var msg in transition.myMessages) {
						msgs[msg.Key.myName] = msg.Value;
					}
					t["Messages"] = msgs;

					tlist.Add(t);
				}
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
							if(ce.myAnchorAAtom == containingAtom.uid)
								ceclass["AnchorAAtom"] = "[Self]";
							else
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

			// save messages
			JSONArray mlist = new JSONArray();
			foreach(var mm in layerToSave.myMessages)
			{
				Message message = mm.Value;

				JSONClass m = new JSONClass();

				m["Name"] = message.myName;
				m["MessageString"] = message.myMessageString;

				JSONArray srcstlist = new JSONArray();
				foreach(var srcst in message.mySourceStates) {
					JSONClass src = new JSONClass();
					src["Name"] = srcst.Value.myName;
					srcstlist.Add(src);
				}
				m["SourceStates"] = srcstlist;
				m["TargetState"] = message.myTargetState.myName;

				string firstSrcState = message.mySourceStates.Keys.ToList()[0];
				if(message.myTargetState.myLayer == message.mySourceStates[firstSrcState].myLayer)
					m["TargetLayer"] = "[Self]";
				else
					m["TargetLayer"] = message.myTargetState.myLayer.myName;
				if(message.myTargetState.myAnimation == message.mySourceStates[firstSrcState].myAnimation)
					m["TargetAnimation"] = "[Self]";
				else
					m["TargetAnimation"] = message.myTargetState.myAnimation.myName;
				m["Duration"].AsFloat = message.myDuration;
				m["DurationNoise"].AsFloat = message.myDurationNoise;
				m["EaseInDuration"].AsFloat = message.myEaseInDuration;
				m["EaseOutDuration"].AsFloat = message.myEaseOutDuration;
				m["Probability"].AsFloat = message.myProbability;

				JSONClass synct = new JSONClass();
				foreach (var syncl in message.mySyncTargets) {
					synct[syncl.Key.myName] = syncl.Value.myName;
				}
				m["SyncTargets"] = synct;

				mlist.Add(m);
			}
			layer["Messages"] = mlist;

			jc["Layer"] = layer;

			return jc;
		}

		private Layer LoadLayer(JSONClass jc, bool keepName, bool clearStates)
		{
			// reset
			if(myCurrentLayer != null & clearStates){
				if(myCurrentLayer.myStates.Count > 0){
					foreach (var s in myCurrentLayer.myStates)
					{
						State state = s.Value;
						state.EnterBeginTrigger.Remove();
						state.EnterEndTrigger.Remove();
						state.ExitBeginTrigger.Remove();
						state.ExitEndTrigger.Remove();
					}
				}
				myCurrentLayer.myControlCaptures.Clear();
				myCurrentLayer.myMorphCaptures.Clear();
				myCurrentLayer.myStates.Clear();
				myCurrentState = null;
				myClock = 0.0f;
			}

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
					JSONClass ccclass = cclist[i].AsObject;
					cc = new ControlCapture(this, ccclass["Name"]);
					cc.myApplyPosition = ccclass["ApplyPos"].AsBool;
					cc.myApplyRotation = ccclass["ApplyRot"].AsBool;

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
					JSONClass mcclass = mclist[i].AsObject;
					string uid = mcclass["UID"];
					if (uid.EndsWith(".vmi")) // handle custom morphs, resolve VAR packages
						uid = SuperController.singleton.NormalizeLoadPath(uid);
					string sid = mcclass["SID"];
					mc = new MorphCapture(geometry, uid, sid);
					mc.myApply = mcclass["Apply"].AsBool;

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
				State state = new State(this, st["Name"]) {
					myIsRootState = st["IsRootState"].AsBool,
					myWaitDurationMin = st["WaitDurationMin"].AsFloat,
					myWaitDurationMax = st["WaitDurationMax"].AsFloat,
					myDefaultDuration = st["DefaultDuration"].AsFloat,
					myDefaultEaseInDuration = st["DefaultEaseInDuration"].AsFloat,
					myDefaultEaseOutDuration = st["DefaultEaseOutDuration"].AsFloat,
					myDefaultProbability = st["DefaultProbability"].AsFloat,
				};

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
						ControlEntryAnchored ce = new ControlEntryAnchored(this, ccname, state, cc);
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
						mc = myCurrentLayer.myMorphCaptures.Find(x => x.mySID == key);
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

			// load settings
			myPlayPaused.valNoCallback = jc.HasKey("Paused") && jc["Paused"].AsBool;
			myPlayPaused.setCallbackFunction(myPlayPaused.val);

			SetLayer(myCurrentLayer);

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

		private void LoadTransitions(JSONClass jc)
		{
			JSONClass layer = jc["Layer"].AsObject;

			if (!myCurrentAnimation.myLayers.TryGetValue(layer["Name"], out myCurrentLayer))
				return;

			JSONArray slist = layer["States"].AsArray;
			for (int i=0; i<slist.Count; ++i)
			{
				JSONClass st = slist[i].AsObject;
				State source;
				if (!myCurrentLayer.myStates.TryGetValue(st["Name"], out source))
					continue;

				JSONArray tlist = st["Transitions"].AsArray;
				for (int j=0; j<tlist.Count; ++j)
				{
					JSONClass tclass = tlist[j].AsObject;

					Animation targetAnimation;
					if(String.Equals(tclass["TargetAnimation"], "[Self]"))
						targetAnimation = myCurrentAnimation;
					else if(!myAnimations.TryGetValue(tclass["TargetAnimation"], out targetAnimation))
						continue;

					Layer targetLayer;
					if(String.Equals(tclass["TargetLayer"], "[Self]"))
						targetLayer = myCurrentLayer;
					else if(!targetAnimation.myLayers.TryGetValue(tclass["TargetLayer"], out targetLayer))
						continue;

					State target;
					if(!targetLayer.myStates.TryGetValue(tclass["TargetState"], out target))
						continue;

					Transition transition = new Transition(source, target);
					transition.myProbability = tclass["Probability"].AsFloat;
					transition.myDuration = tclass["Duration"].AsFloat;
					transition.myDurationNoise = tclass["DurationNoise"].AsFloat;
					transition.myEaseInDuration = tclass["EaseInDuration"].AsFloat;
					transition.myEaseOutDuration = tclass["EaseOutDuration"].AsFloat;
					transition.mySourceState = source;
					transition.myTargetState = target;

					JSONClass synctlist = tclass["SyncTargets"].AsObject;
					foreach (string key in synctlist.Keys) {
						Layer syncLayer;
						if (!targetAnimation.myLayers.TryGetValue(key, out syncLayer))
							continue;
						State syncState;
						if (!syncLayer.myStates.TryGetValue(synctlist[key], out syncState))
							continue;
						transition.mySyncTargets[syncLayer] = syncState;
					}

					JSONClass msglist = tclass["Messages"].AsObject;
					foreach (string key in msglist.Keys) {
						Role role;
						if (!targetAnimation.myRoles.TryGetValue(key, out role))
							continue;
						transition.myMessages[role] = msglist[key];
					}

					source.myTransitions.Add(transition);
				}
			}
		}

		private void LoadMessages(JSONClass jc)
		{
			JSONClass layer = jc["Layer"].AsObject;

			if (!myCurrentAnimation.myLayers.TryGetValue(layer["Name"], out myCurrentLayer))
				return;

			JSONArray mlist = layer["Messages"].AsArray;
			for (int i=0; i<mlist.Count; ++i)
			{
				JSONClass mclass = mlist[i].AsObject;

				Animation targetAnimation;
				if(String.Equals(mclass["TargetAnimation"], "[Self]"))
					targetAnimation = myCurrentAnimation;
				else if(!myAnimations.TryGetValue(mclass["TargetAnimation"], out targetAnimation))
					continue;

				Layer targetLayer;
				if(String.Equals(mclass["TargetLayer"], "[Self]"))
					targetLayer = myCurrentLayer;
				else if(!targetAnimation.myLayers.TryGetValue(mclass["TargetLayer"], out targetLayer))
					continue;

				State target;
				if(!targetLayer.myStates.TryGetValue(mclass["TargetState"], out target))
					continue;

				Message message = new Message(mclass["Name"]);
				message.myMessageString = mclass["MessageString"];
				message.myProbability = mclass["Probability"].AsFloat;
				message.myDuration = mclass["Duration"].AsFloat;
				message.myDurationNoise = mclass["DurationNoise"].AsFloat;
				message.myEaseInDuration = mclass["EaseInDuration"].AsFloat;
				message.myEaseOutDuration = mclass["EaseOutDuration"].AsFloat;

				JSONArray srcstlist = mclass["SourceStates"].AsArray;
				for (int j=0; j<srcstlist.Count; ++j)
				{
					JSONClass src = srcstlist[j].AsObject;
					State srcst = myCurrentLayer.myStates[src["Name"]];
					message.mySourceStates[srcst.myName] = srcst;
				}

				message.myTargetState = target;

				JSONClass synctlist = mclass["SyncTargets"].AsObject;
				foreach (string key in synctlist.Keys) {
					Layer syncLayer;
					if (!targetAnimation.myLayers.TryGetValue(key, out syncLayer))
						continue;
					State syncState;
					if (!syncLayer.myStates.TryGetValue(synctlist[key], out syncState))
						continue;
					message.mySyncTargets[syncLayer] = syncState;
				}

				myCurrentLayer.myMessages[message.myName] = message;
			}
		}

		// =======================================================================================

		private class Animation
		{
			public string myName;
			public Dictionary<string, Layer> myLayers = new Dictionary<string, Layer>();
			public Dictionary<string, Role> myRoles = new Dictionary<string, Role>();
			public float mySpeed = 1.0f;

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

		// =======================================================================================
		private class Layer
		{
			public string myName;
			public Animation myAnimation;
			public Dictionary<string, State> myStates = new Dictionary<string, State>();
			public Dictionary<string, Message> myMessages = new Dictionary<string, Message>();
			public bool myNoValidTransition = false;
			public State myCurrentState;
			public List<ControlCapture> myControlCaptures = new List<ControlCapture>();
			public List<MorphCapture> myMorphCaptures = new List<MorphCapture>();
			private Transition myTransition;
			private float myTransitionNoise = 0.0f;
			public float myClock = 0.0f;
			public float myDuration = 1.0f;
			private List<TriggerActionDiscrete> myTriggerActionsNeedingUpdate = new List<TriggerActionDiscrete>();
			private State myBlendState = State.CreateBlendState();

			public Layer(string name)
			{
				myName = name;
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
				// SuperController.LogError("Set State");
				// SuperController.LogError(state.myName);
				myNoValidTransition = false;
				myCurrentState = state;

				myClock = 0.0f;
				myDuration = UnityEngine.Random.Range(state.myWaitDurationMin, state.myWaitDurationMax);
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
				for (int i=0; i<myTriggerActionsNeedingUpdate.Count; ++i)
					myTriggerActionsNeedingUpdate[i].Update();
				myTriggerActionsNeedingUpdate.RemoveAll(a => !a.timerActive);

				bool paused = myPaused && myTransition == null;
				if (!paused)
					myClock = Mathf.Min(myClock + Time.deltaTime*myCurrentAnimation.mySpeed, 100000.0f);

				float t;
				if(myTransition != null) {
					if(myClock >= myDuration) {
						t = Smooth(myTransition.myEaseOutDuration, myTransition.myEaseInDuration, myTransition.myDuration, myClock-myDuration);

						for (int i=0; i<myControlCaptures.Count; ++i)
							myControlCaptures[i].UpdateTransition(t);
						for (int i=0; i<myMorphCaptures.Count; ++i)
							myMorphCaptures[i].UpdateTransition(t);

						if (myClock >= myDuration + myTransition.myDuration + myTransitionNoise)
						{
							if (myTransition.myTargetState != null)
							{
								State previousState = myCurrentState;
								SetState(myTransition.myTargetState);
								if (myMainLayer.val == myName)
									myMainState.valNoCallback = myCurrentState.myName;
									myMainAnimation.valNoCallback = myCurrentState.myAnimation.myName;

								if (previousState.ExitEndTrigger != null)
									previousState.ExitEndTrigger.Trigger(myTriggerActionsNeedingUpdate);
								if (myCurrentState.EnterEndTrigger != null)
									myCurrentState.EnterEndTrigger.Trigger(myTriggerActionsNeedingUpdate);
								foreach(var m in myTransition.myMessages) {
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
							myTransition = null;
						}
					}
				}
				else if (!paused && !myNoValidTransition)
				{
					SetRandomTransition();
				}
			}

			public void ArriveFromAnotherAnimation(Transition transition, State targetState) {
				targetState.myLayer.SetBlendTransition(targetState);

				myMainAnimation.valNoCallback = myCurrentAnimation.myName;
			}

			private void TransitionToAnotherAnimation(Transition transition)
			{
				State targetState = transition.myTargetState;
				Animation animation = targetState.myAnimation;
				Layer targetLayer = targetState.myLayer;
				myCurrentAnimation = animation;
				SetAnimation(animation);
				targetLayer.ArriveFromAnotherAnimation(transition, targetState);
				foreach(var sc in transition.mySyncTargets) {
					Layer syncLayer = sc.Key;
					State syncState = sc.Value;
					syncLayer.ArriveFromAnotherAnimation(transition, syncState);
				}

				// myMainAnimation.valNoCallback = myCurrentAnimation.myName;
				// myMainLayer.valNoCallback = myCurrentLayer.myName;
				// myMainState.valNoCallback = myCurrentState.myName;
			}

			public void SetTransition(Transition transition)
			{
				// SuperController.LogError("Set transition");
				// SuperController.LogError(myTransition.myDuration.ToString());
				// SuperController.LogError(myTransition.mySourceState.myName);
				// SuperController.LogError(myTransition.myTargetState.myAnimation.myName);
				// SuperController.LogError(myTransition.myTargetState.myName);

				myNoValidTransition = false;

				myClock = 0.0f;

				if(transition.myTargetState.myAnimation != myCurrentAnimation) {
					TransitionToAnotherAnimation(transition);
					return;
				}

				for (int i=0; i<myControlCaptures.Count; ++i)
					myControlCaptures[i].SetTransition(transition);
				for (int i=0; i<myMorphCaptures.Count; ++i)
					myMorphCaptures[i].SetTransition(transition);

				myTransition = transition;

				myTransitionNoise = UnityEngine.Random.Range(-transition.myDurationNoise, transition.myDurationNoise);

				if (transition.mySourceState.ExitBeginTrigger != null)
					transition.mySourceState.ExitBeginTrigger.Trigger(myTriggerActionsNeedingUpdate);
				if (transition.myTargetState.EnterBeginTrigger != null)
					transition.myTargetState.EnterBeginTrigger.Trigger(myTriggerActionsNeedingUpdate);
			}

			public void SetRandomTransition()
			{
				List<State> states = myCurrentState.getReachableStates();

				int i;
				float sum = 0.0f;
				// for (i=0; i<states.Count; ++i)
				// 	sum += states[i].myDefaultProbability;
				for (i=0; i<myCurrentState.myTransitions.Count; ++i)
					sum += myCurrentState.myTransitions[i].myProbability;
				if (sum == 0.0f)
				{
					myTransition = null;
					myNoValidTransition = true;
				}
				else
				{
					float threshold = UnityEngine.Random.Range(0.0f, sum);
					sum = 0.0f;
					for (i=0; i<myCurrentState.myTransitions.Count-1; ++i)
					{
						sum += myCurrentState.myTransitions[i].myProbability;
						if (threshold <= sum)
							break;
					}
					SetTransition(myCurrentState.getIncomingTransition(states[i]));
				}
			}

			public void SetBlendTransition(State state, bool debug = false)
			{
				// SuperController.LogError("Set blend transition");
				// SuperController.LogError(myCurrentState.myName);
				// SuperController.LogError(state.myName);
				// if (myCurrentState != null)
				// {
				// 	List<State> states = new List<State>(16);
				// 	for (int i=0; i< myCurrentState.myTransitions.Count; i++) {
				// 		states.Add(myCurrentState.myTransitions[i]);
				// 	}
				// 	List<int> indices = new List<int>(4);
				// 	for (int i=0; i<states.Count; ++i)
				// 	{
				// 		if (states[i] == state)
				// 			indices.Add(i);
				// 	}
				// 	if (indices.Count == 0)
				// 	{
				// 		states.Clear();
				// 		for (int i=0; i< myCurrentState.myTransitions.Count; i++) {
				// 			states.Add(myCurrentState.myTransitions[i]);
				// 		}

				// 		for (int i=0; i<states.Count; ++i)
				// 		{
				// 			if (states[i] == state)
				// 				indices.Add(i);
				// 		}
				// 	}
				// 	if (indices.Count > 0)
				// 	{
				// 		int selected = UnityEngine.Random.Range(0, indices.Count);
				// 		myTransition.myTargetState = states[indices[selected]];
				// 	}
				// }

				if (myCurrentState == null)
				{
					CaptureState(myBlendState);
					myBlendState.AssignOutTriggers(myCurrentState);
					SetTransition(new Transition(myBlendState, state, 0.1f*myCurrentAnimation.mySpeed));
				} else {
					SetTransition(new Transition(myCurrentState, state, 0.1f*myCurrentAnimation.mySpeed));
				}
				myClock = myDuration;
			}
		}
		private class Role
		{
			public String myName;
			public Atom myPerson;

			public Role(string name){
				myName = name;
			}
		}

		private class BaseTransition
		{
			public Dictionary<Layer, State> mySyncTargets = new Dictionary<Layer, State>();
			public Dictionary<Role, String> myMessages = new Dictionary<Role, String>();
			public State myTargetState;
			public float myProbability;
			public float myEaseInDuration;
			public float myEaseOutDuration;
			public float myDuration;
			public float myDurationNoise = 0.0f;
		}

		private class Transition : BaseTransition
		{
			public State mySourceState;

			public Transition(State sourceState, State targetState)
			{
				mySourceState = sourceState;
				myTargetState = targetState;
				myProbability = targetState.myDefaultProbability;
				myEaseInDuration = targetState.myDefaultEaseInDuration;
				myEaseOutDuration = targetState.myDefaultEaseOutDuration;
				myDuration = targetState.myDefaultDuration;
			}

			public Transition(State sourceState, State targetState, float duration)
			{
				mySourceState = sourceState;
				myTargetState = targetState;
				myProbability = targetState.myDefaultProbability;
				myEaseInDuration = 0.0f;
				myEaseOutDuration = 0.0f;
				myDuration = duration;
			}

			public Transition(Transition transition)
			{
				mySourceState = transition.mySourceState;
				myTargetState = transition.myTargetState;
				myProbability = transition.myProbability;
				myEaseInDuration = transition.myEaseInDuration;
				myEaseOutDuration = transition.myEaseOutDuration;
				myDuration = transition.myDuration;
				myDurationNoise = transition.myDurationNoise;
				mySyncTargets = transition.mySyncTargets;
				myMessages = transition.myMessages;
			}

			public Transition(State sourceState, Message message)
			{
				mySourceState = sourceState;
				myTargetState = message.myTargetState;
				myProbability = message.myProbability;
				myEaseInDuration = message.myEaseInDuration;
				myEaseOutDuration = message.myEaseOutDuration;
				myDuration = message.myDuration;
				myDurationNoise = message.myDurationNoise;
				mySyncTargets = message.mySyncTargets;
			}
		}

		private class Message : BaseTransition
		{
			public String myMessageString;
			public String myName;
			public Dictionary<string, State> mySourceStates = new Dictionary<string, State>();

			public Message(string name) {
				myName = name;
			}
		}

		private class State
		{
			public string myName;
			public Animation myAnimation;
			public Layer myLayer;
			public float myWaitDurationMin;
			public float myWaitDurationMax;
			public float myDefaultDuration;
			public float myDefaultEaseInDuration;
			public float myDefaultEaseOutDuration;
			public float myDefaultProbability;
			public bool myIsRootState = false;
			public uint myDebugIndex = 0;
			public Dictionary<ControlCapture, ControlEntryAnchored> myControlEntries = new Dictionary<ControlCapture, ControlEntryAnchored>();
			public Dictionary<MorphCapture, float> myMorphEntries = new Dictionary<MorphCapture, float>();
			public List<Transition> myTransitions = new List<Transition>();
			public EventTrigger EnterBeginTrigger;
			public EventTrigger EnterEndTrigger;
			public EventTrigger ExitBeginTrigger;
			public EventTrigger ExitEndTrigger;

			private State(string name)
			{
				myName = name;
				myAnimation = myCurrentAnimation;
				myLayer = myCurrentLayer;
				// do NOT init event triggers
			}

			public State(MVRScript script, string name)
			{
				myName = name;
				myAnimation = myCurrentAnimation;
				myLayer = myCurrentLayer;
				EnterBeginTrigger = new EventTrigger(script, "OnEnterBegin", name);
				EnterEndTrigger = new EventTrigger(script, "OnEnterEnd", name);
				ExitBeginTrigger = new EventTrigger(script, "OnExitBegin", name);
				ExitEndTrigger = new EventTrigger(script, "OnExitEnd", name);
			}

			public State(string name, State source)
			{
				myName = name;
				myAnimation = myCurrentAnimation;
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

			public Transition getIncomingTransition(State state) {
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

			public static State CreateBlendState()
			{
				return new State("BlendState") {
					myWaitDurationMin = 0.0f,
					myWaitDurationMax = 0.0f,
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
					entry = new ControlEntryAnchored(myPlugin, myName, state, this);
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

			public void SetTransition(Transition transition)
			{
				myEntryCount = 2;
				if (!transition.mySourceState.myControlEntries.TryGetValue(this, out myTransition[0]))
				{
					CaptureEntry(transition.mySourceState);
					myTransition[0] = transition.mySourceState.myControlEntries[this];
				}

				if (!transition.myTargetState.myControlEntries.TryGetValue(this, out myTransition[1]))
				{
					CaptureEntry(transition.myTargetState);
					myTransition[1] = transition.myTargetState.myControlEntries[this];
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
				if (myEntryCount <= 2 || myEntryCount > 4){
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
						return 0.0f;
					}
					else if (idx >= numSamples+2){
						return 1.0f;
					}
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
			public State myState;

			public ControlEntryAnchored(AnimationPoser plugin, string control, State state, ControlCapture controlCapture)
			{
				myState = state;
				Atom containingAtom = plugin.GetContainingAtom();
				if (containingAtom.type != "Person" || control == "control")
					myAnchorMode = ANCHORMODE_WORLD;
				myAnchorAAtom = myAnchorBAtom = containingAtom.uid;
				myControlCapture = controlCapture;
				myAnchorAAtom = myAnchorBAtom = containingAtom.uid;
			}

			public ControlEntryAnchored Clone()
			{
				return (ControlEntryAnchored)MemberwiseClone();
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
					} else {
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
				// myEntry.myPosition = position;
				// myEntry.myRotation = rotation;

				State rootState = myCurrentLayer.myStates.Values.ToList().Find(s => s.myIsRootState);
				if(rootState != null && myState == rootState) {
					ControlCapture rootcc = rootState.myControlEntries.Keys.ToList().Find(ccx => ccx.myName == myControlCapture.myName);
					ControlEntryAnchored rootce = rootState.myControlEntries[rootcc];
					foreach(var s in myCurrentLayer.myStates) {
						State st = s.Value;
						if(st != rootState) {
							ControlCapture cc = st.myControlEntries.Keys.ToList().Find(ccx => ccx.myName == myControlCapture.myName);
							ControlEntryAnchored ce = st.myControlEntries[cc];
							ce.myAnchorOffset.myPosition = ce.myAnchorOffset.myPosition + (position - rootce.myAnchorOffset.myPosition);
							ce.myAnchorOffset.myRotation = Quaternion.Inverse(Quaternion.Inverse(rotation) * rootce.myAnchorOffset.myRotation) * ce.myAnchorOffset.myRotation;
						}
					}
				}
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
							return;
						}
						root.myPosition = myAnchorATransform.position;
						root.myRotation = myAnchorATransform.rotation;
					} else {
						if (myAnchorATransform == null || myAnchorBTransform == null){
							return;
						}
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
			public string mySID;
			public DAZMorph myMorph;
			public DAZCharacterSelector.Gender myGender;
			private float[] myTransition = new float[MAX_STATES];
			private int myEntryCount = 0;
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

			public void SetTransition(Transition transition)
			{
				myEntryCount = 2;
				bool identical = true;
				float morphValue = myMorph.morphValue;

				if (!transition.mySourceState.myMorphEntries.TryGetValue(this, out myTransition[0]))
				{
					CaptureEntry(transition.mySourceState);
					myTransition[0] = morphValue;
				}
				else
				{
					identical &= (myTransition[0] == morphValue);
				}

				if (!transition.myTargetState.myMorphEntries.TryGetValue(this, out myTransition[1]))
				{
					CaptureEntry(transition.myTargetState);
					myTransition[1] = morphValue;
				}
				else
				{
					identical &= (myTransition[1] == morphValue);
				}

				if (identical)
					myEntryCount = 0; // nothing to do, save some performance
			}

			public void UpdateTransition(float t)
			{
				if (!myApply){
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
				if (myMorphCaptures.Find(x => x.mySID == sid) == null){
					return sid;
				}
			}

			return null; // you are very lucky, you should play lottery!
		}
	}
}
