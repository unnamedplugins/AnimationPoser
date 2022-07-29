namespace HaremLife
{
	public partial class AnimationPoser : MVRScript
	{
		private class IndirectTransition : BaseTransition
		{
			public IndirectTransition(State sourceState, State targetState)
			{
				mySourceState = sourceState;
				myTargetState = targetState;
				myProbability = targetState.myDefaultProbability;
			}

			public IndirectTransition(IndirectTransition t) {
				mySourceState = t.mySourceState;
				myTargetState = t.myTargetState;
				myProbability = t.myProbability;
			}
		}
    }
}