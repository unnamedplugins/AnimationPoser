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
		private class ControlKeyframe : Keyframe
		{
			public ControlEntry myControlEntry;
			public ControlEntry myControlPointIn;
			public ControlEntry myControlPointOut;

			public ControlKeyframe(string firstOrLast, ControlEntry entry)
			{
				if(String.Equals(firstOrLast, "first")) {
					myTime = 0;
					myIsFirst = true;
				}
				if(String.Equals(firstOrLast, "last")) {
					myTime = 1;
					myIsLast = true;
				}
				myControlEntry = entry;
			}

			public ControlKeyframe(float time, ControlEntry entry)
			{
				myTime = time;
				myControlEntry = entry;
			}
		}
    }
}