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

			public MorphKeyframe(string firstOrLast, float entry)
			{
				if(String.Equals(firstOrLast, "first")) {
					myTime = 0;
					myIsFirst = true;
				}
				if(String.Equals(firstOrLast, "last")) {
					myTime = 1;
					myIsLast = true;
				}
				myMorphEntry = entry;
			}

			public MorphKeyframe(float time, float entry)
			{
				myTime = time;
				myMorphEntry = entry;
			}
		}
    }
}