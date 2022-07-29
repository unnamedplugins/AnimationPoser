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