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
    }
}