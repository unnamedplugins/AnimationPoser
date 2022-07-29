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
		private class Role : AnimationObject
		{
			public Atom myPerson;

			public Role(string name) : base(name) {
			}
		}
    }
}