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
		private class Avoid : AnimationObject
		{
			public Dictionary<string, State> myAvoidStates = new Dictionary<string, State>();
			public bool myIsPlaced = false;

			public Avoid(string name) : base(name) {
			}
		}
    }
}