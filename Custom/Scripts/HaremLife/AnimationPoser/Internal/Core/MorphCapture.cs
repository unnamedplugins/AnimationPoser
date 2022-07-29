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
		private class MorphCapture
		{
			public string mySID;
			public DAZMorph myMorph;
			public DAZCharacterSelector.Gender myGender;
			public bool myApply = true;

			// used when adding a capture
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector.Gender gender, DAZMorph morph)
			{
				myMorph = morph;
				myGender = gender;
				mySID = plugin.GenerateMorphsSID(gender == DAZCharacterSelector.Gender.Female);
			}

			// legacy handling of old qualifiedName where the order was reversed
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector geometry, string oldQualifiedName)
			{
				bool isFemale = oldQualifiedName.StartsWith("Female#");
				if (!isFemale && !oldQualifiedName.StartsWith("Male#")){
					return;
				}
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				string morphUID = oldQualifiedName.Substring(isFemale ? 7 : 5);
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;

				mySID = plugin.GenerateMorphsSID(isFemale);
			}

			// legacy handling before there were ShortIDs
			public MorphCapture(AnimationPoser plugin, DAZCharacterSelector geometry, string morphUID, bool isFemale)
			{
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;

				mySID = plugin.GenerateMorphsSID(isFemale);
			}

			// used when loading from JSON
			public MorphCapture(DAZCharacterSelector geometry, string morphUID, string morphSID)
			{
				bool isFemale = morphSID.Length > 0 && morphSID[0] == 'F';
				GenerateDAZMorphsControlUI morphsControl = isFemale ? geometry.morphsControlFemaleUI : geometry.morphsControlMaleUI;
				myMorph = morphsControl.GetMorphByUid(morphUID);
				myGender = isFemale ? DAZCharacterSelector.Gender.Female : DAZCharacterSelector.Gender.Male;
				mySID = morphSID;
			}

			public void CaptureEntry(State state)
			{
				state.myMorphEntries[this] = myMorph.morphValue;
			}

			public bool IsValid()
			{
				return myMorph != null && mySID != null;
			}
		}
    }
}