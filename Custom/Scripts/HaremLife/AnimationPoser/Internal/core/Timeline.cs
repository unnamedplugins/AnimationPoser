namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private class Timeline {
			public List<Keyframe> myKeyframes = new List<Keyframe>();

			public void AddKeyframe(Keyframe keyframe) {
				int i;
				for(i=0; i<myKeyframes.Count; i++) {
					if(myKeyframes[i].myTime > keyframe.myTime)
						break;
				}
				myKeyframes.Insert(i, keyframe);
				ComputeControlPoints();
			}

			public void RemoveKeyframe(Keyframe keyframe) {
				myKeyframes.Remove(keyframe);
				ComputeControlPoints();
			}

			public void Split(float v, float transitionDuration, Timeline newTimeline) {
				for(int i=0; i<myKeyframes.Count; i++) {
					if(Math.Abs(v - myKeyframes[i].myTime) * transitionDuration < 0.01)
						RemoveKeyframe(myKeyframes[i]);
				}

				int splitIndex;
				for(splitIndex=0; splitIndex<myKeyframes.Count; splitIndex++) {
					if(myKeyframes[splitIndex].myTime > v)
						break;
				}
				int n = myKeyframes.Count;

				for (int i=0; i<splitIndex; ++i) {
					myKeyframes[i].myTime = myKeyframes[i].myTime / v;
				}

				for (int i=splitIndex; i<n; ++i) {
					myKeyframes[i].myTime = (myKeyframes[i].myTime - v) / (1-v);
					newTimeline.myKeyframes.Add(myKeyframes[i]);
				}

				for (int i=n-1; i>=splitIndex; --i) {
					myKeyframes.Remove(myKeyframes[i]);
				}

				myKeyframes.Remove(myKeyframes[0]);
				newTimeline.myKeyframes.Remove(newTimeline.myKeyframes[n-splitIndex-1]);
			}

			public virtual void ComputeControlPoints() {
			}

			public virtual void CaptureKeyframe(float time) {
			}

			public virtual void UpdateKeyframe(Keyframe keyframe) {
			}
		}
    }
}