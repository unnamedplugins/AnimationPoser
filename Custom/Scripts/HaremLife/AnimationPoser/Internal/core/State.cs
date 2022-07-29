namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
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

			public void InitializeEntries() {
				foreach(var ce in myControlEntries)
					ce.Value.Initialize();
			}
		}
    }
}