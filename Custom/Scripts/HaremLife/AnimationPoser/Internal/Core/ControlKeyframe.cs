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

			public ControlKeyframe(string firstOrLast, ControlEntry entry) : base(firstOrLast)
			{
				myControlEntry = entry;
			}

			public ControlKeyframe(float time, ControlEntry entry) : base(time)
			{
				myControlEntry = entry;
			}
		}
    }
}