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
		private class MorphKeyframe : Keyframe
		{
			public float myMorphEntry;
			public float myControlPointIn;
			public float myControlPointOut;

			public MorphKeyframe(string firstOrLast, float entry) : base(firstOrLast)
			{
				myMorphEntry = entry;
			}

			public MorphKeyframe(float time, float entry) : base(time)
			{
				myMorphEntry = entry;
			}

		}
    }
}