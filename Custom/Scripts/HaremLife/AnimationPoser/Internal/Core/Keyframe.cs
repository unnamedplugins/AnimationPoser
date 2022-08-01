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
		private class Keyframe
		{
			public float myTime;
			public bool myIsFirst = false;
			public bool myIsLast = false;

			public Keyframe(string firstOrLast)
			{
				if(String.Equals(firstOrLast, "first")) {
					myTime = 0;
					myIsFirst = true;
				}
				if(String.Equals(firstOrLast, "last")) {
					myTime = 1;
					myIsLast = true;
				}
			}

			public Keyframe(float time) {
				myTime = time;
			}
		}
    }
}