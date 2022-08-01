/* /////////////////////////////////////////////////////////////////////////////////////////////////
AnimationPoser by HaremLife.
Based on Life#IdlePoser by MacGruber.
State-based idle animation system with anchoring.
https://www.patreon.com/MacGruber_Laboratory
https://github.com/haremlife/AnimationPoser

Licensed under CC BY-SA after EarlyAccess ended. (see https://creativecommons.org/licenses/by-sa/4.0/)

///////////////////////////////////////////////////////////////////////////////////////////////// */
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
		private void SetHeader(JSONClass jc, string format = "") {
			JSONClass info = new JSONClass();
			if(format == "") {
				info["Format"] = "HaremLife.AnimationPoser";
			} else {
				info["Format"] = "HaremLife.AnimationPoser." + format;
			}
			info["Version"] = "3.6";
			string creatorName = UserPreferences.singleton.creatorName;
			if (string.IsNullOrEmpty(creatorName))
				creatorName = "Unknown";
			info["Author"] = creatorName;
			jc["Info"] = info;
		}

		private JSONClass SaveAnimations()
		{
			JSONClass jc = new JSONClass();
			SetHeader(jc);

			if (myCurrentAnimation != null)
				jc["InitialAnimation"] = myCurrentAnimation.myName;

			// save settings
			jc["Paused"].AsBool = myPlayPaused.val;

			JSONArray anims = new JSONArray();
			foreach(var an in myAnimations)
			{
				anims.Add("", SaveAnimation(an.Value));
			}

			SaveRoles(jc);
			SaveMessages(jc);
			SaveAvoids(jc);
			jc["Animations"] = anims;

			return jc;
		}

		private void SaveRoles(JSONClass jc, bool withHeader = false) {
			if(withHeader) {
				SetHeader(jc, "Roles");
			}
			JSONArray rlist = new JSONArray();
			foreach (var r in myRoles)
			{
				rlist.Add("", SaveRole(r.Value));
			}
			jc["Roles"] = rlist;
		}

		private void SaveMessages(JSONClass jc, bool withHeader = false) {
			if(withHeader) {
				SetHeader(jc, "Messages");
			}
			JSONArray mlist = new JSONArray();
			foreach(var mm in myMessages)
			{
				mlist.Add("", SaveMessage(mm.Value));
			}
			jc["Messages"] = mlist;
		}

		private void SaveAvoids(JSONClass jc, bool withHeader = false) {
			if(withHeader) {
				SetHeader(jc, "Avoids");
			}
			JSONArray alist = new JSONArray();
			foreach(var aa in myAvoids)
			{
				alist.Add("", SaveAvoid(aa.Value));
			}
			jc["Avoids"] = alist;
		}

		private JSONClass SaveAnimation(Animation animationToSave, bool withHeader = false)
		{
			JSONClass anim = new JSONClass();
			if(withHeader) {
				SetHeader(anim, "Animation");
			}
			anim["Name"] = animationToSave.myName;
			anim["Speed"].AsFloat = animationToSave.mySpeed;
			JSONArray llist = new JSONArray();
			foreach(var l in animationToSave.myLayers){
				llist.Add("", SaveLayer(l.Value));
			}
			anim["Layers"] = llist;
			return anim;
		}

		private JSONClass SaveRole(Role roleToSave) {
			JSONClass rclass = new JSONClass();
			rclass["Name"] = roleToSave.myName;
			if(roleToSave.myPerson != null) {
				rclass["Person"] = roleToSave.myPerson.name;
			} else {
				rclass["Person"] = "";
			}
			// ccclass["Person"] = r.myPerson;
			return rclass;
		}

		private JSONClass SaveMessage(Message messageToSave) {
			JSONClass m = new JSONClass();

			m["Name"] = messageToSave.myName;
			m["MessageString"] = messageToSave.myMessageString;

			JSONArray srcstlist = new JSONArray();
			foreach(var srcst in messageToSave.mySourceStates) {
				JSONClass src = new JSONClass();
				src["AnimationName"] = srcst.Value.myLayer.myAnimation.myName;
				src["LayerName"] = srcst.Value.myLayer.myName;
				src["StateName"] = srcst.Value.myName;
				srcstlist.Add(src);
			}
			m["SourceStates"] = srcstlist;
			m["TargetState"] = messageToSave.myTargetState.myName;
			m["TargetLayer"] = messageToSave.myTargetState.myLayer.myName;
			m["TargetAnimation"] = messageToSave.myTargetState.myAnimation().myName;

			return m;
		}

		private JSONClass SaveAvoid(Avoid avoidToSave) {
			JSONClass a = new JSONClass();

			a["Name"] = avoidToSave.myName;
			a["IsPlaced"].AsBool = avoidToSave.myIsPlaced;

			JSONArray avstlist = new JSONArray();
			foreach(var avst in avoidToSave.myAvoidStates) {
				JSONClass av = new JSONClass();
				av["AnimationName"] = avst.Value.myLayer.myAnimation.myName;
				av["LayerName"] = avst.Value.myLayer.myName;
				av["StateName"] = avst.Value.myName;
				avstlist.Add(av);
			}
			a["AvoidStates"] = avstlist;

			return a;
		}

		private JSONClass SaveLayer(Layer layerToSave, bool withHeader = false)
		{
			JSONClass layer = new JSONClass();
			if(withHeader) {
				SetHeader(layer, "Layer");
			}

			layer["Name"] = layerToSave.myName;

			if (layerToSave.myCurrentState != null)
				layer["InitialState"] = layerToSave.myCurrentState.myName;

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
					JSONClass t = new JSONClass();
					if(state.myTransitions[i] is Transition) {
						Transition transition = state.myTransitions[i] as Transition;
						t["Type"] = "Direct";
						t["SourceState"] = transition.mySourceState.myName;
						t["TargetState"] = transition.myTargetState.myName;
						if(transition.myTargetState.myLayer == transition.mySourceState.myLayer)
							t["TargetLayer"] = "[Self]";
						else
							t["TargetLayer"] = transition.myTargetState.myLayer.myName;
						if(transition.myTargetState.myAnimation() == transition.mySourceState.myAnimation())
							t["TargetAnimation"] = "[Self]";
						else
							t["TargetAnimation"] = transition.myTargetState.myAnimation().myName;
						t["Duration"].AsFloat = transition.myDuration;
						t["DurationNoise"].AsFloat = transition.myDurationNoise;
						t["EaseInDuration"].AsFloat = transition.myEaseInDuration;
						t["EaseOutDuration"].AsFloat = transition.myEaseOutDuration;
						t["Probability"].AsFloat = transition.myProbability;

						JSONClass ctltmls = new JSONClass();
						foreach (var c in transition.myControlTimelines) {
							JSONArray ctlkfms = new JSONArray();
							ControlTimeline timeline = c.Value;

							for(int j=0; j<timeline.myKeyframes.Count; j++) {
								JSONClass ctlkfm = new JSONClass();
								ControlKeyframe keyframe = timeline.myKeyframes[j] as ControlKeyframe;
								ctlkfm["T"].AsFloat = keyframe.myTime;
								ctlkfm["X"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.x;
								ctlkfm["Y"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.y;
								ctlkfm["Z"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.z;
								Vector3 rotation = keyframe.myControlEntry.myTransform.myRotation.eulerAngles;
								ctlkfm["RX"].AsFloat = rotation.x;
								ctlkfm["RY"].AsFloat = rotation.y;
								ctlkfm["RZ"].AsFloat = rotation.z;

								ctlkfms.Add("", ctlkfm);
							}

							JSONClass ctltml = new JSONClass();
							if(timeline.myHalfWayRotation != null) {
								Quaternion halfway = (Quaternion) timeline.myHalfWayRotation;
								ctltml["HalfwayRotationX"].AsFloat = halfway.x;
								ctltml["HalfwayRotationY"].AsFloat = halfway.y;
								ctltml["HalfwayRotationZ"].AsFloat = halfway.z;
								ctltml["HalfwayRotationW"].AsFloat = halfway.w;
							}
							ctltml["ControlKeyframes"] = ctlkfms;
							ctltmls[c.Key.myName] = ctltml;
						}
						t["ControlTimelines"] = ctltmls;

						JSONClass mphtmls = new JSONClass();
						foreach (var c in transition.myMorphTimelines) {
							JSONArray mphkfms = new JSONArray();
							MorphTimeline timeline = c.Value;

							for(int j=0; j<timeline.myKeyframes.Count; j++) {
								JSONClass mphkfm = new JSONClass();
								MorphKeyframe keyframe = timeline.myKeyframes[j] as MorphKeyframe;
								mphkfm["T"].AsFloat = keyframe.myTime;
								mphkfm["V"].AsFloat = keyframe.myMorphEntry;

								mphkfms.Add("", mphkfm);
							}

							JSONClass mphtml = new JSONClass();
							mphtml["MorphKeyframes"] = mphkfms;
							mphtmls[c.Key.mySID] = mphtml;
						}
						t["MorphTimelines"] = mphtmls;

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

						JSONClass rls = new JSONClass();
						foreach (var rl in transition.myAvoids) {
							JSONClass avds = new JSONClass();
							foreach(var avd in rl.Value) {
								avds[avd.Key].AsBool = avd.Value;
							}
							rls[rl.Key.myName] = avds;
						}
						t["Avoids"] = rls;
					} else {
						IndirectTransition transition = state.myTransitions[i] as IndirectTransition;
						t["Type"] = "Indirect";
						t["SourceState"] = transition.mySourceState.myName;
						t["TargetState"] = transition.myTargetState.myName;
						if(transition.myTargetState.myLayer == transition.mySourceState.myLayer)
							t["TargetLayer"] = "[Self]";
						else
							t["TargetLayer"] = transition.myTargetState.myLayer.myName;
						if(transition.myTargetState.myAnimation() == transition.mySourceState.myAnimation())
							t["TargetAnimation"] = "[Self]";
						else
							t["TargetAnimation"] = transition.myTargetState.myAnimation().myName;
						t["Probability"].AsFloat = transition.myProbability;
					}

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
						ceclass["PositionState"] = ce.myPositionState.ToString();
						ceclass["RotationState"] = ce.myRotationState.ToString();
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
							ceclass["AnchorAType"].AsInt = ce.myAnchorAType;
							if(ce.myAnchorAAtom == containingAtom.uid)
								ceclass["AnchorAAtom"] = "[Self]";
							else
								ceclass["AnchorAAtom"] = ce.myAnchorAAtom;
							ceclass["AnchorAControl"] = ce.myAnchorAControl;
						}
						if (ce.myAnchorMode == ControlEntryAnchored.ANCHORMODE_BLEND)
						{
							ceclass["AnchorBType"].AsInt = ce.myAnchorBType;
							if(ce.myAnchorAAtom == containingAtom.uid)
								ceclass["AnchorBAtom"] = "[Self]";
							else
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

			return layer;
		}

		private void LoadAnimations(JSONClass jc)
		{
			// load info
			myAnimations.Clear();
			int version = jc["Info"].AsObject["Version"].AsInt;

			LoadRoles(jc);

			// load captures
			JSONArray anims = jc["Animations"].AsArray;
			for (int l=0; l<anims.Count; ++l)
			{
				JSONClass anim = anims[l].AsObject;
				Animation animation = CreateAnimation(anim["Name"]);
				animation.mySpeed = anim["Speed"].AsFloat;

				JSONArray layers = anim["Layers"].AsArray;
				for(int m=0; m<layers.Count; m++)
				{
					JSONClass layerObj = layers[m].AsObject;
					Layer layer = LoadLayer(layerObj, false);
					animation.myLayers[layer.myName] = layer;
					layer.myAnimation = animation;
				}

				myAnimations[animation.myName] = animation;
			}

			for (int l=0; l<anims.Count; ++l)
			{
				JSONClass anim = anims[l].AsObject;
				Animation animation;
				if (!myAnimations.TryGetValue(anim["Name"], out animation))
					continue;
				JSONArray layers = anim["Layers"].AsArray;
				for(int m=0; m<layers.Count; m++)
				{
					JSONClass layerObj;
					if(layers[m].AsObject.HasKey("Layer")) {
						layerObj = layers[m].AsObject["Layer"].AsObject;
					} else {
						layerObj = layers[m].AsObject;
					}

					Layer layer;
					if (!animation.myLayers.TryGetValue(layerObj["Name"], out layer))
						continue;

					LoadTransitions(layer, layerObj);
				}
			}

			if(myAnimations.Count == 0)
				return;

			Animation initial;
			if (jc.HasKey("InitialAnimation") && myAnimations.TryGetValue(jc["InitialAnimation"].Value, out initial)) {
			} else {
				initial = myAnimations.Values.ToList()[0];
			}
			SetAnimation(initial);
			initial.InitAnimationLayers();

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
			LoadMessages(jc);
			LoadAvoids(jc);
		}

		private Animation LoadAnimation(JSONClass anim)
		{
			Animation animation = CreateAnimation(anim["Name"]);
			animation.mySpeed = anim["Speed"].AsFloat;

			JSONArray layers = anim["Layers"].AsArray;
			for(int m=0; m<layers.Count; m++)
			{
				JSONClass layerObj = layers[m].AsObject;
				Layer layer = LoadLayer(layerObj, false);
				animation.myLayers[layer.myName] = layer;
				layer.myAnimation = animation;
			}

			for(int m=0; m<layers.Count; m++)
			{
				JSONClass layerObj;
				if(layers[m].AsObject.HasKey("Layer")) {
					layerObj = layers[m].AsObject["Layer"].AsObject;
				} else {
					layerObj = layers[m].AsObject;
				}

				Layer layer;
				if (!animation.myLayers.TryGetValue(layerObj["Name"], out layer))
					continue;

				LoadTransitions(layer, layerObj);
			}

			return animation;
		}

		private FreeControllerV3.PositionState getPositionState(String state) {
			if(state == "On") {
				return FreeControllerV3.PositionState.On;
			} else if(state == "Comply") {
				return FreeControllerV3.PositionState.Comply;
			} else if(state == "Off") {
				return FreeControllerV3.PositionState.Off;
			} else if(state == "Hold") {
				return FreeControllerV3.PositionState.Hold;
			} else if(state == "Lock") {
				return FreeControllerV3.PositionState.Lock;
			} return FreeControllerV3.PositionState.On;
		}

		private FreeControllerV3.RotationState getRotationState(String state) {
			if(state == "On") {
				return FreeControllerV3.RotationState.On;
			} else if(state == "Comply") {
				return FreeControllerV3.RotationState.Comply;
			} else if(state == "Off") {
				return FreeControllerV3.RotationState.Off;
			} else if(state == "Hold") {
				return FreeControllerV3.RotationState.Hold;
			} else if(state == "Lock") {
				return FreeControllerV3.RotationState.Lock;
			} return FreeControllerV3.RotationState.On;
		}

		private Layer LoadLayer(JSONClass jc, bool newName)
		{
			JSONClass layerObj;
			if(jc.HasKey("Layer")) {
				layerObj = jc["Layer"].AsObject;
			} else {
				layerObj = jc.AsObject;
			}

			Layer layer;
			if(newName)
				layer = CreateLayer(FindNewName("Layer", "layers", new List<string>(myCurrentAnimation.myLayers.Keys)));
			else
				layer = CreateLayer(layerObj["Name"]);

			// load captures
			if (layerObj.HasKey("ControlCaptures"))
			{
				JSONArray cclist = layerObj["ControlCaptures"].AsArray;
				for (int i=0; i<cclist.Count; ++i)
				{
					ControlCapture cc;
					JSONClass ccclass = cclist[i].AsObject;
					cc = new ControlCapture(this, ccclass["Name"]);
					cc.myApplyPosition = ccclass["ApplyPos"].AsBool;
					cc.myApplyRotation = ccclass["ApplyRot"].AsBool;

					if (cc.IsValid())
						layer.myControlCaptures.Add(cc);
				}
			}
			if (layerObj.HasKey("MorphCaptures"))
			{
				JSONArray mclist = layerObj["MorphCaptures"].AsArray;
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
						layer.myMorphCaptures.Add(mc);
				}
			}

			// load states
			JSONArray slist = layerObj["States"].AsArray;
			for (int i=0; i<slist.Count; ++i)
			{
				// load state
				JSONClass st = slist[i].AsObject;
				State state = new State(this, st["Name"], layer) {
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


				if (layer.myStates.ContainsKey(state.myName))
					continue;
				layer.myStates[state.myName] = state;

				// load control captures
				if (layer.myControlCaptures.Count > 0)
				{
					JSONClass celist = st["ControlEntries"].AsObject;
					foreach (string ccname in celist.Keys)
					{
						ControlCapture cc = layer.myControlCaptures.Find(x => x.myName == ccname);
						if (cc == null)
							continue;

						JSONClass ceclass = celist[ccname].AsObject;
						ControlEntryAnchored ce = new ControlEntryAnchored(cc);
						ce.myPositionState = getPositionState(ceclass["PositionState"]);
						ce.myRotationState = getRotationState(ceclass["RotationState"]);
						ce.myAnchorOffset = new ControlTransform(
							new Vector3(ceclass["PX"].AsFloat, ceclass["PY"].AsFloat, ceclass["PZ"].AsFloat),
							Quaternion.Euler(ceclass["RX"].AsFloat, ceclass["RY"].AsFloat, ceclass["RZ"].AsFloat)
						);

						ce.myAnchorMode = ceclass["AnchorMode"].AsInt;
						if (ce.myAnchorMode >= ControlEntryAnchored.ANCHORMODE_SINGLE)
						{
							ce.myDampingTime = ceclass["DampingTime"].AsFloat;
							ce.myAnchorAType = ceclass["AnchorAType"].AsInt;
							ce.myAnchorAAtom = ceclass["AnchorAAtom"].Value;
							ce.myAnchorAControl = ceclass["AnchorAControl"].Value;

							if (ce.myAnchorAAtom == "[Self]")
								ce.myAnchorAAtom = containingAtom.uid;
						}
						if (ce.myAnchorMode == ControlEntryAnchored.ANCHORMODE_BLEND)
						{
							ce.myAnchorBType = ceclass["AnchorBType"].AsInt;
							ce.myAnchorBAtom = ceclass["AnchorBAtom"].Value;
							ce.myAnchorBControl = ceclass["AnchorBControl"].Value;
							ce.myBlendRatio = ceclass["BlendRatio"].AsFloat;

							if (ce.myAnchorBAtom == "[Self]")
								ce.myAnchorBAtom = containingAtom.uid;
						}
						ce.Initialize();

						state.myControlEntries.Add(cc, ce);
					}
					for (int j=0; j<layer.myControlCaptures.Count; ++j)
					{
						if (!state.myControlEntries.ContainsKey(layer.myControlCaptures[j]))
							layer.myControlCaptures[j].CaptureEntry(state);
					}
				}

				// load morph captures
				if (layer.myMorphCaptures.Count > 0)
				{
					JSONClass melist = st["MorphEntries"].AsObject;
					foreach (string key in melist.Keys)
					{
						MorphCapture mc = null;
						mc = layer.myMorphCaptures.Find(x => x.mySID == key);
						if (mc == null)
						{
							continue;
						}
						float me = melist[key].AsFloat;
						state.myMorphEntries.Add(mc, me);
					}
					for (int j=0; j<layer.myMorphCaptures.Count; ++j)
					{
						if (!state.myMorphEntries.ContainsKey(layer.myMorphCaptures[j]))
							layer.myMorphCaptures[j].CaptureEntry(state);
					}
				}
			}

			// blend to initial state
			if (jc.HasKey("InitialState"))
			{
				State initial;
				if (layer.myStates.TryGetValue(jc["InitialState"].Value, out initial))
				{
					layer.SetState(initial);
				}
			}
			return layer;
		}

		private void LoadTransitions(Layer layer, JSONClass layerObj)
		{
			JSONArray slist = layerObj["States"].AsArray;
			for (int i=0; i<slist.Count; ++i)
			{
				JSONClass st = slist[i].AsObject;
				State source;
				if (!layer.myStates.TryGetValue(st["Name"], out source))
					continue;

				JSONArray tlist = st["Transitions"].AsArray;
				for (int j=0; j<tlist.Count; ++j)
				{
					JSONClass tclass = tlist[j].AsObject;

					Animation targetAnimation;
					if(String.Equals(tclass["TargetAnimation"], "[Self]"))
						targetAnimation = source.myLayer.myAnimation;
					else if(!myAnimations.TryGetValue(tclass["TargetAnimation"], out targetAnimation))
						continue;

					Layer targetLayer;
					if(String.Equals(tclass["TargetLayer"], "[Self]"))
						targetLayer = source.myLayer;
					else if(!targetAnimation.myLayers.TryGetValue(tclass["TargetLayer"], out targetLayer))
						continue;

					State target;
					if(!targetLayer.myStates.TryGetValue(tclass["TargetState"], out target))
						continue;

					if(String.Equals(tclass["Type"], "Direct") || tclass["Type"] == null) {
						Transition transition = new Transition(source, target);
						transition.myProbability = tclass["Probability"].AsFloat;
						transition.myDuration = tclass["Duration"].AsFloat;
						transition.myDurationNoise = tclass["DurationNoise"].AsFloat;
						transition.myEaseInDuration = tclass["EaseInDuration"].AsFloat;
						transition.myEaseOutDuration = tclass["EaseOutDuration"].AsFloat;

						if(tclass.HasKey("ControlTimelines")) {
							JSONClass ctltmls = tclass["ControlTimelines"].AsObject;
							foreach (string key in ctltmls.Keys) {
								JSONClass ctltml = ctltmls[key].AsObject;

								ControlCapture capture = layer.myControlCaptures.FirstOrDefault(cc => cc.myName == key);
								if(capture == null)
									continue;

								ControlTimeline timeline = new ControlTimeline(capture);
								timeline.SetEndpoints(
									transition.mySourceState.myControlEntries[capture],
									transition.myTargetState.myControlEntries[capture]
								);

								JSONArray kfms = ctltml["ControlKeyframes"].AsArray;

								for (int k=1; k<kfms.Count-1; ++k)
								{
									JSONClass kfm = kfms[k].AsObject;

									ControlEntry ce = new ControlEntry(capture);
									ce.myTransform = new ControlTransform(
										new Vector3(kfm["X"].AsFloat, kfm["Y"].AsFloat, kfm["Z"].AsFloat),
										Quaternion.Euler(kfm["RX"].AsFloat, kfm["RY"].AsFloat, kfm["RZ"].AsFloat)
									);
									timeline.AddKeyframe(new ControlKeyframe(kfm["T"].AsFloat, ce));
								}
								if(ctltml.Keys.Contains("HalfwayRotationX"))
									timeline.myHalfWayRotation = new Quaternion(
										ctltml["HalfwayRotationX"].AsFloat,
										ctltml["HalfwayRotationY"].AsFloat,
										ctltml["HalfwayRotationZ"].AsFloat,
										ctltml["HalfwayRotationW"].AsFloat
								);
								transition.myControlTimelines[capture] = timeline;
							}
						}

						if(tclass.HasKey("MorphTimelines")) {
							JSONClass mphtmls = tclass["MorphTimelines"].AsObject;
							foreach (string key in mphtmls.Keys) {
								JSONClass mphtml = mphtmls[key].AsObject;

								MorphCapture capture = layer.myMorphCaptures.FirstOrDefault(cc => cc.mySID == key);
								if(capture == null)
									continue;

								MorphTimeline timeline = new MorphTimeline(capture);

								JSONArray kfms = mphtml["MorphKeyframes"].AsArray;

								for (int k=0; k<kfms.Count; ++k)
								{
									JSONClass kfm = kfms[k].AsObject;

									MorphKeyframe keyframe;
									if(k==0) {
										keyframe = new MorphKeyframe("first", kfm["V"].AsFloat);
									} else if(k==kfms.Count-1) {
										keyframe = new MorphKeyframe("last", kfm["V"].AsFloat);
									} else {
										keyframe = new MorphKeyframe(kfm["T"].AsFloat, kfm["V"].AsFloat);
									}

									timeline.AddKeyframe(keyframe);
								}
								transition.myMorphTimelines[capture] = timeline;
							}
						}

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
							if (!myRoles.TryGetValue(key, out role))
								continue;
							transition.myMessages[role] = msglist[key];
						}

						JSONClass rlslist = tclass["Avoids"].AsObject;
						foreach (string rl in rlslist.Keys) {
							Role role;
							if (!myRoles.TryGetValue(rl, out role))
								continue;
							JSONClass avdslist = rlslist[rl].AsObject;
							foreach (string avd in avdslist.Keys) {
								if(!transition.myAvoids.Keys.Contains(role))
									transition.myAvoids[role] = new Dictionary<string, bool>();
								transition.myAvoids[role][avd] = avdslist[avd].AsBool;
							}
						}

						source.myTransitions.Add(transition);
					} else {
						IndirectTransition transition = new IndirectTransition(source, target);
						transition.myProbability = tclass["Probability"].AsFloat;
						transition.mySourceState = source;
						transition.myTargetState = target;
						source.myTransitions.Add(transition);
					}
				}
			}
		}

		private void LoadRoles(JSONClass jc)
		{
			JSONArray rlist = jc["Roles"].AsArray;
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
				myRoles[r.myName] = r;
			}
		}

		private void LoadMessages(JSONClass jc)
		{
			JSONArray mlist = jc["Messages"].AsArray;

			for (int i=0; i<mlist.Count; ++i)
			{
				JSONClass mclass = mlist[i].AsObject;

				Animation targetAnimation;
				if(!myAnimations.TryGetValue(mclass["TargetAnimation"], out targetAnimation))
					continue;

				Layer targetLayer;
				if(!targetAnimation.myLayers.TryGetValue(mclass["TargetLayer"], out targetLayer))
					continue;

				State target;
				if(!targetLayer.myStates.TryGetValue(mclass["TargetState"], out target))
					continue;

				Message message = new Message(mclass["Name"]);
				message.myMessageString = mclass["MessageString"];

				JSONArray srcstlist = mclass["SourceStates"].AsArray;
				for (int j=0; j<srcstlist.Count; ++j)
				{
					JSONClass src = srcstlist[j].AsObject;
					Animation srcan = myAnimations[src["AnimationName"]];
					Layer srcly = srcan.myLayers[src["LayerName"]];
					State srcst = srcly.myStates[src["StateName"]];
					string qualStateName = $"{srcan.myName}.{srcly.myName}.{srcst.myName}";
					message.mySourceStates[qualStateName] = srcst;
				}

				message.myTargetState = target;

				myMessages[message.myName] = message;
			}
		}

		private void LoadAvoids(JSONClass jc)
		{
			JSONArray alist = jc["Avoids"].AsArray;

			for (int i=0; i<alist.Count; ++i)
			{
				JSONClass aclass = alist[i].AsObject;

				Avoid avoid = new Avoid(aclass["Name"]);
				avoid.myIsPlaced = aclass["IsPlaced"].AsBool;

				JSONArray avstlist = aclass["AvoidStates"].AsArray;
				for (int j=0; j<avstlist.Count; ++j)
				{
					JSONClass av = avstlist[j].AsObject;
					Animation avan = myAnimations[av["AnimationName"]];
					Layer avly = avan.myLayers[av["LayerName"]];
					State avst = avly.myStates[av["StateName"]];
					string qualStateName = $"{avan.myName}.{avly.myName}.{avst.myName}";
					avoid.myAvoidStates[qualStateName] = avst;
				}

				myAvoids[avoid.myName] = avoid;
			}
		}

		private void LoadFromVamTimeline(JSONClass jc, Transition transition) {
			Layer layer = transition.mySourceState.myLayer;

			JSONArray ctllist = jc["Clips"].AsArray[0]["Controllers"].AsArray;
			for (int i=0; i<ctllist.Count; ++i)
			{
				JSONClass ctl = ctllist[i].AsObject;

				ControlCapture capture = layer.myControlCaptures.FirstOrDefault(cc => String.Equals(cc.myName, ctl["Controller"]));
				if(capture == null)
					continue;

				ControlEntryAnchored startEntry = transition.mySourceState.myControlEntries[capture];
				ControlEntryAnchored endEntry = transition.myTargetState.myControlEntries[capture];

				ControlTimeline timeline = new ControlTimeline(capture);

				JSONArray xlist = ctl["X"].AsArray;
				JSONArray ylist = ctl["Y"].AsArray;
				JSONArray zlist = ctl["Z"].AsArray;
				JSONArray rxlist = ctl["RotX"].AsArray;
				JSONArray rylist = ctl["RotY"].AsArray;
				JSONArray rzlist = ctl["RotZ"].AsArray;
				JSONArray rwlist = ctl["RotW"].AsArray;

				startEntry.Capture(new ControlTransform(
					new Vector3(xlist[0]["v"].AsFloat, ylist[0]["v"].AsFloat, zlist[0]["v"].AsFloat),
					new Quaternion(rxlist[0]["v"].AsFloat, rylist[0]["v"].AsFloat, rzlist[0]["v"].AsFloat, rwlist[0]["v"].AsFloat)
				), startEntry.myControlCapture.GetPositionState(), startEntry.myControlCapture.GetRotationState());

				endEntry.Capture(new ControlTransform(
					new Vector3(xlist[xlist.Count-1]["v"].AsFloat, ylist[xlist.Count-1]["v"].AsFloat, zlist[xlist.Count-1]["v"].AsFloat),
					new Quaternion(rxlist[xlist.Count-1]["v"].AsFloat, rylist[xlist.Count-1]["v"].AsFloat, rzlist[xlist.Count-1]["v"].AsFloat, rwlist[xlist.Count-1]["v"].AsFloat)
				), endEntry.myControlCapture.GetPositionState(), endEntry.myControlCapture.GetRotationState());

				timeline.SetEndpoints(startEntry, endEntry);

				for (int j=1; j<xlist.Count-1; ++j)
				{
					float time = xlist[j]["t"].AsFloat/xlist[xlist.Count-1]["t"].AsFloat;
					ControlEntry ce = new ControlEntry(capture);
					ce.myTransform = new ControlTransform(
						new Vector3(xlist[j]["v"].AsFloat, ylist[j]["v"].AsFloat, zlist[j]["v"].AsFloat),
						new Quaternion(rxlist[j]["v"].AsFloat, rylist[j]["v"].AsFloat, rzlist[j]["v"].AsFloat, rwlist[j]["v"].AsFloat)
					);
					ControlTransform virtualAnchor = new ControlTransform(
						startEntry.myTransform, endEntry.myTransform, time
					);
					ce.myTransform = virtualAnchor.Inverse().Compose(ce.myTransform);
					timeline.AddKeyframe(new ControlKeyframe(time, ce));
				}

				timeline.ComputeControlPoints();
				transition.myControlTimelines[capture] = timeline;
			}

			JSONArray mphlist = jc["Clips"].AsArray[0]["FloatParams"].AsArray;
			for (int i=0; i<mphlist.Count; ++i)
			{
				JSONClass mph = mphlist[i].AsObject;

				MorphCapture capture = layer.myMorphCaptures.FirstOrDefault(cc => String.Equals(cc.myMorph.resolvedDisplayName, mph["Name"]));
				if(capture == null)
					continue;

				MorphTimeline timeline = new MorphTimeline(capture);

				JSONArray vlist = mph["Value"].AsArray;
				for (int j=0; j<vlist.Count; ++j)
				{
					MorphKeyframe keyframe;
					if(j==0) {
						keyframe = new MorphKeyframe("first", vlist[j]["Value"].AsFloat);
					} else if(j==vlist.Count-1) {
						keyframe = new MorphKeyframe("last", vlist[j]["Value"].AsFloat);
					} else {
						float t = vlist[j]["t"].AsFloat/vlist[vlist.Count-1]["t"].AsFloat;
						keyframe = new MorphKeyframe(t, vlist[j]["Value"].AsFloat);
					}

					timeline.AddKeyframe(keyframe);
				}
				transition.myMorphTimelines[capture] = timeline;
			}
		}

		private JSONClass SaveToVamTimeline(Transition transition) {
			JSONClass jc = new JSONClass();
			JSONArray clips = new JSONArray();
			JSONClass clip = new JSONClass();

			clip["AnimationName"] = "AnimationPoser Export";
			clip["AnimationLength"] = "1";
			clip["BlendDuration"] = "0.75";
			clip["Loop"] = "0";
			clip["NextAnimationRandomizeWeight"] = "1";
			clip["AutoTransitionPrevious"] = "0";
			clip["AutoTransitionNext"] = "0";
			clip["SyncTransitionTime"] = "0";
			clip["SyncTransitionTimeNL"] = "0";
			clip["EnsureQuaternionContinuity"] = "1";
			clip["AnimationLayer"] = "Main Layer";
			clip["Speed"] = "1";
			clip["Weight"] = "1";
			clip["Uninterruptible"] = "0";

			JSONArray ctllist = new JSONArray();
			foreach (var c in transition.myControlTimelines) {
				JSONClass ctl = new JSONClass();

				ctl["Controller"] = c.Key.myName;
				ctl["ControlPosition"] = "1";
				ctl["ControlRotation"] = "1";

				ControlTimeline timeline = c.Value;

				JSONArray xlist = new JSONArray();
				JSONArray ylist = new JSONArray();
				JSONArray zlist = new JSONArray();
				JSONArray rxlist = new JSONArray();
				JSONArray rylist = new JSONArray();
				JSONArray rzlist = new JSONArray();
				JSONArray rwlist = new JSONArray();
				for(int j=0; j<timeline.myKeyframes.Count; j++) {
					JSONClass xentry = new JSONClass();
					JSONClass yentry = new JSONClass();
					JSONClass zentry = new JSONClass();
					JSONClass rxentry = new JSONClass();
					JSONClass ryentry = new JSONClass();
					JSONClass rzentry = new JSONClass();
					JSONClass rwentry = new JSONClass();

					ControlKeyframe keyframe = timeline.myKeyframes[j] as ControlKeyframe;

					xentry["t"].AsFloat = keyframe.myTime;
					yentry["t"].AsFloat = keyframe.myTime;
					zentry["t"].AsFloat = keyframe.myTime;
					rxentry["t"].AsFloat = keyframe.myTime;
					ryentry["t"].AsFloat = keyframe.myTime;
					rzentry["t"].AsFloat = keyframe.myTime;
					rwentry["t"].AsFloat = keyframe.myTime;

					xentry["c"] = "3";
					yentry["c"] = "3";
					zentry["c"] = "3";
					rxentry["c"] = "3";
					ryentry["c"] = "3";
					rzentry["c"] = "3";
					rwentry["c"] = "3";

					xentry["v"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.x;
					yentry["v"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.y;
					zentry["v"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.z;
					rxentry["v"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.x;
					ryentry["v"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.y;
					rzentry["v"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.z;
					rwentry["v"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.w;

					if(keyframe.myControlPointIn != null) {
						xentry["i"].AsFloat = keyframe.myControlPointIn.myTransform.myPosition.x;
						yentry["i"].AsFloat = keyframe.myControlPointIn.myTransform.myPosition.y;
						zentry["i"].AsFloat = keyframe.myControlPointIn.myTransform.myPosition.z;
						rxentry["i"].AsFloat = keyframe.myControlPointIn.myTransform.myRotation.x;
						ryentry["i"].AsFloat = keyframe.myControlPointIn.myTransform.myRotation.y;
						rzentry["i"].AsFloat = keyframe.myControlPointIn.myTransform.myRotation.z;
						rwentry["i"].AsFloat = keyframe.myControlPointIn.myTransform.myRotation.w;
					} else {
						xentry["i"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.x;
						yentry["i"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.y;
						zentry["i"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.z;
						rxentry["i"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.x;
						ryentry["i"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.y;
						rzentry["i"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.z;
						rwentry["i"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.w;
					}

					if(keyframe.myControlPointOut != null) {
						xentry["o"].AsFloat = keyframe.myControlPointOut.myTransform.myPosition.x;
						yentry["o"].AsFloat = keyframe.myControlPointOut.myTransform.myPosition.y;
						zentry["o"].AsFloat = keyframe.myControlPointOut.myTransform.myPosition.z;
						rxentry["o"].AsFloat = keyframe.myControlPointOut.myTransform.myRotation.x;
						ryentry["o"].AsFloat = keyframe.myControlPointOut.myTransform.myRotation.y;
						rzentry["o"].AsFloat = keyframe.myControlPointOut.myTransform.myRotation.z;
						rwentry["o"].AsFloat = keyframe.myControlPointOut.myTransform.myRotation.w;
					} else {
						xentry["o"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.x;
						yentry["o"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.y;
						zentry["o"].AsFloat = keyframe.myControlEntry.myTransform.myPosition.z;
						rxentry["o"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.x;
						ryentry["o"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.y;
						rzentry["o"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.z;
						rwentry["o"].AsFloat = keyframe.myControlEntry.myTransform.myRotation.w;
					}

					xlist.Add(xentry);
					ylist.Add(yentry);
					zlist.Add(zentry);
					rxlist.Add(rxentry);
					rylist.Add(ryentry);
					rzlist.Add(rzentry);
					rwlist.Add(rwentry);
				}

				ctl["X"] = xlist;
				ctl["Y"] = ylist;
				ctl["Z"] = zlist;
				ctl["RotX"] = rxlist;
				ctl["RotY"] = rylist;
				ctl["RotZ"] = rzlist;
				ctl["RotW"] = rwlist;

				ctllist.Add(ctl);
			}
			clip["Controllers"] = ctllist;

			JSONArray mphlist = new JSONArray();
			foreach (var c in transition.myMorphTimelines) {
				JSONClass mph = new JSONClass();

				mph["Storable"] = "geometry";
				mph["Name"] = c.Key.myMorph.resolvedDisplayName;

				MorphTimeline timeline = c.Value;

				JSONArray vlist = new JSONArray();
				for(int j=0; j<timeline.myKeyframes.Count; j++) {
					JSONClass entry = new JSONClass();

					MorphKeyframe keyframe = timeline.myKeyframes[j] as MorphKeyframe;

					entry["t"].AsFloat = keyframe.myTime;
					entry["c"] = "3";
					entry["v"].AsFloat = keyframe.myMorphEntry;

					if(keyframe.myControlPointIn != null) {
						entry["i"].AsFloat = keyframe.myControlPointIn;
					} else {
						entry["i"].AsFloat = keyframe.myMorphEntry;
					}

					if(keyframe.myControlPointOut != null) {
						entry["o"].AsFloat = keyframe.myControlPointOut;
					} else {
						entry["o"].AsFloat = keyframe.myMorphEntry;
					}

					vlist.Add(entry);
				}

				mph["Value"] = vlist;

				mphlist.Add(mph);
			}
			clip["FloatParams"] = mphlist;

			clips.Add(clip);
			jc["Clips"] = clips;
			jc["AtomType"] = "Person";
			return jc;
		}
    }
}
