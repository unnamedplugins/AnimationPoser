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
				mySourceState.InitializeEntries();
				myTargetState.InitializeEntries();
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
    }
}