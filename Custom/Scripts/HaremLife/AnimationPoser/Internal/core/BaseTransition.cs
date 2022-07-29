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
		private class BaseTransition
		{
			public State mySourceState;
			public State myTargetState;
			public float myProbability;
		}
    }
}